using HyperTizen.Core;

namespace HyperTizen.Core.Tests;

public class HyperionClientReplyTests
{
    private static readonly TimeSpan[] FastBackoff = { TimeSpan.FromMilliseconds(50) };

    private static async Task<HyperionClient> ConnectedClient(FakeHyperionServer server, ListLog? log = null, TimeSpan? replyTimeout = null)
    {
        var client = new HyperionClient(log ?? new ListLog(), FastBackoff, null, replyTimeout);
        client.SetServer(server.Uri);
        client.Start();
        await TestHelpers.WaitUntilAsync(() => client.IsConnected);
        return client;
    }

    [Fact]
    public async Task Nothing_is_refused_while_the_server_accepts()
    {
        using var server = new FakeHyperionServer();
        var client = await ConnectedClient(server);

        await client.SendImageAsync("QUJD");
        await TestHelpers.WaitUntilAsync(() => !server.Messages.IsEmpty);
        await Task.Delay(100);

        Assert.Null(client.Rejection);
        await client.StopAsync();
    }

    [Fact]
    public async Task A_refused_command_is_reported_with_the_servers_reason_and_logged_once()
    {
        using var server = new FakeHyperionServer { Reply = message => FakeHyperionServer.Refuse(message, "No Authorization") };
        var log = new ListLog();
        var client = await ConnectedClient(server, log);

        await client.SendImageAsync("QUJD");
        await client.SendImageAsync("QUJD");
        await client.SendImageAsync("QUJD");

        await TestHelpers.WaitUntilAsync(() => client.Rejection != null);
        Assert.Equal("The server refused 'image': No Authorization", client.Rejection);
        await TestHelpers.WaitUntilAsync(() => server.Messages.Count == 3);
        await Task.Delay(100);
        Assert.Single(log.Errors);
        await client.StopAsync();
    }

    [Fact]
    public async Task A_refusal_ends_once_the_server_accepts_that_command_again()
    {
        using var server = new FakeHyperionServer { Reply = message => FakeHyperionServer.Refuse(message, "No Authorization") };
        var client = await ConnectedClient(server);
        await client.SendImageAsync("QUJD");
        await TestHelpers.WaitUntilAsync(() => client.Rejection != null);

        server.Reply = FakeHyperionServer.Accept;
        await client.SendImageAsync("QUJD");

        await TestHelpers.WaitUntilAsync(() => client.Rejection == null);
        await client.StopAsync();
    }

    [Fact]
    public async Task A_refused_instance_stays_reported_while_images_are_accepted()
    {
        using var server = new FakeHyperionServer
        {
            Reply = message => message.Contains("switchTo")
                ? FakeHyperionServer.Refuse(message, "Selected Hyperion instance isn't running")
                : FakeHyperionServer.Accept(message)
        };
        var client = await ConnectedClient(server);

        await client.SetInstanceAsync(3);
        await TestHelpers.WaitUntilAsync(() => client.Rejection != null);
        await client.SendImageAsync("QUJD");
        await TestHelpers.WaitUntilAsync(() => server.Messages.Count == 2);
        await Task.Delay(100);

        Assert.Equal("The server refused 'instance-switchTo': Selected Hyperion instance isn't running", client.Rejection);
        await client.StopAsync();
    }

    [Fact]
    public async Task A_refusal_is_forgotten_when_the_server_changes()
    {
        using var first = new FakeHyperionServer { Reply = message => FakeHyperionServer.Refuse(message, "No Authorization") };
        using var second = new FakeHyperionServer();
        var client = await ConnectedClient(first);
        await client.SendImageAsync("QUJD");
        await TestHelpers.WaitUntilAsync(() => client.Rejection != null);

        client.SetServer(second.Uri);

        await TestHelpers.WaitUntilAsync(() => second.ConnectionCount == 1);
        Assert.Null(client.Rejection);
        await client.StopAsync();
    }

