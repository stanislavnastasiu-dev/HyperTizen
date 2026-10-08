using System.Net.WebSockets;
using HyperTizen.Core;
using Newtonsoft.Json.Linq;

namespace HyperTizen.Core.Tests;

public class HyperTizenServiceZonesTests
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
    private static Task<JObject> PreviewAsync(ClientWebSocket ui) => AskAsync(ui, "{\"event\":9}", 10);

    // SetConfig has no reply; the status request after it is answered only once it was handled.
    private static async Task SetAsync(ClientWebSocket ui, string key, string value)
    {
        await Ws.SendAsync(ui, "{\"event\":0,\"key\":\"" + key + "\",\"value\":\"" + value + "\"}");
        await StatusAsync(ui);
    }

    private static async Task DeleteAsync(ClientWebSocket ui, string key)
    {
        await Ws.SendAsync(ui, "{\"event\":11,\"key\":\"" + key + "\"}");
        await StatusAsync(ui);
    }

    private static string[] Edges(JObject preview) => preview["points"]!.Select(point => (string)point["edge"]!).ToArray();

    [Fact]
    public async Task A_preview_has_one_point_per_color_with_position_and_edge()
    {
        using var fixture = new Fixture();
        await fixture.Service.StartAsync();
        using var ui = await fixture.ConnectAsync();

        var preview = await PreviewAsync(ui);

        Assert.True((bool)preview["ok"]!);
        Assert.Equal(14, preview["colors"]!.Count());
        Assert.Equal(14, preview["points"]!.Count());
        Assert.Equal(0.125, (double)preview["points"]![0]!["x"]!, 6);
        Assert.Equal(0.05, (double)preview["points"]![0]!["y"]!, 6);
        Assert.Equal(
            new[] { "top", "top", "top", "top", "right", "right", "right", "bottom", "bottom", "bottom", "bottom", "left", "left", "left" },
            Edges(preview));
    }

    [Fact]
    public async Task A_zone_setting_changes_what_is_captured()
    {
        using var fixture = new Fixture();
        await fixture.Service.StartAsync();
        using var ui = await fixture.ConnectAsync();

        await SetAsync(ui, "zonesBottom", "0");
        await SetAsync(ui, "zonesTop", "8");
        var preview = await PreviewAsync(ui);

        Assert.Equal("0", fixture.Settings.Get("zonesBottom"));
        Assert.Equal(14, preview["colors"]!.Count());
        Assert.Equal(8, Edges(preview).Count(edge => edge == "top"));
        Assert.DoesNotContain("bottom", Edges(preview));
    }

    [Theory]
    [InlineData("zonesTop", "17")]
    [InlineData("zonesBottom", "17")]
    [InlineData("zonesLeft", "13")]
    [InlineData("zonesRight", "13")]
    [InlineData("zonesTop", "-1")]
    [InlineData("zonesTop", "1.5")]
    [InlineData("zonesTop", "four")]
    [InlineData("zonesTop", "")]
    public async Task A_zone_count_outside_its_limits_is_refused(string key, string value)
    {
        using var fixture = new Fixture();
        await fixture.Service.StartAsync();
        using var ui = await fixture.ConnectAsync();

        await SetAsync(ui, key, value);

        Assert.False(fixture.Settings.Contains(key));
        Assert.Equal(14, (await PreviewAsync(ui))["colors"]!.Count());
    }

    [Fact]
    public async Task The_limits_themselves_are_accepted()
    {
        using var fixture = new Fixture();
        await fixture.Service.StartAsync();
        using var ui = await fixture.ConnectAsync();

        await SetAsync(ui, "zonesTop", "16");
        await SetAsync(ui, "zonesBottom", "16");
        await SetAsync(ui, "zonesLeft", "12");
        await SetAsync(ui, "zonesRight", "12");

        Assert.Equal(56, (await PreviewAsync(ui))["colors"]!.Count());
    }

    [Fact]
    public async Task The_last_zone_cannot_be_removed()
    {
        using var fixture = new Fixture();
        await fixture.Service.StartAsync();
        using var ui = await fixture.ConnectAsync();

        await SetAsync(ui, "zonesTop", "0");
        await SetAsync(ui, "zonesBottom", "0");
        await SetAsync(ui, "zonesLeft", "0");
        await SetAsync(ui, "zonesRight", "0");

        Assert.False(fixture.Settings.Contains("zonesRight"));
        Assert.Equal(new[] { "right", "right", "right" }, Edges(await PreviewAsync(ui)));
    }

    [Fact]
    public async Task Deleting_a_zone_setting_returns_it_to_its_default()
    {
        using var fixture = new Fixture();
        await fixture.Service.StartAsync();
        using var ui = await fixture.ConnectAsync();
        await SetAsync(ui, "zonesTop", "8");
        Assert.Equal(18, (await PreviewAsync(ui))["colors"]!.Count());

        await DeleteAsync(ui, "zonesTop");

        Assert.Equal(14, (await PreviewAsync(ui))["colors"]!.Count());
    }

    [Fact]
    public async Task Stored_zone_values_that_are_not_valid_fall_back_to_their_defaults()
    {
        using var fixture = new Fixture();
        fixture.Settings.Set("zonesTop", "99");
        fixture.Settings.Set("zonesLeft", "many");
        fixture.Settings.Set("zonesRight", "5");

        await fixture.Service.StartAsync();
        using var ui = await fixture.ConnectAsync();
        var edges = Edges(await PreviewAsync(ui));

        Assert.Equal(4, edges.Count(edge => edge == "top"));
        Assert.Equal(3, edges.Count(edge => edge == "left"));
        Assert.Equal(5, edges.Count(edge => edge == "right"));
    }

    [Fact]
    public async Task Four_stored_zeros_fall_back_to_the_default_layout()
    {
        using var fixture = new Fixture();
        foreach (string key in new[] { "zonesTop", "zonesBottom", "zonesLeft", "zonesRight" }) fixture.Settings.Set(key, "0");

        await fixture.Service.StartAsync();
        using var ui = await fixture.ConnectAsync();

        Assert.Equal(14, (await PreviewAsync(ui))["colors"]!.Count());
    }

    [Theory]
    [InlineData("0", true)]
    [InlineData("1", true)]
    [InlineData("45", true)]
    [InlineData("60", true)]
    [InlineData("10", true)]
    [InlineData("20", true)]
    [InlineData("30", true)]
    [InlineData("61", false)]
    [InlineData("7.5", false)]
    [InlineData("-1", false)]
    [InlineData("fast", false)]
    public async Task Frame_rate_limit_is_unlimited_or_one_to_sixty(string value, bool accepted)
    {
        using var fixture = new Fixture();
        await fixture.Service.StartAsync();
        using var ui = await fixture.ConnectAsync();

        await SetAsync(ui, "maxFps", value);

        Assert.Equal(accepted, fixture.Settings.Contains("maxFps"));
    }

    [Fact]
    public async Task Status_has_no_timing_before_any_frame()
    {
        using var fixture = new Fixture();
        await fixture.Service.StartAsync();
        using var ui = await fixture.ConnectAsync();

        var status = await StatusAsync(ui);

        Assert.Equal(JTokenType.Null, status["frameMs"]!.Type);
        Assert.Equal(0, (double)status["fps"]!);
    }

    [Fact]
    public async Task Status_reports_timing_while_capturing()
    {
        using var fixture = new Fixture();
        fixture.Settings.Set("enabled", "true");
        fixture.Settings.Set("rpcServer", fixture.Hyperion.Uri);
        await fixture.Service.StartAsync();
        using var ui = await fixture.ConnectAsync();
        await TestHelpers.WaitUntilAsync(() => fixture.Hyperion.Messages.Count >= 8);

        var status = await StatusAsync(ui);

        Assert.InRange((int)status["frameMs"]!, 5, 2000);
        Assert.True((double)status["fps"]! >= 1.0);
    }
}
