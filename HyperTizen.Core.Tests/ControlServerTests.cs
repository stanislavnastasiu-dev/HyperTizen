using System.Net;
using System.Net.WebSockets;
using HyperTizen.Core;
using Newtonsoft.Json.Linq;

namespace HyperTizen.Core.Tests;

public class ControlServerTests : IDisposable
{
    private readonly int _port = TestHelpers.FreePort();
    private readonly MemorySettingsStore _settings = new();
    private readonly ListLog _log = new();
    private readonly List<(string Key, string Value)> _changes = new();
    private readonly ControlServer _server;
    private Func<Task<List<SSDPScanResultEvent.SSDPDevice>>> _scan =
        () => Task.FromResult(new List<SSDPScanResultEvent.SSDPDevice>());

    public ControlServerTests()
    {
        _server = new ControlServer(
            "http://127.0.0.1:" + _port + "/",
            _settings,
            () => _scan(),
            (key, value) =>
            {
                lock (_changes) _changes.Add((key, value));
                return Task.CompletedTask;
            },
            _log);
        _server.Start();
    }

    public void Dispose() => _server.StopAsync().GetAwaiter().GetResult();

    private string WsUri => "ws://127.0.0.1:" + _port + "/";

    [Fact]
    public async Task Reading_a_missing_key_returns_an_error()
    {
        using var client = await Ws.ConnectAsync(WsUri);

        await Ws.SendAsync(client, "{\"event\":1,\"key\":\"rpcServer\"}");
        var reply = JObject.Parse(await Ws.ReceiveAsync(client));

        Assert.Equal(2, (int)reply["Event"]!);
        Assert.True((bool)reply["error"]!);
        Assert.Equal("rpcServer", (string?)reply["key"]);
        Assert.Equal("Key doesn't exist.", (string?)reply["value"]);
    }

    [Fact]
    public async Task A_value_that_was_set_can_be_read_back()
    {
        using var client = await Ws.ConnectAsync(WsUri);

        await Ws.SendAsync(client, "{\"event\":0,\"key\":\"rpcServer\",\"value\":\"ws://10.0.0.5:8090\"}");
        await Ws.SendAsync(client, "{\"event\":1,\"key\":\"rpcServer\"}");
        var reply = JObject.Parse(await Ws.ReceiveAsync(client));

        Assert.False((bool)reply["error"]!);
        Assert.Equal("ws://10.0.0.5:8090", (string?)reply["value"]);
        Assert.Equal("ws://10.0.0.5:8090", _settings.Get("rpcServer"));
        lock (_changes) Assert.Equal(new[] { ("rpcServer", "ws://10.0.0.5:8090") }, _changes);
    }

    [Fact]
    public async Task Scan_returns_the_found_devices()
    {
        _scan = () => Task.FromResult(new List<SSDPScanResultEvent.SSDPDevice>
        {
            new("Living room", "http://10.0.0.5:8090")
        });
        using var client = await Ws.ConnectAsync(WsUri);

        await Ws.SendAsync(client, "{\"event\":3}");
        var reply = JObject.Parse(await Ws.ReceiveAsync(client));

        Assert.Equal(4, (int)reply["Event"]!);
        Assert.Equal("Living room", (string?)reply["devices"]![0]!["FriendlyName"]);
        Assert.Equal("http://10.0.0.5:8090", (string?)reply["devices"]![0]!["UrlBase"]);
    }

    [Theory]
    [InlineData("this is not json")]
    [InlineData("null")]
    [InlineData("{\"event\":99}")]
    [InlineData("{\"event\":0,\"key\":\"enabled\"}")]
    [InlineData("[1,2,3]")]
    public async Task An_unexpected_message_does_not_close_the_connection(string message)
    {
        using var client = await Ws.ConnectAsync(WsUri);

        await Ws.SendAsync(client, message);
        await Ws.SendAsync(client, "{\"event\":1,\"key\":\"enabled\"}");
        var reply = JObject.Parse(await Ws.ReceiveAsync(client));

        Assert.Equal(2, (int)reply["Event"]!);
        Assert.Equal(WebSocketState.Open, client.State);
    }

    [Fact]
    public async Task Reading_without_a_key_returns_an_error()
    {
        using var client = await Ws.ConnectAsync(WsUri);

        await Ws.SendAsync(client, "{\"event\":1}");
        var reply = JObject.Parse(await Ws.ReceiveAsync(client));

        Assert.True((bool)reply["error"]!);
    }

    [Fact]
    public async Task A_failing_scan_does_not_close_the_connection()
    {
        _scan = () => throw new InvalidOperationException("network down");
        using var client = await Ws.ConnectAsync(WsUri);

        await Ws.SendAsync(client, "{\"event\":3}");
        await Ws.SendAsync(client, "{\"event\":1,\"key\":\"enabled\"}");
        var reply = JObject.Parse(await Ws.ReceiveAsync(client));

        Assert.Equal(2, (int)reply["Event"]!);
        Assert.NotEmpty(_log.Errors);
    }

    [Fact]
    public async Task A_fragmented_message_is_reassembled()
    {
        using var client = await Ws.ConnectAsync(WsUri);

        await Ws.SendAsync(client, "{\"event\":1,", endOfMessage: false);
        await Ws.SendAsync(client, "\"key\":\"enabled\"}", endOfMessage: true);
        var reply = JObject.Parse(await Ws.ReceiveAsync(client));

        Assert.Equal("enabled", (string?)reply["key"]);
    }

    [Fact]
    public async Task A_client_that_vanishes_does_not_affect_other_clients()
    {
        var vanishing = await Ws.ConnectAsync(WsUri);
        vanishing.Abort();

        using var client = await Ws.ConnectAsync(WsUri);
        await Ws.SendAsync(client, "{\"event\":1,\"key\":\"enabled\"}");
        var reply = JObject.Parse(await Ws.ReceiveAsync(client));

        Assert.Equal(2, (int)reply["Event"]!);
    }

    [Fact]
    public async Task A_plain_http_request_gets_400()
    {
        using var http = new HttpClient();

        var response = await http.GetAsync("http://127.0.0.1:" + _port + "/");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
