using HyperTizen.Core;

namespace HyperTizen.Core.Tests;

public class CaptureZonesLoopTests
{
    private sealed class Setup
    {
        public ListLog Log { get; } = new();
        public FakeCapturer Capturer { get; } = new();
        public CaptureOptions Options { get; } = new();
        public CaptureService Service { get; }

        public Setup(FakeHyperionServer server)
        {
            var client = new HyperionClient(Log, new[] { TimeSpan.FromMilliseconds(50) });
            client.SetServer(server.Uri);
            Service = new CaptureService(Capturer, client, Log, null, Options);
        }
    }

    private static int Images(FakeHyperionServer server) => server.Messages.Count(json => json.Contains("\"command\":\"image\""));

    [Fact]
    public async Task The_capturer_is_asked_for_the_points_of_the_layout()
    {
        using var server = new FakeHyperionServer();
        var setup = new Setup(server);

        await setup.Service.StartAsync();
        await TestHelpers.WaitUntilAsync(() => setup.Capturer.Captures >= 1);
        await setup.Service.StopAsync();

        Assert.Equal(14, setup.Capturer.LastPointCount);
    }

    [Fact]
    public async Task A_layout_change_shows_up_in_the_next_frames_without_a_restart()
    {
        using var server = new FakeHyperionServer();
        var setup = new Setup(server);
        await setup.Service.StartAsync();
        await TestHelpers.WaitUntilAsync(() => Images(server) >= 1);

        setup.Options.Layout = new CaptureLayout(2, 0, 0, 0);

        await TestHelpers.WaitUntilAsync(() => setup.Capturer.LastPointCount == 2);
        int before = Images(server);
        await TestHelpers.WaitUntilAsync(() => Images(server) > before + 1);
        await setup.Service.StopAsync();
        Assert.Empty(setup.Log.Errors);
    }

    [Fact]
    public async Task A_capturer_returning_the_wrong_number_of_colors_is_a_logged_failure()
    {
        using var server = new FakeHyperionServer();
        var setup = new Setup(server);
        setup.Capturer.ExtraColors = 1;

        await setup.Service.StartAsync();
        await TestHelpers.WaitUntilAsync(() => setup.Log.Errors.Contains("Capture failed"));
        await setup.Service.StopAsync();

        Assert.Equal(0, Images(server));
    }

    [Fact]
    public async Task Timing_is_reported_while_frames_are_sent()
    {
        using var server = new FakeHyperionServer();
        var setup = new Setup(server);
        Assert.Null(setup.Service.FrameMs);
        Assert.Equal(0, setup.Service.Fps);

        await setup.Service.StartAsync();
        // More frames than the assertion needs: the server sees a frame a moment before it is counted.
        await TestHelpers.WaitUntilAsync(() => Images(server) >= 8);
        int? frameMs = setup.Service.FrameMs;
        double fps = setup.Service.Fps;
        await setup.Service.StopAsync();

        // The fake capturer takes 5 ms per frame.
        Assert.NotNull(frameMs);
        Assert.InRange(frameMs!.Value, 5, 2000);
        Assert.True(fps >= 1.0, "fps was " + fps);
    }

    [Fact]
    public async Task A_preview_carries_the_layout_its_colors_were_captured_with()
    {
        using var server = new FakeHyperionServer();
        var setup = new Setup(server);
        setup.Options.Layout = new CaptureLayout(1, 1, 1, 1);

        PreviewFrame frame = await setup.Service.GetPreviewAsync(TimeSpan.FromSeconds(3));

        Assert.Equal(4, frame.Colors.Length);
        Assert.Equal(4, frame.Layout.Points.Length);
    }

    [Fact]
    public async Task Previews_stay_consistent_while_the_layout_keeps_changing()
    {
        using var server = new FakeHyperionServer();
        var setup = new Setup(server);
        await setup.Service.StartAsync();
        await TestHelpers.WaitUntilAsync(() => Images(server) >= 1);

        for (int i = 1; i <= 20; i++)
        {
            setup.Options.Layout = new CaptureLayout(i % 16 + 1, i % 5, i % 3, i % 7);
            PreviewFrame frame = await setup.Service.GetPreviewAsync(TimeSpan.FromSeconds(3));
            Assert.NotNull(frame.Colors);
            Assert.Equal(frame.Layout.Points.Length, frame.Colors.Length);
        }

        await setup.Service.StopAsync();
        Assert.Empty(setup.Log.Errors);
    }

    [Fact]
    public async Task A_preview_without_colors_has_no_layout()
    {
        using var server = new FakeHyperionServer();
        var setup = new Setup(server);
        setup.Capturer.Supported = false;

        PreviewFrame frame = await setup.Service.GetPreviewAsync(TimeSpan.FromSeconds(3));

        Assert.Null(frame.Colors);
        Assert.Null(frame.Layout);
        Assert.Equal(CaptureService.NotSupported, frame.Error);
    }
}
