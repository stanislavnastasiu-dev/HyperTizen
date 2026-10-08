using HyperTizen.Core;

namespace HyperTizen.Core.Tests;

public class HyperTizenServiceTests
{
    private const string Clear = "{\"command\":\"clear\",\"priority\":99}";

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

        public string ControlUri => "ws://127.0.0.1:" + Port + "/";

        public void Dispose()
        {
            Service.StopAsync().GetAwaiter().GetResult();
            Hyperion.Dispose();
        }
    }

    [Fact]
    public async Task First_start_defaults_to_disabled_and_does_not_capture()
    {
        using var fixture = new Fixture();

        await fixture.Service.StartAsync();
        await Task.Delay(200);

        Assert.Equal("false", fixture.Settings.Get("enabled"));
        Assert.Equal(0, fixture.Capturer.Captures);
    }

    [Fact]
    public async Task Starts_capturing_when_enabled_and_a_server_is_stored()
    {
        using var fixture = new Fixture();
        fixture.Settings.Set("enabled", "true");
        fixture.Settings.Set("rpcServer", fixture.Hyperion.Uri);

        await fixture.Service.StartAsync();

        await TestHelpers.WaitUntilAsync(() => !fixture.Hyperion.Messages.IsEmpty);
    }

    [Fact]
    public async Task The_ui_can_pick_a_server_and_enable_capture()
    {
        using var fixture = new Fixture();
        await fixture.Service.StartAsync();
        using var ui = await Ws.ConnectAsync(fixture.ControlUri);

        await Ws.SendAsync(ui, "{\"event\":0,\"key\":\"rpcServer\",\"value\":\"" + fixture.Hyperion.Uri + "\"}");
        await Ws.SendAsync(ui, "{\"event\":0,\"key\":\"enabled\",\"value\":\"true\"}");

        await TestHelpers.WaitUntilAsync(() => !fixture.Hyperion.Messages.IsEmpty);
        Assert.Equal("true", fixture.Settings.Get("enabled"));
    }

    [Fact]
    public async Task Disabling_from_the_ui_clears_the_leds()
    {
        using var fixture = new Fixture();
        fixture.Settings.Set("enabled", "true");
        fixture.Settings.Set("rpcServer", fixture.Hyperion.Uri);
        await fixture.Service.StartAsync();
        await TestHelpers.WaitUntilAsync(() => !fixture.Hyperion.Messages.IsEmpty);
        using var ui = await Ws.ConnectAsync(fixture.ControlUri);

        await Ws.SendAsync(ui, "{\"event\":0,\"key\":\"enabled\",\"value\":\"false\"}");

        await TestHelpers.WaitUntilAsync(() => fixture.Hyperion.Messages.Contains(Clear));
    }

    [Fact]
    public async Task Display_off_clears_and_display_on_resumes_without_changing_the_setting()
    {
        using var fixture = new Fixture();
        fixture.Settings.Set("enabled", "true");
        fixture.Settings.Set("rpcServer", fixture.Hyperion.Uri);
        await fixture.Service.StartAsync();
        await TestHelpers.WaitUntilAsync(() => !fixture.Hyperion.Messages.IsEmpty);

        await fixture.Service.OnDisplayOffAsync();
        await TestHelpers.WaitUntilAsync(() => fixture.Hyperion.Messages.Contains(Clear));
        Assert.Equal("true", fixture.Settings.Get("enabled"));
        int capturesWhileOff = fixture.Capturer.Captures;

        await fixture.Service.OnDisplayOnAsync();

        await TestHelpers.WaitUntilAsync(() => fixture.Capturer.Captures > capturesWhileOff);
    }

    [Fact]
    public async Task Display_on_does_nothing_while_disabled()
    {
        using var fixture = new Fixture();
        fixture.Settings.Set("rpcServer", fixture.Hyperion.Uri);
        await fixture.Service.StartAsync();

        await fixture.Service.OnDisplayOnAsync();
        await Task.Delay(200);

        Assert.Equal(0, fixture.Capturer.Captures);
    }

    [Fact]
    public async Task An_invalid_enabled_value_is_ignored()
    {
        using var fixture = new Fixture();
        fixture.Settings.Set("rpcServer", fixture.Hyperion.Uri);
        await fixture.Service.StartAsync();
        using var ui = await Ws.ConnectAsync(fixture.ControlUri);

        await Ws.SendAsync(ui, "{\"event\":0,\"key\":\"enabled\",\"value\":\"maybe\"}");
        await Ws.SendAsync(ui, "{\"event\":1,\"key\":\"rpcServer\"}");
        await Ws.ReceiveAsync(ui);

        Assert.Equal(0, fixture.Capturer.Captures);
        Assert.NotEmpty(fixture.Log.Errors);
    }
}
