using System.Net.WebSockets;
using HyperTizen.Core;
using Newtonsoft.Json.Linq;

namespace HyperTizen.Core.Tests;

public class HyperTizenServiceStatusTests
{
    private sealed class Fixture : IDisposable
    {
        public FakeHyperionServer Hyperion { get; } = new();
        public MemorySettingsStore Settings { get; } = new();
        public FakeCapturer Capturer { get; } = new();
        public ListLog Log { get; } = new();
        public int Port { get; } = TestHelpers.FreePort();
        public HyperTizenService Service { get; }

        public Fixture()
        {
            Service = new HyperTizenService("http://127.0.0.1:" + Port + "/", Capturer, Settings, Log);
        }

        public Task<ClientWebSocket> ConnectAsync() => Ws.ConnectAsync("ws://127.0.0.1:" + Port + "/");

        public void Enable()
        {
            Settings.Set("enabled", "true");
            Settings.Set("rpcServer", Hyperion.Uri);
        }

        public void Dispose()
        {
            Service.StopAsync().GetAwaiter().GetResult();
            Hyperion.Dispose();
        }
    }

    private static async Task<JObject> AskAsync(ClientWebSocket ui, string message, int expectedEvent)
    {
        await Ws.SendAsync(ui, message);
        while (true)
        {
            var reply = JObject.Parse(await Ws.ReceiveAsync(ui));
            if ((int)reply["Event"]! == expectedEvent) return reply;
        }
    }

    private static Task<JObject> StatusAsync(ClientWebSocket ui) => AskAsync(ui, "{\"event\":5}", 6);

    private static bool IsImageAt(string json, int priority) =>
        json.Contains("\"command\":\"image\"") && (int)JObject.Parse(json)["priority"]! == priority;

    [Fact]
    public async Task Status_of_a_fresh_install()
    {
        using var fixture = new Fixture();
        await fixture.Service.StartAsync();
        using var ui = await fixture.ConnectAsync();

        var status = await StatusAsync(ui);

        Assert.Equal("1.1.0", (string?)status["version"]);
        Assert.False((bool)status["enabled"]!);
        Assert.Equal(JTokenType.Null, status["rpcServer"]!.Type);
        Assert.False((bool)status["connected"]!);
        Assert.Equal("unknown", (string?)status["capture"]);
        Assert.Equal(JTokenType.Null, status["lastError"]!.Type);
    }

    [Fact]
    public async Task Status_while_capturing()
    {
        using var fixture = new Fixture();
        fixture.Enable();
        await fixture.Service.StartAsync();
        await TestHelpers.WaitUntilAsync(() => !fixture.Hyperion.Messages.IsEmpty);
        using var ui = await fixture.ConnectAsync();

        var status = await StatusAsync(ui);

        Assert.True((bool)status["enabled"]!);
        Assert.Equal(fixture.Hyperion.Uri, (string?)status["rpcServer"]);
        Assert.True((bool)status["connected"]!);
        Assert.Equal("running", (string?)status["capture"]);
    }

    [Fact]
    public async Task Status_reports_an_unsupported_tv()
    {
        using var fixture = new Fixture();
        fixture.Capturer.Supported = false;
        fixture.Enable();
        await fixture.Service.StartAsync();
        using var ui = await fixture.ConnectAsync();

        var status = await StatusAsync(ui);

        Assert.Equal("unsupported", (string?)status["capture"]);
        Assert.Contains("not supported", (string?)status["lastError"]);
    }

    [Fact]
    public async Task Last_error_appears_for_an_unreachable_server_and_clears_once_frames_flow()
    {
        using var fixture = new Fixture();
        fixture.Settings.Set("enabled", "true");
        fixture.Settings.Set("rpcServer", "ws://127.0.0.1:" + TestHelpers.FreePort() + "/");
        await fixture.Service.StartAsync();
        using var ui = await fixture.ConnectAsync();
        await TestHelpers.WaitUntilAsync(() => fixture.Log.Errors.Any(e => e.Contains("Hyperion connection")), 10000);

        var failing = await StatusAsync(ui);
        Assert.False((bool)failing["connected"]!);
        Assert.Contains("Hyperion connection", (string?)failing["lastError"]);

        await Ws.SendAsync(ui, "{\"event\":0,\"key\":\"rpcServer\",\"value\":\"" + fixture.Hyperion.Uri + "\"}");
        await TestHelpers.WaitUntilAsync(() => !fixture.Hyperion.Messages.IsEmpty);

        var healthy = await StatusAsync(ui);
        Assert.True((bool)healthy["connected"]!);
        Assert.Equal(JTokenType.Null, healthy["lastError"]!.Type);
    }

    [Fact]
    public async Task Last_error_from_a_failed_led_test_clears_once_a_test_succeeds()
    {
        using var fixture = new Fixture();
        fixture.Settings.Set("rpcServer", "ws://127.0.0.1:" + TestHelpers.FreePort() + "/");
        await fixture.Service.StartAsync();
        using var ui = await fixture.ConnectAsync();

        var failed = await AskAsync(ui, "{\"event\":7}", 8);
        Assert.False((bool)failed["ok"]!);
        Assert.Contains("Hyperion connection", (string?)(await StatusAsync(ui))["lastError"]);

        // Capture stays off, so no frame will ever clear the error.
        await Ws.SendAsync(ui, "{\"event\":0,\"key\":\"rpcServer\",\"value\":\"" + fixture.Hyperion.Uri + "\"}");
        Assert.Equal(JTokenType.Null, (await StatusAsync(ui))["lastError"]!.Type);

        var passed = await AskAsync(ui, "{\"event\":7}", 8);
        Assert.True((bool)passed["ok"]!);
        Assert.Equal(JTokenType.Null, (await StatusAsync(ui))["lastError"]!.Type);
    }

