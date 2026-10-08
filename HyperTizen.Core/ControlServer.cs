using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace HyperTizen.Core
{
    // WebSocket server the web UI talks to: read/write settings and scan for Hyperion devices.
    public sealed class ControlServer
    {
        private const int MaxMessageBytes = 1024 * 1024;

        private readonly string _prefix;
        private readonly ISettingsStore _settings;
        private readonly Func<Task<List<SSDPScanResultEvent.SSDPDevice>>> _scan;
        private readonly Func<string, string, Task> _onConfigChanged;
        private readonly ILog _log;

        private HttpListener _listener;
        private CancellationTokenSource _cancellation;
        private Task _acceptLoop;

        public ControlServer(
            string prefix,
            ISettingsStore settings,
            Func<Task<List<SSDPScanResultEvent.SSDPDevice>>> scan,
            Func<string, string, Task> onConfigChanged,
            ILog log)
        {
            _prefix = prefix ?? throw new ArgumentNullException(nameof(prefix));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _scan = scan ?? throw new ArgumentNullException(nameof(scan));
            _onConfigChanged = onConfigChanged ?? throw new ArgumentNullException(nameof(onConfigChanged));
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        public void Start()
        {
            if (_listener != null) return;

            var listener = new HttpListener();
            listener.Prefixes.Add(_prefix);
            listener.Start();

            _listener = listener;
            _cancellation = new CancellationTokenSource();
            CancellationToken token = _cancellation.Token;
            _acceptLoop = Task.Run(() => AcceptLoopAsync(listener, token));
            _log.Info("Control server listening on " + _prefix);
        }

        public async Task StopAsync()
        {
            if (_listener == null) return;

            _cancellation.Cancel();
            _listener.Close();
            await _acceptLoop.ConfigureAwait(false);
            _cancellation.Dispose();
            _cancellation = null;
            _listener = null;
        }

        public async Task<string> HandleMessageAsync(string message)
        {
            BasicEvent data = JsonConvert.DeserializeObject<BasicEvent>(message);
            if (data == null) throw new JsonSerializationException("Empty control message.");

            switch (data.Event)
            {
                case Event.ScanSSDP:
                    {
                        var devices = await _scan().ConfigureAwait(false);
                        return JsonConvert.SerializeObject(new SSDPScanResultEvent(devices));
                    }

                case Event.ReadConfig:
                    {
                        ReadConfigEvent readConfigEvent = JsonConvert.DeserializeObject<ReadConfigEvent>(message);
                        if (readConfigEvent.key == null || !_settings.Contains(readConfigEvent.key))
                            return JsonConvert.SerializeObject(new ReadConfigResultEvent(true, readConfigEvent.key, "Key doesn't exist."));

                        string value = _settings.Get(readConfigEvent.key);
                        return JsonConvert.SerializeObject(new ReadConfigResultEvent(false, readConfigEvent.key, value));
                    }

                case Event.SetConfig:
                    {
                        SetConfigEvent setConfigEvent = JsonConvert.DeserializeObject<SetConfigEvent>(message);
                        if (setConfigEvent.key == null || setConfigEvent.value == null)
                        {
                            _log.Error("Ignoring SetConfig without key or value");
                            return null;
                        }

                        _settings.Set(setConfigEvent.key, setConfigEvent.value);
                        await _onConfigChanged(setConfigEvent.key, setConfigEvent.value).ConfigureAwait(false);
                        return null;
                    }

                default:
                    _log.Info("Ignoring control event " + (int)data.Event);
                    return null;
            }
        }

        private async Task AcceptLoopAsync(HttpListener listener, CancellationToken cancellation)
        {
            while (!cancellation.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await listener.GetContextAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    if (cancellation.IsCancellationRequested) return;
                    _log.Error("Control server stopped accepting connections", ex);
                    return;
                }

                try
                {
                    if (!context.Request.IsWebSocketRequest)
                    {
                        context.Response.StatusCode = 400;
                        context.Response.Close();
                        continue;
                    }

                    var webSocketContext = await context.AcceptWebSocketAsync(null).ConfigureAwait(false);
                    _ = HandleConnectionAsync(webSocketContext.WebSocket, cancellation);
                }
                catch (Exception ex)
                {
                    _log.Error("Accepting a control connection failed", ex);
                }
            }
        }

        private async Task HandleConnectionAsync(System.Net.WebSockets.WebSocket webSocket, CancellationToken cancellation)
        {
            var buffer = new byte[4096];
            try
            {
                using (webSocket)
                {
                    while (webSocket.State == WebSocketState.Open)
                    {
                        string message;
                        using (var received = new MemoryStream())
                        {
                            WebSocketReceiveResult result;
                            do
                            {
                                result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellation).ConfigureAwait(false);
                                if (result.MessageType == WebSocketMessageType.Close)
                                {
                                    await webSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None).ConfigureAwait(false);
                                    return;
                                }

                                received.Write(buffer, 0, result.Count);
                                if (received.Length > MaxMessageBytes)
                                {
                                    await webSocket.CloseOutputAsync(WebSocketCloseStatus.MessageTooBig, string.Empty, CancellationToken.None).ConfigureAwait(false);
                                    return;
                                }
                            } while (!result.EndOfMessage);

                            message = Encoding.UTF8.GetString(received.ToArray());
                        }

                        string reply;
                        try
                        {
                            reply = await HandleMessageAsync(message).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            _log.Error("Control message could not be handled", ex);
                            continue;
                        }

                        if (reply != null)
                        {
                            byte[] bytes = Encoding.UTF8.GetBytes(reply);
                            await webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellation).ConfigureAwait(false);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (WebSocketException ex)
            {
                _log.Info("Control client disconnected: " + ex.Message);
            }
            catch (Exception ex)
            {
                _log.Error("Control connection failed", ex);
            }
        }
    }
}
