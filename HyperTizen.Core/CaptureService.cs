using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace HyperTizen.Core
{
    // Result of a preview request: colors, or the reason there are none.
    public sealed class PreviewFrame
    {
        public PreviewFrame(Rgb10[] colors, string error)
        {
            Colors = colors;
            Error = error;
        }

        public Rgb10[] Colors { get; }
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
        private volatile Rgb10[] _lastFrame;
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

                return HasFreshFrame(timeout)
                    ? new PreviewFrame(_lastFrame, null)
                    : new PreviewFrame(null, NotReturningColors);
            }

            var outstanding = _oneShot;
            if (outstanding != null && !outstanding.IsCompleted) return new PreviewFrame(null, NotReturningColors);
            var stuck = _stuckLoop;
            if (stuck != null && !stuck.IsCompleted) return new PreviewFrame(null, NotReturningColors);

            Task<PreviewFrame> capture = Task.Run(() => CaptureOnce());
            _oneShot = capture;
            if (!await CompletesWithinAsync(capture, timeout).ConfigureAwait(false))
                return new PreviewFrame(null, NotReturningColors);

            try
            {
                return await capture.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.Error("Preview capture failed", ex);
                return new PreviewFrame(null, ex.Message);
            }
        }

        private PreviewFrame CaptureOnce()
        {
            if (!_captureLock.Wait(TimeSpan.FromMilliseconds(500))) return new PreviewFrame(null, NotReturningColors);
            try
            {
                if (!InitializeCapturer()) return new PreviewFrame(null, NotSupported);

                Rgb10[] colors = _capturer.Capture();
                RememberFrame(colors);
                return new PreviewFrame(colors, null);
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

        private void RememberFrame(Rgb10[] colors)
        {
            _lastFrame = colors;
            _lastFrameTick = Environment.TickCount;
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
            while (!cancellation.IsCancellationRequested)
            {
                try
                {
                    if (_paused || !_client.IsConnected)
                    {
                        // Nothing to send to yet; frames are not produced while disconnected or paused.
                        await Task.Delay(_paused ? 50 : 100, cancellation).ConfigureAwait(false);
                        continue;
                    }

                    var frameTime = Stopwatch.StartNew();
                    Rgb10[] colors;
                    await _captureLock.WaitAsync(cancellation).ConfigureAwait(false);
                    try
                    {
                        colors = _capturer.Capture();
                    }
                    finally
                    {
                        _captureLock.Release();
                    }
                    RememberFrame(colors);
                    if (_paused) continue;

                    // A priority change leaves the last image at the old priority; remove it first.
                    byte priority = _options.Priority;
                    if (lastPriority >= 0 && lastPriority != priority)
                        await _client.SendClearAsync((byte)lastPriority).ConfigureAwait(false);
                    lastPriority = priority;

                    if (await _client.SendImageAsync(FrameEncoder.ToBase64Png(colors), priority).ConfigureAwait(false))
                        _onFrameSent?.Invoke();

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
