using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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

        private const int MaxReplyBytes = 64 * 1024;
        private static readonly Stopwatch Clock = Stopwatch.StartNew();

        private readonly Action _onConnected;
        // What the server refused lately, by the command it answered. A command it accepts again
        // is forgotten.
        private readonly ConcurrentDictionary<string, string> _refused = new ConcurrentDictionary<string, string>();
        private readonly long _replyTimeoutMs;
        // When the oldest command still waiting for its reply was sent; 0 when none is waiting.
        private long _awaitingSince;
        // A server that never answers at all is left alone; one that did and then stops is gone.
        private volatile bool _replied;
        private volatile int _instance;

        public HyperionClient(ILog log, TimeSpan[] backoff = null, Action onConnected = null, TimeSpan? replyTimeout = null)
        {
            _onConnected = onConnected;
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _backoff = backoff != null && backoff.Length > 0 ? backoff : DefaultBackoff;
            _replyTimeoutMs = (long)(replyTimeout ?? TimeSpan.FromSeconds(5)).TotalMilliseconds;
        }

        // Why the server refuses what it is sent, for example "No Authorization"; null when it does not.
        public string Rejection
        {
            get { return _refused.Values.FirstOrDefault(); }
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
                _refused.Clear();
                // Drops the current connection or wait so the loop picks up the new address.
                _attempt?.Cancel();
            }
        }

        // The LED instance of the server that commands go to; 0 is its first.
        public async Task SetInstanceAsync(int instance)
        {
            if (_instance == instance) return;
            _instance = instance;
            await SendAsync(JsonConvert.SerializeObject(new InstanceCommand(instance))).ConfigureAwait(false);
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

        private Task<bool> SendAsync(string json)
        {
            return SendAsync(_socket, json);
        }

        private async Task<bool> SendAsync(ClientWebSocket socket, string json)
        {
            if (socket == null || socket.State != WebSocketState.Open) return false;

            // A server that lost power or its network never closes the connection; it only stops
            // answering. Dropping the connection makes the loop connect again.
            long now = Clock.ElapsedMilliseconds + 1;
            long since = Interlocked.Read(ref _awaitingSince);
            if (_replied && since != 0 && now - since > _replyTimeoutMs)
            {
                _log.Error("The server stopped answering");
                socket.Abort();
                return false;
            }
            Interlocked.CompareExchange(ref _awaitingSince, now, 0);

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
                        _refused.Clear();
                        _replied = false;
                        Interlocked.Exchange(ref _awaitingSince, 0);
                        // Before anyone else can send: the first image must already go to this instance.
                        int instance = _instance;
                        if (instance != 0)
                            await SendAsync(socket, JsonConvert.SerializeObject(new InstanceCommand(instance))).ConfigureAwait(false);
                        _socket = socket;
                        failures = 0;
                        _log.Info("Connected to " + uri);
                        _onConnected?.Invoke();
                        try
                        {
                            await ReadRepliesAsync(socket, attempt.Token).ConfigureAwait(false);
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

        // Hyperion replies to every command. A reply shows the server is still there, and says when
        // it refuses the command.
        private async Task ReadRepliesAsync(ClientWebSocket socket, CancellationToken cancellation)
        {
            var buffer = new byte[4096];
            using (var received = new MemoryStream())
            {
                while (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseSent)
                {
                    var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellation).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        if (socket.State == WebSocketState.CloseReceived)
                            await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None).ConfigureAwait(false);
                        return;
                    }

                    _replied = true;
                    Interlocked.Exchange(ref _awaitingSince, 0);

                    // A reply too long to be an answer to a command only counts as a sign of life.
                    if (received.Length <= MaxReplyBytes) received.Write(buffer, 0, result.Count);
                    if (!result.EndOfMessage) continue;

                    if (received.Length <= MaxReplyBytes) NoteReply(Encoding.UTF8.GetString(received.ToArray()));
                    received.SetLength(0);
                }
            }
        }

        private void NoteReply(string json)
        {
            JObject reply;
            try
            {
                reply = JObject.Parse(json);
            }
            catch (JsonException)
            {
                return;
            }

            JToken success = reply["success"];
            if (success == null || success.Type != JTokenType.Boolean) return;

            string command = (string)reply["command"] ?? string.Empty;
            if ((bool)success)
            {
                _refused.TryRemove(command, out _);
                return;
            }

            string reason = "The server refused '" + command + "': " + ((string)reply["error"] ?? "no reason given");
            string before;
            _refused.TryGetValue(command, out before);
            _refused[command] = reason;
            // Every frame is refused the same way; say it once.
            if (before != reason) _log.Error(reason);
        }
    }
}
