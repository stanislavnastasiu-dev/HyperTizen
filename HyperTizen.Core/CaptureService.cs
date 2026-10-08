using System;
using System.Threading;
using System.Threading.Tasks;

namespace HyperTizen.Core
{
    // Owns the capture loop: capture a frame, encode it, send it to Hyperion, repeat.
    public sealed class CaptureService
    {
        private readonly IScreenCapturer _capturer;
        private readonly HyperionClient _client;
        private readonly ILog _log;
        private readonly SemaphoreSlim _transition = new SemaphoreSlim(1, 1);

        private CancellationTokenSource _cancellation;
        private volatile Task _loop;

        public CaptureService(IScreenCapturer capturer, HyperionClient client, ILog log)
        {
            _capturer = capturer ?? throw new ArgumentNullException(nameof(capturer));
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        public bool IsRunning
        {
            get { return _loop != null; }
        }

        public async Task<bool> StartAsync()
        {
            await _transition.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_loop != null) return true;

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

                _cancellation.Cancel();
                await _loop.ConfigureAwait(false);
                _cancellation.Dispose();
                _cancellation = null;
                _loop = null;

                await _client.SendClearAsync().ConfigureAwait(false);
                await _client.StopAsync().ConfigureAwait(false);
                _log.Info("Capture stopped");
            }
            finally
            {
                _transition.Release();
            }
        }

        private async Task RunAsync(CancellationToken cancellation)
        {
            while (!cancellation.IsCancellationRequested)
            {
                try
                {
                    if (!_client.IsConnected)
                    {
                        // Nothing to send to yet; frames are not produced while disconnected.
                        await Task.Delay(100, cancellation).ConfigureAwait(false);
                        continue;
                    }

                    Rgb10[] colors = _capturer.Capture();
                    await _client.SendImageAsync(FrameEncoder.ToBase64Png(colors)).ConfigureAwait(false);
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
