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

    // Like Hyperion, answers every command; a test replaces this to refuse some.
    public Func<string, string?> Reply { get; set; } = Accept;
    // While true nothing is answered, as when the server's machine is gone without closing.
    public volatile bool Silent;

    public static string Accept(string message) =>
        "{\"command\":\"" + ReplyName(message) + "\",\"success\":true,\"tan\":0}";

    public static string Refuse(string message, string error) =>
        "{\"command\":\"" + ReplyName(message) + "\",\"error\":\"" + error + "\",\"success\":false,\"tan\":0}";

    // Hyperion names a reply after the command and, when there is one, its subcommand.
    private static string ReplyName(string message)
    {
        var parsed = Newtonsoft.Json.Linq.JObject.Parse(message);
        string? subcommand = (string?)parsed["subcommand"];
        return (string?)parsed["command"] + (subcommand == null ? "" : "-" + subcommand);
    }

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

                string text = Encoding.UTF8.GetString(message.ToArray());
                Messages.Enqueue(text);

                string? reply = Silent ? null : Reply(text);
                if (reply != null)
                    await socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(reply)), WebSocketMessageType.Text, true, CancellationToken.None);
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
