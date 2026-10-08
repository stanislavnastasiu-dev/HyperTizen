using HyperTizen.Core;
using Newtonsoft.Json.Linq;

namespace HyperTizen.Core.Tests;

public class HyperionClientCommandTests
{
    private static async Task<HyperionClient> ConnectedClient(FakeHyperionServer server)
    {
        var client = new HyperionClient(new ListLog(), new[] { TimeSpan.FromMilliseconds(50) });
        client.SetServer(server.Uri);
        client.Start();
        await TestHelpers.WaitUntilAsync(() => client.IsConnected);
        return client;
    }

    [Fact]
    public async Task Sends_the_exact_color_command()
    {
        using var server = new FakeHyperionServer();
        var client = await ConnectedClient(server);

        Assert.True(await client.SendColorAsync(255, 0, 128, 42));
        await client.StopAsync();

        await TestHelpers.WaitUntilAsync(() => !server.Messages.IsEmpty);
        server.Messages.TryPeek(out string? json);
        Assert.Equal("{\"command\":\"color\",\"color\":[255,0,128],\"priority\":42,\"origin\":\"HyperTizen\"}", json);
    }

    [Fact]
    public async Task Image_and_clear_use_the_given_priority()
    {
        using var server = new FakeHyperionServer();
        var client = await ConnectedClient(server);

        await client.SendImageAsync("QUJD", 7);
        await client.SendClearAsync(7);
        await client.StopAsync();

        await TestHelpers.WaitUntilAsync(() => server.Messages.Count == 2);
        string[] messages = server.Messages.ToArray();
        Assert.Equal(7, (int)JObject.Parse(messages[0])["priority"]!);
        Assert.Equal("{\"command\":\"clear\",\"priority\":7}", messages[1]);
    }

    [Fact]
    public async Task Send_color_returns_false_when_not_connected()
    {
        var client = new HyperionClient(new ListLog());

        Assert.False(await client.SendColorAsync(1, 2, 3));
    }

    [Fact]
    public async Task Reports_whether_it_has_a_server_and_is_started()
    {
        using var server = new FakeHyperionServer();
        var client = new HyperionClient(new ListLog());

        Assert.False(client.HasServer);
        Assert.False(client.IsStarted);

        client.SetServer(server.Uri);
        client.Start();
        Assert.True(client.HasServer);
        Assert.True(client.IsStarted);

        await client.StopAsync();
        client.SetServer(null);
        Assert.False(client.HasServer);
        Assert.False(client.IsStarted);
    }
}
