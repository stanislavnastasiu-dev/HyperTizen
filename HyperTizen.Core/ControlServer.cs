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
    // WebSocket server the web UI talks to. With a UI folder it also serves the UI's files.
    public sealed class ControlServer
    {
        private const int MaxMessageBytes = 1024 * 1024;

        private readonly string _prefix;
        private readonly ISettingsStore _settings;
        private readonly IControlActions _actions;
        private readonly ILog _log;
        private readonly string _staticRoot;

        private HttpListener _listener;
        private CancellationTokenSource _cancellation;
        private Task _acceptLoop;

        public ControlServer(string prefix, ISettingsStore settings, IControlActions actions, ILog log, string staticRoot = null)
        {
            _prefix = prefix ?? throw new ArgumentNullException(nameof(prefix));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _actions = actions ?? throw new ArgumentNullException(nameof(actions));
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _staticRoot = staticRoot;
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

        public Task<string> HandleMessageAsync(string message)
        {
            return HandleEventAsync(Parse(message), message);
        }

        private static BasicEvent Parse(string message)
        {
            BasicEvent data = JsonConvert.DeserializeObject<BasicEvent>(message);
            if (data == null) throw new JsonSerializationException("Empty control message.");
            return data;
        }

        // These can take seconds; they must not hold up other messages on the same connection.
        private static bool IsLongRunning(Event controlEvent)
        {
            return controlEvent == Event.ScanSSDP || controlEvent == Event.TestLeds || controlEvent == Event.GetPreview;
        }

        private async Task<string> HandleEventAsync(BasicEvent data, string message)
        {
            switch (data.Event)
            {
                case Event.ScanSSDP:
                    {
                        var devices = await _actions.ScanAsync().ConfigureAwait(false);
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

                        if (!_actions.IsValidConfig(setConfigEvent.key, setConfigEvent.value))
                        {
                            _log.Error("Ignoring invalid value for '" + setConfigEvent.key + "': " + setConfigEvent.value);
                            return null;
                        }

                        _settings.Set(setConfigEvent.key, setConfigEvent.value);
                        await _actions.OnConfigChangedAsync(setConfigEvent.key, setConfigEvent.value).ConfigureAwait(false);
                        return null;
                    }

                case Event.DeleteConfig:
                    {
                        DeleteConfigEvent deleteConfigEvent = JsonConvert.DeserializeObject<DeleteConfigEvent>(message);
                        if (deleteConfigEvent.key == null)
                        {
                            _log.Error("Ignoring DeleteConfig without key");
                            return null;
                        }

                        _settings.Remove(deleteConfigEvent.key);
                        await _actions.OnConfigDeletedAsync(deleteConfigEvent.key).ConfigureAwait(false);
                        return null;
                    }

                case Event.GetStatus:
                    return JsonConvert.SerializeObject(_actions.GetStatus());

                case Event.TestLeds:
                    return JsonConvert.SerializeObject(await _actions.TestLedsAsync().ConfigureAwait(false));

                case Event.GetPreview:
                    return JsonConvert.SerializeObject(await _actions.GetPreviewAsync().ConfigureAwait(false));

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
                        ServeFile(context);
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

        private void ServeFile(HttpListenerContext context)
        {
            HttpListenerResponse response = context.Response;
            try
            {
                if (_staticRoot == null)
                {
                    response.StatusCode = 400;
                    return;
                }

                string file = context.Request.HttpMethod == "GET"
                    ? StaticFiles.Resolve(_staticRoot, Uri.UnescapeDataString(context.Request.Url.AbsolutePath))
                    : null;
                if (file == null)
                {
                    response.StatusCode = 404;
                    return;
                }

                byte[] body = File.ReadAllBytes(file);
                response.ContentType = StaticFiles.ContentTypeFor(file);
                response.Headers["Cache-Control"] = "no-store";
                response.ContentLength64 = body.Length;
                response.OutputStream.Write(body, 0, body.Length);
            }
            catch (Exception ex)
            {
                _log.Error("Serving " + context.Request.Url + " failed", ex);
            }
            finally
            {
                try
                {
                    response.Close();
                }
                catch (Exception)
                {
                    // The client went away; nothing to do.
                }
            }
        }

        private async Task HandleConnectionAsync(System.Net.WebSockets.WebSocket webSocket, CancellationToken cancellation)
        {
            var buffer = new byte[4096];
            var sendLock = new SemaphoreSlim(1, 1);
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
                                    await sendLock.WaitAsync(cancellation).ConfigureAwait(false);
                                    try
                                    {
                                        await webSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None).ConfigureAwait(false);
                                    }
                                    finally
                                    {
                                        sendLock.Release();
                                    }
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

                        BasicEvent data;
                        try
                        {
                            data = Parse(message);
                        }
                        catch (Exception ex)
                        {
                            _log.Error("Control message could not be read", ex);
                            continue;
                        }

                        Task processing = ProcessAsync(webSocket, sendLock, data, message, cancellation);
                        if (!IsLongRunning(data.Event)) await processing.ConfigureAwait(false);
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

        // Never throws: a failed message or a reply to a client that left must not end the connection loop.
        private async Task ProcessAsync(
            System.Net.WebSockets.WebSocket webSocket,
            SemaphoreSlim sendLock,
            BasicEvent data,
            string message,
            CancellationToken cancellation)
        {
            try
            {
                string reply = await HandleEventAsync(data, message).ConfigureAwait(false);
                if (reply == null) return;

                byte[] bytes = Encoding.UTF8.GetBytes(reply);
                await sendLock.WaitAsync(cancellation).ConfigureAwait(false);
                try
                {
                    if (webSocket.State == WebSocketState.Open)
                        await webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellation).ConfigureAwait(false);
                }
                finally
                {
                    sendLock.Release();
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
                // The connection closed while a long request was still running.
            }
            catch (WebSocketException ex)
            {
                _log.Info("Control client disconnected: " + ex.Message);
            }
            catch (Exception ex)
            {
                _log.Error("Control message could not be handled", ex);
            }
        }
    }
}
