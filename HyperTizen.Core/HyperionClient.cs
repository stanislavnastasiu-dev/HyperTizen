using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace HyperTizen.Core
{
    // Keeps a WebSocket connection to Hyperion / HyperHDR open while started, and sends commands over it.
    public sealed class HyperionClient
    {
        private static readonly TimeSpan[] DefaultBackoff =
        {
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(10)
        };

        private readonly ILog _log;
        private readonly TimeSpan[] _backoff;
        private readonly object _gate = new object();
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);

        private volatile string _uri;
        private volatile ClientWebSocket _socket;
        private volatile bool _closing;
        private CancellationTokenSource _stop;
        private CancellationTokenSource _attempt;
        private volatile Task _loop;

        private readonly Action _onConnected;

        public HyperionClient(ILog log, TimeSpan[] backoff = null, Action onConnected = null)
        {
            _onConnected = onConnected;
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _backoff = backoff != null && backoff.Length > 0 ? backoff : DefaultBackoff;
        }

        public bool IsConnected
        {
            get
            {
                var socket = _socket;
                return socket != null && socket.State == WebSocketState.Open;
            }
        }

        public bool IsStarted
        {
            get { return _loop != null; }
        }

        public bool HasServer
        {
            get { return _uri != null; }
        }

        public void SetServer(string uri)
        {
            lock (_gate)
            {
                _uri = string.IsNullOrWhiteSpace(uri) ? null : uri;
                // Drops the current connection or wait so the loop picks up the new address.
                _attempt?.Cancel();
            }
        }

        public void Start()
        {
            lock (_gate)
            {
                if (_loop != null) return;
                _closing = false;
                _stop = new CancellationTokenSource();
                CancellationToken token = _stop.Token;
                _loop = Task.Run(() => RunAsync(token));
            }
        }

        public async Task StopAsync()
        {
            Task loop;
            CancellationTokenSource stop;
            lock (_gate)
            {
                loop = _loop;
                stop = _stop;
                _loop = null;
                _stop = null;
            }
            if (loop == null) return;

            // Close gracefully first so messages already sent are delivered.
            _closing = true;
            var socket = _socket;
            if (socket != null && socket.State == WebSocketState.Open)
            {
                try
                {
                    using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2)))
                    {
                        await _sendLock.WaitAsync(timeout.Token).ConfigureAwait(false);
                        try
                        {
                            await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, string.Empty, timeout.Token).ConfigureAwait(false);
                        }
                        finally
                        {
                            _sendLock.Release();
                        }
                    }
                    await Task.WhenAny(loop, Task.Delay(TimeSpan.FromSeconds(2))).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _log.Error("Closing the Hyperion connection failed", ex);
                }
            }

            stop.Cancel();
            await loop.ConfigureAwait(false);
            stop.Dispose();
        }

        public Task<bool> SendImageAsync(string base64Png, byte priority = 99)
        {
            return SendAsync(JsonConvert.SerializeObject(new ImageCommand(base64Png, priority)));
        }

        public Task<bool> SendClearAsync(byte priority = 99)
        {
            return SendAsync(JsonConvert.SerializeObject(new ClearCommand(priority)));
        }

        public Task<bool> SendColorAsync(int r, int g, int b, byte priority = 99)
        {
            return SendAsync(JsonConvert.SerializeObject(new ColorCommand(r, g, b, priority)));
        }

        private async Task<bool> SendAsync(string json)
        {
            var socket = _socket;
            if (socket == null || socket.State != WebSocketState.Open) return false;

            byte[] bytes = Encoding.UTF8.GetBytes(json);
            await _sendLock.WaitAsync().ConfigureAwait(false);
            try
            {
                await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex)
            {
                _log.Error("Sending to Hyperion failed", ex);
                return false;
            }
            finally
            {
                _sendLock.Release();
            }
        }

        private async Task RunAsync(CancellationToken stop)
        {
            int failures = 0;
            TimeSpan wait = TimeSpan.Zero;

            while (!stop.IsCancellationRequested)
            {
                string uri;
                CancellationTokenSource attempt;
                lock (_gate)
                {
                    uri = _uri;
                    _attempt = attempt = CancellationTokenSource.CreateLinkedTokenSource(stop);
                }

                try
                {
                    if (wait > TimeSpan.Zero) await Task.Delay(wait, attempt.Token).ConfigureAwait(false);
                    if (uri == null) await Task.Delay(Timeout.Infinite, attempt.Token).ConfigureAwait(false);

                    using (var socket = new ClientWebSocket())
                    {
                        await socket.ConnectAsync(new Uri(uri), attempt.Token).ConfigureAwait(false);
                        _socket = socket;
                        failures = 0;
                        _log.Info("Connected to " + uri);
                        _onConnected?.Invoke();
                        try
                        {
                            await DrainAsync(socket, attempt.Token).ConfigureAwait(false);
                        }
                        finally
                        {
                            _socket = null;
                        }
                    }

                    _log.Info("Disconnected from " + uri);
                    if (_closing) return;
                    wait = _backoff[0];
                    failures = 1;
                }
                catch (OperationCanceledException)
                {
                    if (stop.IsCancellationRequested) return;
                    // The server address changed: connect to the new one right away.
                    wait = TimeSpan.Zero;
                    failures = 0;
                }
                catch (Exception ex)
                {
                    if (stop.IsCancellationRequested) return;
                    _log.Error("Hyperion connection to '" + uri + "' failed", ex);
                    wait = _backoff[Math.Min(failures, _backoff.Length - 1)];
                    failures++;
                }
                finally
                {
                    lock (_gate)
                    {
                        if (_attempt == attempt) _attempt = null;
                        attempt.Dispose();
                    }
                }
            }
        }

        // Hyperion replies to every command; the replies are read and discarded so the socket stays healthy.
        private static async Task DrainAsync(ClientWebSocket socket, CancellationToken cancellation)
        {
            var buffer = new byte[4096];
            while (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseSent)
            {
                var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellation).ConfigureAwait(false);
                if (result.MessageType != WebSocketMessageType.Close) continue;

                if (socket.State == WebSocketState.CloseReceived)
                    await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None).ConfigureAwait(false);
                return;
            }
        }
    }
}
