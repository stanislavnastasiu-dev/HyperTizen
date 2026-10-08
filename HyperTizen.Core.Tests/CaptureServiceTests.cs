using HyperTizen.Core;
using Newtonsoft.Json.Linq;

namespace HyperTizen.Core.Tests;

public class CaptureServiceTests
{
    private static (CaptureService Service, FakeCapturer Capturer, ListLog Log) Create(FakeHyperionServer server)
    {
        var log = new ListLog();
        var client = new HyperionClient(log, new[] { TimeSpan.FromMilliseconds(50) });
        client.SetServer(server.Uri);
        var capturer = new FakeCapturer();
        return (new CaptureService(capturer, client, log), capturer, log);
    }

    [Fact]
    public async Task Sends_captured_frames_as_image_commands()
    {
        using var server = new FakeHyperionServer();
        var (service, _, _) = Create(server);

        Assert.True(await service.StartAsync());

        await TestHelpers.WaitUntilAsync(() => server.Messages.Count >= 3);
        server.Messages.TryPeek(out string? json);
        Assert.Equal("image", (string?)JObject.Parse(json!)["command"]);
        await service.StopAsync();
    }

    [Fact]
    public async Task Starting_twice_runs_a_single_loop()
    {
        using var server = new FakeHyperionServer();
        var (service, capturer, _) = Create(server);

        Assert.True(await service.StartAsync());
        Assert.True(await service.StartAsync());

        await TestHelpers.WaitUntilAsync(() => capturer.Captures >= 20);
        Assert.Equal(1, capturer.MaxConcurrent);
        Assert.Equal(1, capturer.InitializeCalls);
        await service.StopAsync();
    }

    [Fact]
    public async Task Stop_ends_the_loop_and_clears_the_leds()
    {
        using var server = new FakeHyperionServer();
        var (service, capturer, _) = Create(server);
        await service.StartAsync();
        await TestHelpers.WaitUntilAsync(() => capturer.Captures >= 3);

        await service.StopAsync();

        Assert.False(service.IsRunning);
        int capturesAtStop = capturer.Captures;
        await Task.Delay(200);
        Assert.Equal(capturesAtStop, capturer.Captures);
        await TestHelpers.WaitUntilAsync(() => server.Messages.Any(m => m == "{\"command\":\"clear\",\"priority\":99}"));
        Assert.Equal("{\"command\":\"clear\",\"priority\":99}", server.Messages.Last());
    }

    [Fact]
    public async Task Does_not_start_when_the_capturer_is_unsupported()
    {
        using var server = new FakeHyperionServer();
        var (service, capturer, log) = Create(server);
        capturer.Supported = false;

        Assert.False(await service.StartAsync());

        Assert.False(service.IsRunning);
        await Task.Delay(200);
        Assert.Equal(0, capturer.Captures);
        Assert.Equal(0, server.ConnectionCount);
        Assert.NotEmpty(log.Errors);
    }

    [Fact]
    public async Task Keeps_running_after_a_capture_failure()
    {
        using var server = new FakeHyperionServer();
        var (service, capturer, log) = Create(server);
        capturer.FailuresRemaining = 1;

        await service.StartAsync();

        await TestHelpers.WaitUntilAsync(() => capturer.Captures >= 1);
        Assert.Contains(log.Errors, e => e.Contains("Capture failed"));
        await service.StopAsync();
    }

    [Fact]
    public async Task Can_be_restarted_after_stop()
    {
        using var server = new FakeHyperionServer();
        var (service, capturer, _) = Create(server);
        await service.StartAsync();
        await TestHelpers.WaitUntilAsync(() => capturer.Captures >= 1);
        await service.StopAsync();
        int before = capturer.Captures;

        Assert.True(await service.StartAsync());

        await TestHelpers.WaitUntilAsync(() => capturer.Captures > before);
        await service.StopAsync();
    }
}
