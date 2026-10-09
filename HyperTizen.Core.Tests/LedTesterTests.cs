using HyperTizen.Core;

namespace HyperTizen.Core.Tests;

public class LedTesterTests
{
    private static readonly TimeSpan Step = TimeSpan.FromMilliseconds(30);

    private sealed class Fixture
    {
        public HyperionClient Client { get; }
        public CaptureService Capture { get; }
        public CaptureOptions Options { get; } = new();
        public FakeCapturer Capturer { get; } = new();
        public LedTester Tester { get; }

        public Fixture(string? serverUri, TimeSpan? connectTimeout = null)
        {
            var log = new ListLog();
            Client = new HyperionClient(log, new[] { TimeSpan.FromMilliseconds(50) });
            Client.SetServer(serverUri);
            Capture = new CaptureService(Capturer, Client, log, null, Options);
            Tester = new LedTester(Client, Capture, Options, Step, connectTimeout ?? TimeSpan.FromSeconds(5));
        }
    }

    private static string Color(int r, int g, int b, int priority) =>
        "{\"command\":\"color\",\"color\":[" + r + "," + g + "," + b + "],\"priority\":" + priority + ",\"origin\":\"HyperTizen\"}";

    [Fact]
    public async Task Sends_red_green_blue_white_then_clear_at_the_configured_priority()
    {
        using var server = new FakeHyperionServer();
        var fixture = new Fixture(server.Uri);
        fixture.Options.Priority = 50;

        TestLedsResultEvent result = await fixture.Tester.RunAsync();

        Assert.True(result.ok);
        Assert.Null(result.error);
        await TestHelpers.WaitUntilAsync(() => server.Messages.Count == 5);
        Assert.Equal(new[]
        {
            Color(255, 0, 0, 50),
            Color(0, 255, 0, 50),
            Color(0, 0, 255, 50),
            Color(255, 255, 255, 50),
            "{\"command\":\"clear\",\"priority\":50}"
        }, server.Messages.ToArray());
    }

    [Fact]
    public async Task Fails_with_the_servers_reason_when_the_colors_are_refused()
    {
        using var server = new FakeHyperionServer { Reply = message => FakeHyperionServer.Refuse(message, "No Authorization") };
        var fixture = new Fixture(server.Uri);

        TestLedsResultEvent result = await fixture.Tester.RunAsync();

        Assert.False(result.ok);
        Assert.Equal("The server refused 'color': No Authorization", result.error);
        Assert.False(fixture.Capture.Paused);
        Assert.False(fixture.Client.IsStarted);
    }

    [Fact]
    public async Task Disconnects_afterwards_when_capture_is_not_running()
    {
        using var server = new FakeHyperionServer();
        var fixture = new Fixture(server.Uri);

        await fixture.Tester.RunAsync();

        Assert.False(fixture.Client.IsStarted);
        Assert.False(fixture.Client.IsConnected);
    }

    [Fact]
    public async Task Fails_without_a_server()
    {
        var fixture = new Fixture(null);

        TestLedsResultEvent result = await fixture.Tester.RunAsync();

        Assert.False(result.ok);
        Assert.Equal("No server is configured.", result.error);
        Assert.False(fixture.Client.IsStarted);
    }

    [Fact]
    public async Task Fails_when_the_server_is_unreachable_and_works_once_it_is_back()
    {
        int port = TestHelpers.FreePort();
        var fixture = new Fixture("ws://127.0.0.1:" + port + "/", TimeSpan.FromMilliseconds(300));

        TestLedsResultEvent result = await fixture.Tester.RunAsync();

        Assert.False(result.ok);
        Assert.Equal("Could not connect to the server.", result.error);
        Assert.False(fixture.Client.IsStarted);

        using var server = new FakeHyperionServer();
        fixture.Client.SetServer(server.Uri);
        Assert.True((await fixture.Tester.RunAsync()).ok);
    }

    [Fact]
    public async Task A_second_test_is_refused_while_one_is_running()
    {
        using var server = new FakeHyperionServer();
        var fixture = new Fixture(server.Uri);

        Task<TestLedsResultEvent> first = fixture.Tester.RunAsync();
        TestLedsResultEvent second = await fixture.Tester.RunAsync();

        Assert.False(second.ok);
        Assert.Equal("A test is already running.", second.error);
        Assert.True((await first).ok);
    }

    [Fact]
    public async Task Captured_frames_pause_during_the_test_and_resume_after()
    {
        using var server = new FakeHyperionServer();
        var fixture = new Fixture(server.Uri);
        await fixture.Capture.StartAsync();
        await TestHelpers.WaitUntilAsync(() => server.Messages.Count >= 2);

        TestLedsResultEvent result = await fixture.Tester.RunAsync();

        Assert.True(result.ok);
        // The server records messages on its own thread; wait for the last one of the test.
        await TestHelpers.WaitUntilAsync(() => server.Messages.Contains("{\"command\":\"clear\",\"priority\":99}"));
        string[] messages = server.Messages.ToArray();
        int red = Array.IndexOf(messages, Color(255, 0, 0, 99));
        int clear = Array.IndexOf(messages, "{\"command\":\"clear\",\"priority\":99}");
        Assert.True(red >= 0 && clear > red);
        // At most one frame that was already in flight may land right after the first color.
        Assert.True(messages.Skip(red + 2).Take(clear - red - 2).All(m => m.Contains("\"command\":\"color\"")),
            "captured frames were sent during the test");

        int afterTest = server.Messages.Count;
        await TestHelpers.WaitUntilAsync(() => server.Messages.Count > afterTest);
        Assert.True(fixture.Client.IsStarted);
        Assert.False(fixture.Capture.Paused);
        await fixture.Capture.StopAsync();
    }
}
