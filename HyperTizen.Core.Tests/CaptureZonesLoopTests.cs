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
    public async Task After_a_first_whole_frame_one_batch_is_captured_and_sent_at_a_time()
    {
        using var server = new FakeHyperionServer();
        var setup = new Setup(server);
        setup.Capturer.BatchSize = 2;
        setup.Options.Layout = new CaptureLayout(3, 0, 0, 0);

        await setup.Service.StartAsync();
        await TestHelpers.WaitUntilAsync(() => setup.Capturer.Captures >= 5 && Images(server) >= 4);
        await setup.Service.StopAsync();

        // Three points in batches of two: two, then the one left over.
        Assert.Equal(new[] { 3, 2, 1, 2, 1 }, setup.Capturer.PointCounts.Take(5));
        Assert.Empty(setup.Log.Errors);
    }

    [Fact]
    public async Task A_batch_replaces_only_the_colors_of_its_own_points()
    {
        using var server = new FakeHyperionServer();
        var setup = new Setup(server);
        setup.Capturer.BatchSize = 2;
        setup.Capturer.StampColors = true;
        setup.Options.Layout = new CaptureLayout(4, 0, 0, 0);

        await setup.Service.StartAsync();
        await TestHelpers.WaitUntilAsync(() => setup.Capturer.Captures >= 3);
        PreviewFrame frame = await setup.Service.GetPreviewAsync(TimeSpan.FromSeconds(3));
        await setup.Service.StopAsync();

        // Each batch was captured together, and one of them one capture later than the other.
        int[] order = frame.Layout.SpreadOrder;
        Assert.Equal(frame.Colors[order[0]].R, frame.Colors[order[1]].R);
        Assert.Equal(frame.Colors[order[2]].R, frame.Colors[order[3]].R);
        Assert.Equal(1, Math.Abs(frame.Colors[order[0]].R - frame.Colors[order[2]].R));
    }

    [Fact]
    public async Task Batches_follow_the_spread_order_of_the_layout()
    {
        using var server = new FakeHyperionServer();
        var setup = new Setup(server);
        setup.Capturer.BatchSize = 2;
        CaptureLayout layout = CaptureLayout.Default;
        setup.Options.Layout = layout;

        await setup.Service.StartAsync();
        await TestHelpers.WaitUntilAsync(() => setup.Capturer.Captures >= 8);
        await setup.Service.StopAsync();

        // After the whole frame, seven batches of two cover the fourteen points in spread order.
        var asked = setup.Capturer.PointsAsked.Skip(1).Take(7).SelectMany(points => points).ToArray();
        var expected = layout.SpreadOrder.Select(index => layout.Points[index]).ToArray();
        Assert.Equal(expected.Select(point => (point.X, point.Y)), asked.Select(point => (point.X, point.Y)));
    }

    [Fact]
    public async Task A_batch_that_holds_every_point_captures_the_whole_frame_each_time()
    {
        using var server = new FakeHyperionServer();
        var setup = new Setup(server);
        setup.Capturer.BatchSize = 16;

        await setup.Service.StartAsync();
        await TestHelpers.WaitUntilAsync(() => setup.Capturer.Captures >= 3);
        await setup.Service.StopAsync();

        Assert.All(setup.Capturer.PointCounts, count => Assert.Equal(14, count));
    }

    [Fact]
    public async Task A_layout_change_starts_over_with_a_whole_frame()
    {
        using var server = new FakeHyperionServer();
        var setup = new Setup(server);
        setup.Capturer.BatchSize = 2;
        setup.Options.Layout = new CaptureLayout(4, 0, 0, 0);
        await setup.Service.StartAsync();
        await TestHelpers.WaitUntilAsync(() => setup.Capturer.Captures >= 3);

        setup.Options.Layout = new CaptureLayout(6, 0, 0, 0);

        await TestHelpers.WaitUntilAsync(() => setup.Capturer.PointCounts.Contains(6));
        await setup.Service.StopAsync();
        Assert.Empty(setup.Log.Errors);
    }

    [Fact]
    public async Task Capture_starts_over_with_a_whole_frame_after_a_pause()
    {
        using var server = new FakeHyperionServer();
        var setup = new Setup(server);
        setup.Capturer.BatchSize = 2;
        setup.Options.Layout = new CaptureLayout(4, 0, 0, 0);
        await setup.Service.StartAsync();
        await TestHelpers.WaitUntilAsync(() => setup.Capturer.Captures >= 3);

        setup.Service.Paused = true;
        // Long enough for a capture that was under way to finish.
        await Task.Delay(200);
        int before = setup.Capturer.PointCounts.Count;
        setup.Service.Paused = false;

        await TestHelpers.WaitUntilAsync(() => setup.Capturer.PointCounts.Count > before);
        await setup.Service.StopAsync();
        Assert.Equal(4, setup.Capturer.PointCounts.ElementAt(before));
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
    public async Task The_rate_is_right_from_the_first_frames_on()
    {
        using var server = new FakeHyperionServer();
        var setup = new Setup(server);
        setup.Options.MaxFps = 10;

        await setup.Service.StartAsync();
        // Well under the five seconds the rate looks back over.
        await TestHelpers.WaitUntilAsync(() => Images(server) >= 5);
        double fps = setup.Service.Fps;
        await setup.Service.StopAsync();

        Assert.InRange(fps, 5.0, 15.0);
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
    public async Task A_preview_that_timed_out_is_picked_up_by_the_next_request()
    {
        using var server = new FakeHyperionServer();
        var setup = new Setup(server);
        // A capture slower than one request waits for, as with many zones on a slow TV.
        setup.Capturer.Gate = new ManualResetEventSlim(false);

        PreviewFrame first = await setup.Service.GetPreviewAsync(TimeSpan.FromMilliseconds(100));
        Assert.Null(first.Colors);

        Task<PreviewFrame> second = setup.Service.GetPreviewAsync(TimeSpan.FromSeconds(3));
        await Task.Delay(50);
        setup.Capturer.Gate.Set();
        PreviewFrame frame = await second;

        Assert.NotNull(frame.Colors);
        Assert.Equal(1, setup.Capturer.Captures);
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