    [Fact]
    public async Task Reconnects_when_a_server_that_answered_stops_answering()
    {
        using var server = new FakeHyperionServer();
        var log = new ListLog();
        var client = await ConnectedClient(server, log, TimeSpan.FromMilliseconds(150));
        await client.SendImageAsync("QUJD");
        await TestHelpers.WaitUntilAsync(() => server.Messages.Count == 1);
        await Task.Delay(100);

        server.Silent = true;
        Assert.True(await client.SendImageAsync("QUJD"));
        await Task.Delay(300);

        Assert.False(await client.SendImageAsync("QUJD"));
        Assert.Contains("The server stopped answering", log.Errors);
        server.Silent = false;
        await TestHelpers.WaitUntilAsync(() => server.ConnectionCount == 2);
        await TestHelpers.WaitUntilAsync(() => client.IsConnected);
        Assert.True(await client.SendImageAsync("QUJD"));
        await client.StopAsync();
    }

    [Fact]
    public async Task A_server_that_never_answers_is_not_dropped()
    {
        using var server = new FakeHyperionServer { Silent = true };
        var client = await ConnectedClient(server, null, TimeSpan.FromMilliseconds(100));

        Assert.True(await client.SendImageAsync("QUJD"));
        await Task.Delay(250);

        Assert.True(await client.SendImageAsync("QUJD"));
        Assert.Equal(1, server.ConnectionCount);
        await client.StopAsync();
    }

    [Fact]
    public async Task A_pause_between_commands_is_not_taken_for_silence()
    {
        using var server = new FakeHyperionServer();
        var client = await ConnectedClient(server, null, TimeSpan.FromMilliseconds(100));

        Assert.True(await client.SendImageAsync("QUJD"));
        await Task.Delay(300);

        Assert.True(await client.SendImageAsync("QUJD"));
        Assert.Equal(1, server.ConnectionCount);
        await client.StopAsync();
    }

    [Fact]
    public async Task The_instance_is_chosen_before_anything_else_on_every_connection()
    {
        using var server = new FakeHyperionServer();
        var client = new HyperionClient(new ListLog(), FastBackoff);
        await client.SetInstanceAsync(2);
        client.SetServer(server.Uri);
        client.Start();
        await TestHelpers.WaitUntilAsync(() => client.IsConnected);
        await client.SendImageAsync("QUJD");
        await TestHelpers.WaitUntilAsync(() => server.Messages.Count == 2);

        server.DropConnections();
        await TestHelpers.WaitUntilAsync(() => server.ConnectionCount == 2);
        await TestHelpers.WaitUntilAsync(() => server.Messages.Count == 3);

        const string switchTo = "{\"command\":\"instance\",\"subcommand\":\"switchTo\",\"instance\":2}";
        string[] messages = server.Messages.ToArray();
        Assert.Equal(switchTo, messages[0]);
        Assert.Equal(switchTo, messages[2]);
        await client.StopAsync();
    }

    [Fact]
    public async Task The_first_instance_needs_no_command_and_a_change_is_sent_at_once()
    {
        using var server = new FakeHyperionServer();
        var client = await ConnectedClient(server);
        await client.SetInstanceAsync(0);
        await client.SendClearAsync();
        await TestHelpers.WaitUntilAsync(() => server.Messages.Count == 1);

        await client.SetInstanceAsync(1);
        await client.SetInstanceAsync(0);

        await TestHelpers.WaitUntilAsync(() => server.Messages.Count == 3);
        string[] messages = server.Messages.ToArray();
        Assert.Equal("{\"command\":\"clear\",\"priority\":99}", messages[0]);
        Assert.Equal("{\"command\":\"instance\",\"subcommand\":\"switchTo\",\"instance\":1}", messages[1]);
        Assert.Equal("{\"command\":\"instance\",\"subcommand\":\"switchTo\",\"instance\":0}", messages[2]);
        await client.StopAsync();
    }
}