    [Fact]
    public async Task Led_test_runs_through_the_control_connection()
    {
        using var fixture = new Fixture();
        fixture.Settings.Set("rpcServer", fixture.Hyperion.Uri);
        await fixture.Service.StartAsync();
        using var ui = await fixture.ConnectAsync();

        var result = await AskAsync(ui, "{\"event\":7}", 8);

        Assert.True((bool)result["ok"]!);
        await TestHelpers.WaitUntilAsync(() => fixture.Hyperion.Messages.Count == 5);
        Assert.Equal("{\"command\":\"clear\",\"priority\":99}", fixture.Hyperion.Messages.Last());
    }

    [Fact]
    public async Task Preview_returns_one_color_per_zone_scaled_to_bytes()
    {
        using var fixture = new Fixture();
        await fixture.Service.StartAsync();
        using var ui = await fixture.ConnectAsync();

        var result = await AskAsync(ui, "{\"event\":9}", 10);

        Assert.True((bool)result["ok"]!);
        var colors = result["colors"]!.ToObject<int[][]>()!;
        Assert.Equal(14, colors.Length);
        // The fake capturer's last point is 832 on the 10-bit scale.
        Assert.Equal(new[] { 208, 208, 208 }, colors[13]);
        Assert.All(colors, c => Assert.All(c, channel => Assert.InRange(channel, 0, 255)));
    }

    [Fact]
    public async Task Preview_reports_an_unsupported_tv()
    {
        using var fixture = new Fixture();
        fixture.Capturer.Supported = false;
        await fixture.Service.StartAsync();
        using var ui = await fixture.ConnectAsync();

        var result = await AskAsync(ui, "{\"event\":9}", 10);

        Assert.False((bool)result["ok"]!);
        Assert.Equal(JTokenType.Null, result["colors"]!.Type);
        Assert.Equal("Screen capture is not supported on this TV.", (string?)result["error"]);
    }

    [Fact]
    public async Task Stored_priority_and_frame_rate_apply_from_start()
    {
        using var fixture = new Fixture();
        fixture.Enable();
        fixture.Settings.Set("priority", "42");
        fixture.Settings.Set("maxFps", "10");
        await fixture.Service.StartAsync();

        await TestHelpers.WaitUntilAsync(() => fixture.Hyperion.Messages.Count >= 2);
        await Task.Delay(500);

        Assert.All(fixture.Hyperion.Messages, m => Assert.True(IsImageAt(m, 42)));
        Assert.InRange(fixture.Hyperion.Messages.Count, 2, 12);
    }

    [Fact]
    public async Task Changing_priority_from_the_ui_moves_frames_to_the_new_priority()
    {
        using var fixture = new Fixture();
        fixture.Enable();
        await fixture.Service.StartAsync();
        await TestHelpers.WaitUntilAsync(() => !fixture.Hyperion.Messages.IsEmpty);
        using var ui = await fixture.ConnectAsync();

        await Ws.SendAsync(ui, "{\"event\":0,\"key\":\"priority\",\"value\":\"120\"}");

        await TestHelpers.WaitUntilAsync(() => fixture.Hyperion.Messages.Any(m => IsImageAt(m, 120)));
        Assert.Contains("{\"command\":\"clear\",\"priority\":99}", fixture.Hyperion.Messages);
        Assert.Equal("120", fixture.Settings.Get("priority"));
    }

    [Theory]
    [InlineData("maxFps", "61")]
    [InlineData("maxFps", "-10")]
    [InlineData("maxFps", "")]
    [InlineData("priority", "0")]
    [InlineData("priority", "254")]
    [InlineData("priority", "9 ")]
    [InlineData("priority", "+5")]
    [InlineData("priority", "abc")]
    [InlineData("enabled", "maybe")]
    [InlineData("rpcServer", " ")]
    public async Task Invalid_settings_are_rejected_and_the_stored_value_is_unchanged(string key, string value)
    {
        using var fixture = new Fixture();
        fixture.Settings.Set("maxFps", "10");
        fixture.Settings.Set("priority", "50");
        fixture.Settings.Set("enabled", "false");
        fixture.Settings.Set("rpcServer", "ws://10.0.0.5:8090");
        string before = fixture.Settings.Get(key);
        await fixture.Service.StartAsync();
        using var ui = await fixture.ConnectAsync();

        await Ws.SendAsync(ui, new JObject { ["event"] = 0, ["key"] = key, ["value"] = value }.ToString());
        await StatusAsync(ui);

        Assert.Equal(before, fixture.Settings.Get(key));
    }

    [Fact]
    public async Task Forgetting_the_server_turns_capture_off_and_disconnects()
    {
        using var fixture = new Fixture();
        fixture.Enable();
        await fixture.Service.StartAsync();
        await TestHelpers.WaitUntilAsync(() => !fixture.Hyperion.Messages.IsEmpty);
        using var ui = await fixture.ConnectAsync();

        await Ws.SendAsync(ui, "{\"event\":11,\"key\":\"rpcServer\"}");
        var status = await StatusAsync(ui);

        Assert.Equal(JTokenType.Null, status["rpcServer"]!.Type);
        Assert.False((bool)status["enabled"]!);
        Assert.False((bool)status["connected"]!);
        Assert.Equal("stopped", (string?)status["capture"]);
        Assert.Equal("false", fixture.Settings.Get("enabled"));
        await TestHelpers.WaitUntilAsync(() => fixture.Hyperion.Messages.Contains("{\"command\":\"clear\",\"priority\":99}"));

        var test = await AskAsync(ui, "{\"event\":7}", 8);
        Assert.Equal("No server is configured.", (string?)test["error"]);
    }
}
