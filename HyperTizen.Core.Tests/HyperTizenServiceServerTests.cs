using System.Net.WebSockets;
using HyperTizen.Core;
using Newtonsoft.Json.Linq;

namespace HyperTizen.Core.Tests;

// What the service accepts from the control port, and what it tells about a server that refuses it.
public class HyperTizenServiceServerTests
{
    private const string SwitchTo2 = "{\"command\":\"instance\",\"subcommand\":\"switchTo\",\"instance\":2}";

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

    private static Task SetAsync(ClientWebSocket ui, string key, string value) =>
        Ws.SendAsync(ui, "{\"event\":0,\"key\":\"" + key + "\",\"value\":\"" + value + "\"}");

    [Fact]
    public async Task Status_shows_the_servers_refusal_for_as_long_as_it_refuses()
    {
        using var fixture = new Fixture();
        fixture.Hyperion.Reply = message => FakeHyperionServer.Refuse(message, "No Authorization");
        fixture.Enable();
        await fixture.Service.StartAsync();
        await TestHelpers.WaitUntilAsync(() => ((IControlActions)fixture.Service).GetStatus().lastError != null);
        using var ui = await fixture.ConnectAsync();

        // Frames keep being sent, and each one sent clears the other errors; the refusal stays.
        for (int i = 0; i < 5; i++)
        {
            Assert.Equal("The server refused 'image': No Authorization", (string?)(await StatusAsync(ui))["lastError"]);
            await Task.Delay(30);
        }

        fixture.Hyperion.Reply = FakeHyperionServer.Accept;
        await TestHelpers.WaitUntilAsync(() => ((IControlActions)fixture.Service).GetStatus().lastError == null);
    }

    [Fact]
    public async Task The_led_test_fails_with_the_servers_reason_when_it_refuses()
    {
        using var fixture = new Fixture();
        fixture.Hyperion.Reply = message => FakeHyperionServer.Refuse(message, "No Authorization");
        fixture.Settings.Set("rpcServer", fixture.Hyperion.Uri);
        await fixture.Service.StartAsync();
        using var ui = await fixture.ConnectAsync();

        var result = await AskAsync(ui, "{\"event\":7}", 8);

        Assert.False((bool)result["ok"]!);
        Assert.Equal("The server refused 'color': No Authorization", (string?)result["error"]);
    }

    [Fact]
    public async Task A_setting_the_service_does_not_know_is_not_stored()
    {
        using var fixture = new Fixture();
        await fixture.Service.StartAsync();
        using var ui = await fixture.ConnectAsync();

        await SetAsync(ui, "somethingElse", "1");
        await StatusAsync(ui);

        Assert.False(fixture.Settings.Contains("somethingElse"));
    }

    [Theory]
    [InlineData("http://192.168.1.10:8090")]
    [InlineData("192.168.1.10:8090")]
    [InlineData("file:///etc/passwd")]
    [InlineData("not an address")]
    public async Task A_server_address_that_is_not_a_websocket_address_is_not_stored(string address)
    {
        using var fixture = new Fixture();
        await fixture.Service.StartAsync();
        using var ui = await fixture.ConnectAsync();

        await SetAsync(ui, "rpcServer", address);
        await StatusAsync(ui);

        Assert.False(fixture.Settings.Contains("rpcServer"));
    }

    [Theory]
    [InlineData("ws://192.168.1.10:8090")]
    [InlineData("wss://hyperion.local:8092")]
    public async Task A_websocket_address_is_stored(string address)
    {
        using var fixture = new Fixture();
        await fixture.Service.StartAsync();
        using var ui = await fixture.ConnectAsync();

        await SetAsync(ui, "rpcServer", address);
        await StatusAsync(ui);

        Assert.Equal(address, fixture.Settings.Get("rpcServer"));
    }

    [Fact]
    public async Task The_stored_instance_is_chosen_before_the_first_image()
    {
        using var fixture = new Fixture();
        fixture.Enable();
        fixture.Settings.Set("instance", "2");

        await fixture.Service.StartAsync();

        await TestHelpers.WaitUntilAsync(() => fixture.Hyperion.Messages.Count >= 2);
        Assert.Equal(SwitchTo2, fixture.Hyperion.Messages.First());
    }

    [Fact]
    public async Task Changing_the_instance_clears_the_one_being_left_then_switches()
    {
        using var fixture = new Fixture();
        fixture.Settings.Set("rpcServer", fixture.Hyperion.Uri);
        fixture.Settings.Set("enabled", "true");
        fixture.Capturer.Gate = new ManualResetEventSlim(false);
        await fixture.Service.StartAsync();
        await TestHelpers.WaitUntilAsync(() => ((IControlActions)fixture.Service).GetStatus().connected);
        using var ui = await fixture.ConnectAsync();

        await SetAsync(ui, "instance", "2");
        await StatusAsync(ui);

        await TestHelpers.WaitUntilAsync(() => fixture.Hyperion.Messages.Count == 2);
        Assert.Equal(new[] { "{\"command\":\"clear\",\"priority\":99}", SwitchTo2 }, fixture.Hyperion.Messages.ToArray());
        Assert.Equal("2", fixture.Settings.Get("instance"));
        fixture.Capturer.Gate.Set();
    }

    [Theory]
    [InlineData("255")]
    [InlineData("-1")]
    [InlineData("one")]
    public async Task An_instance_out_of_range_is_not_stored(string value)
    {
        using var fixture = new Fixture();
        await fixture.Service.StartAsync();
        using var ui = await fixture.ConnectAsync();

        await SetAsync(ui, "instance", value);
        await StatusAsync(ui);

        Assert.False(fixture.Settings.Contains("instance"));
    }

    [Fact]
    public async Task Deleting_the_instance_goes_back_to_the_first()
    {
        using var fixture = new Fixture();
        fixture.Settings.Set("rpcServer", fixture.Hyperion.Uri);
        fixture.Settings.Set("enabled", "true");
        fixture.Settings.Set("instance", "2");
        fixture.Capturer.Gate = new ManualResetEventSlim(false);
        await fixture.Service.StartAsync();
        await TestHelpers.WaitUntilAsync(() => ((IControlActions)fixture.Service).GetStatus().connected);
        using var ui = await fixture.ConnectAsync();

        await Ws.SendAsync(ui, "{\"event\":11,\"key\":\"instance\"}");
        await StatusAsync(ui);

        await TestHelpers.WaitUntilAsync(() => fixture.Hyperion.Messages.Count == 3);
        Assert.Equal("{\"command\":\"instance\",\"subcommand\":\"switchTo\",\"instance\":0}", fixture.Hyperion.Messages.Last());
        fixture.Capturer.Gate.Set();
    }
}
