using System.Diagnostics;
using HyperTizen.Core;

namespace HyperTizen.Core.Tests;

public class CapturePreviewTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(300);

    private static (CaptureService Service, FakeCapturer Capturer) Create(FakeHyperionServer server)
    {
        var log = new ListLog();
        var client = new HyperionClient(log, new[] { TimeSpan.FromMilliseconds(50) });
        client.SetServer(server.Uri);
        var capturer = new FakeCapturer();
        return (new CaptureService(capturer, client, log, TimeSpan.FromMilliseconds(200)), capturer);
    }

    [Fact]
    public async Task Returns_the_running_loops_frame()
    {
        using var server = new FakeHyperionServer();
        var (service, capturer) = Create(server);
        await service.StartAsync();
        await TestHelpers.WaitUntilAsync(() => capturer.Captures >= 1);

        PreviewFrame frame = await service.GetPreviewAsync(Timeout);

        Assert.Null(frame.Error);
        Assert.Equal(16, frame.Colors.Length);
        Assert.Equal(1, capturer.MaxConcurrent);
        await service.StopAsync();
    }

    [Fact]
    public async Task Captures_one_frame_when_the_loop_is_stopped()
    {
        using var server = new FakeHyperionServer();
        var (service, capturer) = Create(server);

        PreviewFrame frame = await service.GetPreviewAsync(Timeout);

        Assert.Null(frame.Error);
        Assert.Equal(16, frame.Colors.Length);
        Assert.Equal(1, capturer.Captures);
        Assert.Equal("stopped", service.State);
        Assert.Equal(0, server.ConnectionCount);
    }

    [Fact]
    public async Task Reports_an_unsupported_capturer()
    {
        using var server = new FakeHyperionServer();
        var (service, capturer) = Create(server);
        capturer.Supported = false;

        PreviewFrame frame = await service.GetPreviewAsync(Timeout);

        Assert.Null(frame.Colors);
        Assert.Equal(CaptureService.NotSupported, frame.Error);
        Assert.Equal("unsupported", service.State);
        Assert.Equal(0, capturer.Captures);
    }

    [Fact]
    public async Task A_capture_that_never_returns_does_not_block_later_requests()
    {
        using var server = new FakeHyperionServer();
        var (service, capturer) = Create(server);
        capturer.Gate = new ManualResetEventSlim(false);

        var clock = Stopwatch.StartNew();
        PreviewFrame first = await service.GetPreviewAsync(Timeout);
        PreviewFrame second = await service.GetPreviewAsync(Timeout);
        clock.Stop();

        Assert.Equal(CaptureService.NotReturningColors, first.Error);
        Assert.Equal(CaptureService.NotReturningColors, second.Error);
        Assert.InRange(clock.ElapsedMilliseconds, 250, 2000);
        Assert.Equal(1, capturer.Entered);

        capturer.Gate.Set();
        await TestHelpers.WaitUntilAsync(() => capturer.Captures >= 1);
        PreviewFrame third = await service.GetPreviewAsync(Timeout);
        Assert.Null(third.Error);
    }

    [Fact]
    public async Task Start_is_refused_while_a_preview_capture_is_stuck()
    {
        using var server = new FakeHyperionServer();
        var (service, capturer) = Create(server);
        capturer.Gate = new ManualResetEventSlim(false);
        await service.GetPreviewAsync(Timeout);

        Assert.False(await service.StartAsync());
        Assert.Equal(1, capturer.MaxConcurrent);

        capturer.Gate.Set();
    }
}
