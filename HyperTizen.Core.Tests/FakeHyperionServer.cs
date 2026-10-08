using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text;

namespace HyperTizen.Core.Tests;

// In-process stand-in for Hyperion: accepts WebSocket connections and records every text message.
internal sealed class FakeHyperionServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly ConcurrentDictionary<WebSocket, HttpListenerContext> _sockets = new();
    private int _connectionCount;

    public FakeHyperionServer()
    {
        int port = TestHelpers.FreePort();
        Uri = "ws://127.0.0.1:" + port + "/";
        _listener.Prefixes.Add("http://127.0.0.1:" + port + "/");
        _listener.Start();
        _ = AcceptAsync();
    }

    public string Uri { get; }
    public ConcurrentQueue<string> Messages { get; } = new();
    public int ConnectionCount => _connectionCount;

    public void DropConnections()
    {
        foreach (var connection in _sockets)
        {
            connection.Key.Abort();
            // On Linux aborting the WebSocket alone leaves the connection open, so the client never notices.
            connection.Value.Response.Abort();
        }
    }

    public void Dispose()
    {
        DropConnections();
        _listener.Close();
    }

    private async Task AcceptAsync()
    {
        while (true)
        {
            WebSocket socket;
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
                socket = (await context.AcceptWebSocketAsync(null)).WebSocket;
            }
            catch
            {
                return;
            }

            _sockets[socket] = context;
            Interlocked.Increment(ref _connectionCount);
            _ = ReceiveAsync(socket);
        }
    }

    private async Task ReceiveAsync(WebSocket socket)
    {
        var buffer = new byte[65536];
        try
        {
            while (socket.State == WebSocketState.Open)
            {
                using var message = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None);
                        return;
                    }
                    message.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                Messages.Enqueue(Encoding.UTF8.GetString(message.ToArray()));
            }
        }
        catch
        {
            // Dropped connections are expected in tests.
        }
        finally
        {
            _sockets.TryRemove(socket, out _);
        }
    }
}
