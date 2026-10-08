using System.Net;
using System.Net.WebSockets;
using HyperTizen.Core;
using Newtonsoft.Json.Linq;

namespace HyperTizen.Core.Tests;

internal sealed class FakeControlActions : IControlActions
{
    public Func<Task<List<SSDPScanResultEvent.SSDPDevice>>> Scan = () => Task.FromResult(new List<SSDPScanResultEvent.SSDPDevice>());
    public Func<string, string, bool> Validate = (_, _) => true;
    public Func<Task<TestLedsResultEvent>> TestLeds = () => Task.FromResult(new TestLedsResultEvent(true, null));
    public Func<Task<PreviewResultEvent>> Preview = () => Task.FromResult(new PreviewResultEvent(false, null, "none"));
    public StatusResultEvent Status = new() { version = "9.9.9", capture = "unknown" };
    public List<(string Key, string Value)> Changes { get; } = new();
    public List<string> Deletions { get; } = new();

    public Task<List<SSDPScanResultEvent.SSDPDevice>> ScanAsync() => Scan();
    public bool IsValidConfig(string key, string value) => Validate(key, value);
    public StatusResultEvent GetStatus() => Status;
    public Task<TestLedsResultEvent> TestLedsAsync() => TestLeds();
    public Task<PreviewResultEvent> GetPreviewAsync() => Preview();

    public Task OnConfigChangedAsync(string key, string value)
    {
        lock (Changes) Changes.Add((key, value));
        return Task.CompletedTask;
    }

    public Task OnConfigDeletedAsync(string key)
    {
        lock (Deletions) Deletions.Add(key);
        return Task.CompletedTask;
    }
}

public class ControlServerTests : IDisposable
{
    private readonly int _port = TestHelpers.FreePort();
    private readonly MemorySettingsStore _settings = new();
    private readonly ListLog _log = new();
    private readonly FakeControlActions _actions = new();
    private readonly ControlServer _server;

    public ControlServerTests()
    {
        _server = new ControlServer("http://127.0.0.1:" + _port + "/", _settings, _actions, _log);
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
        lock (_actions.Changes) Assert.Equal(new[] { ("rpcServer", "ws://10.0.0.5:8090") }, _actions.Changes);
    }

