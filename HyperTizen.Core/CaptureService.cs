using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace HyperTizen.Core
{
    // Result of a preview request: colors and the layout they were captured with, or the reason
    // there are none.
    public sealed class PreviewFrame
    {
        public PreviewFrame(Rgb10[] colors, CaptureLayout layout, string error)
        {
            Colors = colors;
            Layout = layout;
            Error = error;
        }

        public Rgb10[] Colors { get; }
        public CaptureLayout Layout { get; }
        public string Error { get; }
    }

    // Owns the capture loop: capture a frame, encode it, send it to Hyperion, repeat.
    public sealed class CaptureService
    {
        public const string NotReturningColors = "The TV is not returning colors.";
        public const string NotSupported = "Screen capture is not supported on this TV.";

        private const int SupportUnknown = 0;
        private const int Supported = 1;
        private const int Unsupported = 2;

        private readonly IScreenCapturer _capturer;
        private readonly HyperionClient _client;
        private readonly ILog _log;
        private readonly CaptureOptions _options;
        private readonly Action _onFrameSent;
        private readonly SemaphoreSlim _transition = new SemaphoreSlim(1, 1);
        // Every call into the capturer holds this, so the loop and a preview never capture at once.
        private readonly SemaphoreSlim _captureLock = new SemaphoreSlim(1, 1);
        private readonly TimeSpan _stopTimeout;

        private CancellationTokenSource _cancellation;
        private volatile Task _loop;
        private volatile Task _stuckLoop;
        private volatile Task<PreviewFrame> _oneShot;
        private volatile PreviewFrame _lastFrame;

        // Timing of the frames sent lately, for the status reply.
        private const int TimingWindowMs = 5000;
        private static readonly Stopwatch Clock = Stopwatch.StartNew();
        private readonly object _timingGate = new object();
        private readonly Queue<long> _sentAt = new Queue<long>();
        private int _lastFrameMs;
        private long _lastSentAt;
        private volatile int _lastFrameTick;
        private volatile int _support = SupportUnknown;
        private volatile bool _paused;

        public CaptureService(
            IScreenCapturer capturer,
            HyperionClient client,
            ILog log,
            TimeSpan? stopTimeout = null,
            CaptureOptions options = null,
            Action onFrameSent = null)
        {
            _capturer = capturer ?? throw new ArgumentNullException(nameof(capturer));
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _stopTimeout = stopTimeout ?? TimeSpan.FromSeconds(3);
            _options = options ?? new CaptureOptions();
            _onFrameSent = onFrameSent;
        }

        public bool IsRunning
        {
            get { return _loop != null; }
        }

        // While true the loop sends nothing; used by the LED test.
        public bool Paused
        {
            get { return _paused; }
            set { _paused = value; }
        }

        public string State
        {
            get
            {
                if (_support == Unsupported) return "unsupported";
                if (_loop != null) return "running";
                return _support == SupportUnknown ? "unknown" : "stopped";
            }
        }

        // How long the latest frame took, from the start of capture to the image being sent.
        // Null when no frame was sent in the last few seconds.
        public int? FrameMs
        {
            get
            {
                lock (_timingGate)
                {
                    ForgetOldFrames();
                    return _sentAt.Count == 0 ? (int?)null : _lastFrameMs;
                }
            }
        }

        // Frames sent per second, measured across the frames of the last few seconds. Counting them
        // against the whole window instead would read too low just after capture starts.
        public double Fps
        {
            get
            {
                lock (_timingGate)
                {
                    ForgetOldFrames();
                    if (_sentAt.Count < 2) return 0;
                    long span = _lastSentAt - _sentAt.Peek();
                    return span <= 0 ? 0 : Math.Round((_sentAt.Count - 1) * 1000.0 / span, 1);
                }
            }
        }

        public async Task<bool> StartAsync()
        {
            await _transition.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_loop != null) return true;

                if (_stuckLoop != null)
                {
                    if (!_stuckLoop.IsCompleted)
                    {
                        _log.Error("Capture cannot start: the previous capture is still running");
                        return false;
                    }
                    _stuckLoop = null;
                }

                if (!await _captureLock.WaitAsync(_stopTimeout).ConfigureAwait(false))
                {
                    _log.Error("Capture cannot start: the previous capture is still running");
                    return false;
                }

                bool supported;
                try
                {
                    supported = InitializeCapturer();
                }
                finally
                {
                    _captureLock.Release();
                }

                if (!supported)
                {
                    _log.Error("Screen capture is not supported on this device");
                    return false;
                }

                _client.Start();
                _cancellation = new CancellationTokenSource();
                CancellationToken token = _cancellation.Token;
                _loop = Task.Run(() => RunAsync(token));
                _log.Info("Capture started");
                return true;
            }
            finally
            {
                _transition.Release();
            }
        }

        public async Task StopAsync()
        {
            await _transition.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_loop == null) return;

                Task loop = _loop;
                _cancellation.Cancel();

                // A capture or a send that never returns must not block stopping forever.
                bool stopped = await CompletesWithinAsync(loop, _stopTimeout).ConfigureAwait(false);
                if (stopped)
                {
                    await _client.SendClearAsync(_options.Priority).ConfigureAwait(false);
                }
                else
                {
                    _log.Error("Capture loop did not stop within " + _stopTimeout.TotalSeconds + " s; closing the Hyperion connection anyway");
                }

                // Closing the connection also releases a loop that is stuck sending.
                await _client.StopAsync().ConfigureAwait(false);
                if (!stopped) stopped = await CompletesWithinAsync(loop, TimeSpan.FromSeconds(1)).ConfigureAwait(false);

                if (stopped) _cancellation.Dispose();
                else _stuckLoop = loop;
                _cancellation = null;
                _loop = null;
                _log.Info("Capture stopped");
            }
            finally
            {
                _transition.Release();
            }
        }

        // The most recent frame while the loop is sending, otherwise one frame captured for this call.
        public async Task<PreviewFrame> GetPreviewAsync(TimeSpan timeout)
        {
            if (_loop != null && _client.IsConnected && !_paused)
            {
                var waited = Stopwatch.StartNew();
                while (!HasFreshFrame(timeout) && waited.Elapsed < timeout)
                    await Task.Delay(50).ConfigureAwait(false);

                PreviewFrame latest = _lastFrame;
                return HasFreshFrame(timeout) && latest != null
                    ? latest
                    : new PreviewFrame(null, null, NotReturningColors);
            }

            var stuck = _stuckLoop;
            if (stuck != null && !stuck.IsCompleted) return new PreviewFrame(null, null, NotReturningColors);

            // A capture still running from an earlier request is waited for, not started again:
            // with many zones one capture can take longer than a single request waits.
            Task<PreviewFrame> capture = _oneShot;
            if (capture == null || capture.IsCompleted)
            {
                capture = Task.Run(() => CaptureOnce());
                _oneShot = capture;
            }
            if (!await CompletesWithinAsync(capture, timeout).ConfigureAwait(false))
                return new PreviewFrame(null, null, NotReturningColors);

            try
            {
                return await capture.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.Error("Preview capture failed", ex);
                return new PreviewFrame(null, null, ex.Message);
            }
        }

        private PreviewFrame CaptureOnce()
        {
            if (!_captureLock.Wait(TimeSpan.FromMilliseconds(500))) return new PreviewFrame(null, null, NotReturningColors);
            try
            {
                // Previews are polled; asking the device again each time would repeat its log lines and
                // notifications. Starting capture is what checks an unsupported device again.
                if (_support == Unsupported) return new PreviewFrame(null, null, NotSupported);
                if (_support == SupportUnknown && !InitializeCapturer()) return new PreviewFrame(null, null, NotSupported);

                CaptureLayout layout = _options.Layout;
                return RememberFrame(CapturePoints(layout.Points), layout);
            }
            finally
            {
                _captureLock.Release();
            }
        }

        // The caller holds _captureLock.
        private bool InitializeCapturer()
        {
            bool supported;
            try
            {
                supported = _capturer.Initialize();
            }
            catch (Exception ex)
            {
                _log.Error("Capturer initialization failed", ex);
                supported = false;
            }

            _support = supported ? Supported : Unsupported;
            return supported;
        }

        // The caller holds _captureLock.
        private Rgb10[] CapturePoints(IReadOnlyList<CapturePoint> points)
        {
            Rgb10[] colors = _capturer.Capture(points);
            int returned = colors == null ? 0 : colors.Length;
            if (returned != points.Count)
                throw new InvalidOperationException(
                    "The capturer returned " + returned + " colors for " + points.Count + " points.");
            return colors;
        }

        private PreviewFrame RememberFrame(Rgb10[] colors, CaptureLayout layout)
        {
            var frame = new PreviewFrame(colors, layout, null);
            _lastFrame = frame;
            _lastFrameTick = Environment.TickCount;
            return frame;
        }

        private void RecordSent(int frameMs)
        {
            lock (_timingGate)
            {
                _lastFrameMs = frameMs;
                _lastSentAt = Clock.ElapsedMilliseconds;
                _sentAt.Enqueue(_lastSentAt);
                ForgetOldFrames();
            }
        }

        // The caller holds _timingGate.
        private void ForgetOldFrames()
        {
            long oldest = Clock.ElapsedMilliseconds - TimingWindowMs;
            while (_sentAt.Count > 0 && _sentAt.Peek() < oldest) _sentAt.Dequeue();
        }

        private bool HasFreshFrame(TimeSpan maxAge)
        {
            return _lastFrame != null && unchecked(Environment.TickCount - _lastFrameTick) < maxAge.TotalMilliseconds;
        }

        private static async Task<bool> CompletesWithinAsync(Task task, TimeSpan timeout)
        {
            return await Task.WhenAny(task, Task.Delay(timeout)).ConfigureAwait(false) == task;
        }

        private async Task RunAsync(CancellationToken cancellation)
        {
            int lastPriority = -1;
            // The frame being kept up to date one batch at a time, and where in the layout's
            // spread order its next batch starts.
            PreviewFrame current = null;
            int next = 0;
            while (!cancellation.IsCancellationRequested)
            {
                try
                {
                    if (_paused || !_client.IsConnected)
                    {
                        // Nothing to send to yet; frames are not produced while disconnected or paused.
                        // The colors kept from before are out of date by the time capture goes on.
                        current = null;
                        await Task.Delay(_paused ? 50 : 100, cancellation).ConfigureAwait(false);
                        continue;
                    }

                    var frameTime = Stopwatch.StartNew();
                    // Read once: the whole frame uses the layout it started with.
                    CaptureLayout layout = _options.Layout;
                    // A device that measures a few points at a time waits between batches. Sending
                    // after every batch, with the other colors as they were, updates the LEDs that
                    // much more often than sending once all points are measured.
                    int batch = _capturer.BatchSize;
                    bool whole = current == null || current.Layout != layout || batch <= 0 || batch >= layout.Points.Length;
                    Rgb10[] colors;
                    await _captureLock.WaitAsync(cancellation).ConfigureAwait(false);
                    try
                    {
                        if (whole)
                        {
                            colors = CapturePoints(layout.Points);
                            next = 0;
                        }
                        else
                        {
                            // In the layout's spread order, so that a change of picture shows on all
                            // edges at once instead of travelling around the screen.
                            int count = Math.Min(batch, layout.Points.Length - next);
                            var points = new CapturePoint[count];
                            for (int i = 0; i < count; i++) points[i] = layout.Points[layout.SpreadOrder[next + i]];
                            Rgb10[] measured = CapturePoints(points);
                            // A copy: the frame a preview already holds must not change.
                            colors = (Rgb10[])current.Colors.Clone();
                            for (int i = 0; i < count; i++) colors[layout.SpreadOrder[next + i]] = measured[i];
                            next = (next + count) % layout.Points.Length;
                        }
                    }
                    finally
                    {
                        _captureLock.Release();
                    }
                    current = RememberFrame(colors, layout);
                    if (_paused) continue;

                    // A priority change leaves the last image at the old priority; remove it first.
                    byte priority = _options.Priority;
                    if (lastPriority >= 0 && lastPriority != priority)
                        await _client.SendClearAsync((byte)lastPriority).ConfigureAwait(false);
                    lastPriority = priority;

                    if (await _client.SendImageAsync(FrameEncoder.ToBase64Png(colors, layout), priority).ConfigureAwait(false))
                    {
                        RecordSent((int)frameTime.ElapsedMilliseconds);
                        _onFrameSent?.Invoke();
                    }

                    int maxFps = _options.MaxFps;
                    if (maxFps > 0)
                    {
                        int remaining = 1000 / maxFps - (int)frameTime.ElapsedMilliseconds;
                        if (remaining > 0) await Task.Delay(remaining, cancellation).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    _log.Error("Capture failed", ex);
                    current = null;
                    try
                    {
                        await Task.Delay(1000, cancellation).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                }
            }
        }
    }
}
