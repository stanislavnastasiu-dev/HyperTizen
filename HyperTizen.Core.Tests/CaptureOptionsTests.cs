using HyperTizen.Core;
using Newtonsoft.Json.Linq;

namespace HyperTizen.Core.Tests;

public class CaptureOptionsTests
{
    private static (CaptureService Service, FakeCapturer Capturer, CaptureOptions Options) Create(FakeHyperionServer server, Action? onFrameSent = null)
    {
        var log = new ListLog();
        var client = new HyperionClient(log, new[] { TimeSpan.FromMilliseconds(50) });
        client.SetServer(server.Uri);
        var capturer = new FakeCapturer();
        var options = new CaptureOptions();
        return (new CaptureService(capturer, client, log, null, options, onFrameSent), capturer, options);
    }

    private static bool IsImage(string json) => json.Contains("\"command\":\"image\"");

    [Fact]
    public async Task Frame_rate_limit_slows_the_loop()
    {
        using var server = new FakeHyperionServer();
        var (service, capturer, options) = Create(server);
        options.MaxFps = 10;

        await service.StartAsync();
        await TestHelpers.WaitUntilAsync(() => capturer.Captures >= 1);
        int before = capturer.Captures;
        await Task.Delay(600);
        int during = capturer.Captures - before;
        await service.StopAsync();

        Assert.InRange(during, 3, 8);
    }

    [Fact]
    public async Task Images_and_clear_use_the_configured_priority()
    {
        using var server = new FakeHyperionServer();
        var (service, _, options) = Create(server);
        options.Priority = 42;

        await service.StartAsync();
        await TestHelpers.WaitUntilAsync(() => server.Messages.Count >= 2);
        await service.StopAsync();

        await TestHelpers.WaitUntilAsync(() => server.Messages.Last() == "{\"command\":\"clear\",\"priority\":42}");
        Assert.Equal(42, (int)JObject.Parse(server.Messages.First())["priority"]!);
    }

    [Fact]
    public async Task Changing_priority_clears_the_old_one_first()
    {
        using var server = new FakeHyperionServer();
        var (service, _, options) = Create(server);
        options.Priority = 42;
        await service.StartAsync();
        await TestHelpers.WaitUntilAsync(() => server.Messages.Count >= 2);

        options.Priority = 43;

        await TestHelpers.WaitUntilAsync(() => server.Messages.Any(m => IsImage(m) && (int)JObject.Parse(m)["priority"]! == 43));
        await service.StopAsync();
        string[] messages = server.Messages.ToArray();
        int clearOld = Array.IndexOf(messages, "{\"command\":\"clear\",\"priority\":42}");
        int firstNew = Array.FindIndex(messages, m => IsImage(m) && (int)JObject.Parse(m)["priority"]! == 43);
        Assert.True(clearOld >= 0, "old priority was never cleared");
        Assert.True(clearOld < firstNew, "old priority was cleared after the first frame at the new one");
        Assert.DoesNotContain(messages.Skip(firstNew), m => IsImage(m) && (int)JObject.Parse(m)["priority"]! == 42);
    }

    [Fact]
    public async Task Paused_loop_sends_nothing_and_resumes()
    {
        using var server = new FakeHyperionServer();
        var (service, _, _) = Create(server);
        await service.StartAsync();
        await TestHelpers.WaitUntilAsync(() => server.Messages.Count >= 2);

        service.Paused = true;
        await Task.Delay(150);
        int whilePaused = server.Messages.Count;
        await Task.Delay(300);
        Assert.Equal(whilePaused, server.Messages.Count);

        service.Paused = false;
        await TestHelpers.WaitUntilAsync(() => server.Messages.Count > whilePaused);
        await service.StopAsync();
    }

    [Fact]
    public async Task Reports_each_sent_frame()
    {
        using var server = new FakeHyperionServer();
        int sent = 0;
        var (service, _, _) = Create(server, () => Interlocked.Increment(ref sent));

        await service.StartAsync();

        await TestHelpers.WaitUntilAsync(() => sent >= 3);
        await service.StopAsync();
    }

    [Fact]
    public async Task State_follows_the_lifecycle()
    {
        using var server = new FakeHyperionServer();
        var (service, _, _) = Create(server);

        Assert.Equal("unknown", service.State);
        await service.StartAsync();
        Assert.Equal("running", service.State);
        await service.StopAsync();
        Assert.Equal("stopped", service.State);
    }

    [Fact]
    public async Task State_is_unsupported_when_the_capturer_cannot_initialize()
    {
        using var server = new FakeHyperionServer();
        var (service, capturer, _) = Create(server);
        capturer.Supported = false;

        await service.StartAsync();

        Assert.Equal("unsupported", service.State);
    }
}