    [Fact]
    public async Task Scan_returns_the_found_devices()
    {
        _actions.Scan = () => Task.FromResult(new List<SSDPScanResultEvent.SSDPDevice>
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
    [InlineData("{\"event\":11}")]
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
        _actions.Scan = () => throw new InvalidOperationException("network down");
        using var client = await Ws.ConnectAsync(WsUri);

        await Ws.SendAsync(client, "{\"event\":3}");
        await Ws.SendAsync(client, "{\"event\":1,\"key\":\"enabled\"}");
        var reply = JObject.Parse(await Ws.ReceiveAsync(client));

        Assert.Equal(2, (int)reply["Event"]!);
        await TestHelpers.WaitUntilAsync(() => !_log.Errors.IsEmpty);
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
    public async Task A_plain_http_request_gets_400_without_a_ui_folder()
    {
        using var http = new HttpClient();

        var response = await http.GetAsync("http://127.0.0.1:" + _port + "/");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Status_is_returned_as_event_6()
    {
        _actions.Status = new StatusResultEvent
        {
            version = "1.1.0", enabled = true, rpcServer = "ws://10.0.0.5:8090",
            connected = true, capture = "running", lastError = null
        };
        using var client = await Ws.ConnectAsync(WsUri);

        await Ws.SendAsync(client, "{\"event\":5}");
        var reply = JObject.Parse(await Ws.ReceiveAsync(client));

        Assert.Equal(6, (int)reply["Event"]!);
        Assert.Equal("1.1.0", (string?)reply["version"]);
        Assert.True((bool)reply["enabled"]!);
        Assert.Equal("ws://10.0.0.5:8090", (string?)reply["rpcServer"]);
        Assert.True((bool)reply["connected"]!);
        Assert.Equal("running", (string?)reply["capture"]);
        Assert.Equal(JTokenType.Null, reply["lastError"]!.Type);
    }

    [Fact]
    public async Task Led_test_result_is_returned_as_event_8()
    {
        _actions.TestLeds = () => Task.FromResult(new TestLedsResultEvent(false, "No server is configured."));
        using var client = await Ws.ConnectAsync(WsUri);

        await Ws.SendAsync(client, "{\"event\":7}");
        var reply = JObject.Parse(await Ws.ReceiveAsync(client));

        Assert.Equal(8, (int)reply["Event"]!);
        Assert.False((bool)reply["ok"]!);
        Assert.Equal("No server is configured.", (string?)reply["error"]);
    }

    [Fact]
    public async Task Preview_is_returned_as_event_10()
    {
        _actions.Preview = () => Task.FromResult(new PreviewResultEvent(true, new[] { new[] { 1, 2, 3 } }, null));
        using var client = await Ws.ConnectAsync(WsUri);

        await Ws.SendAsync(client, "{\"event\":9}");
        var reply = JObject.Parse(await Ws.ReceiveAsync(client));

        Assert.Equal(10, (int)reply["Event"]!);
        Assert.True((bool)reply["ok"]!);
        Assert.Equal(new[] { 1, 2, 3 }, reply["colors"]![0]!.ToObject<int[]>());
    }

    [Fact]
    public async Task Status_is_answered_while_a_test_and_a_scan_are_in_progress()
    {
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _actions.TestLeds = async () => { await release.Task; return new TestLedsResultEvent(true, null); };
        _actions.Scan = async () => { await release.Task; return new List<SSDPScanResultEvent.SSDPDevice>(); };
        using var first = await Ws.ConnectAsync(WsUri);
        using var second = await Ws.ConnectAsync(WsUri);

        await Ws.SendAsync(first, "{\"event\":7}");
        await Ws.SendAsync(first, "{\"event\":3}");
        await Ws.SendAsync(first, "{\"event\":5}");
        await Ws.SendAsync(second, "{\"event\":5}");

        Assert.Equal(6, (int)JObject.Parse(await Ws.ReceiveAsync(first))["Event"]!);
        Assert.Equal(6, (int)JObject.Parse(await Ws.ReceiveAsync(second))["Event"]!);

        release.SetResult(true);
        var remaining = new[]
        {
            (int)JObject.Parse(await Ws.ReceiveAsync(first))["Event"]!,
            (int)JObject.Parse(await Ws.ReceiveAsync(first))["Event"]!
        };
        Assert.Equal(new[] { 4, 8 }, remaining.OrderBy(e => e).ToArray());
    }

    [Fact]
    public async Task Delete_removes_the_value_and_notifies()
    {
        _settings.Set("rpcServer", "ws://10.0.0.5:8090");
        using var client = await Ws.ConnectAsync(WsUri);

        await Ws.SendAsync(client, "{\"event\":11,\"key\":\"rpcServer\"}");
        await Ws.SendAsync(client, "{\"event\":1,\"key\":\"rpcServer\"}");
        var reply = JObject.Parse(await Ws.ReceiveAsync(client));

        Assert.True((bool)reply["error"]!);
        Assert.False(_settings.Contains("rpcServer"));
        lock (_actions.Deletions) Assert.Equal(new[] { "rpcServer" }, _actions.Deletions);
    }

    [Fact]
    public async Task A_rejected_value_is_not_stored_and_not_announced()
    {
        _settings.Set("maxFps", "10");
        _actions.Validate = (key, value) => !(key == "maxFps" && value == "15");
        using var client = await Ws.ConnectAsync(WsUri);

        await Ws.SendAsync(client, "{\"event\":0,\"key\":\"maxFps\",\"value\":\"15\"}");
        await Ws.SendAsync(client, "{\"event\":1,\"key\":\"maxFps\"}");
        var reply = JObject.Parse(await Ws.ReceiveAsync(client));

        Assert.Equal("10", (string?)reply["value"]);
        lock (_actions.Changes) Assert.Empty(_actions.Changes);
        Assert.NotEmpty(_log.Errors);
    }
}
