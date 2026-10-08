using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using HyperTizen.Core;

namespace HyperTizen.Core.Tests;

internal static class TestHelpers
{
    public static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var elapsed = Stopwatch.StartNew();
        while (!condition())
        {
            if (elapsed.ElapsedMilliseconds > timeoutMs)
                throw new TimeoutException("Condition not met within " + timeoutMs + " ms.");
            await Task.Delay(20);
        }
    }
}

internal sealed class ListLog : ILog
{
    public ConcurrentQueue<string> Infos { get; } = new();
    public ConcurrentQueue<string> Errors { get; } = new();

    public void Info(string message) => Infos.Enqueue(message);
    public void Error(string message, Exception? exception = null) => Errors.Enqueue(message);
}

internal sealed class MemorySettingsStore : ISettingsStore
{
    private readonly ConcurrentDictionary<string, string> _values = new();

    public bool Contains(string key) => _values.ContainsKey(key);
    public string Get(string key) => _values[key];
    public void Set(string key, string value) => _values[key] = value;
}

internal sealed class FakeCapturer : IScreenCapturer
{
    private int _active;

    public bool Supported = true;
    public int InitializeCalls;
    public int Captures;
    public int MaxConcurrent;
    public int FailuresRemaining;
    public int Entered;
    public ManualResetEventSlim? Gate;

    public bool Initialize()
    {
        Interlocked.Increment(ref InitializeCalls);
        return Supported;
    }

    public Rgb10[] Capture()
    {
        int active = Interlocked.Increment(ref _active);
        try
        {
            if (active > MaxConcurrent) MaxConcurrent = active;
            Interlocked.Increment(ref Entered);
            Gate?.Wait();
            Thread.Sleep(5);
            if (Interlocked.Decrement(ref FailuresRemaining) >= 0)
                throw new InvalidOperationException("capture failed");

            Interlocked.Increment(ref Captures);
            var colors = new Rgb10[16];
            for (int i = 0; i < colors.Length; i++) colors[i] = new Rgb10(i, i, i);
            return colors;
        }
        finally
        {
            Interlocked.Decrement(ref _active);
        }
    }
}

internal static class Ws
{
    public static async Task<ClientWebSocket> ConnectAsync(string uri)
    {
        var client = new ClientWebSocket();
        await client.ConnectAsync(new Uri(uri), CancellationToken.None);
        return client;
    }

    public static Task SendAsync(WebSocket socket, string text, bool endOfMessage = true)
    {
        return socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(text)),
            WebSocketMessageType.Text, endOfMessage, CancellationToken.None);
    }

    public static async Task<string> ReceiveAsync(WebSocket socket)
    {
        var buffer = new byte[65536];
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), timeout.Token);
        return Encoding.UTF8.GetString(buffer, 0, result.Count);
    }
}
