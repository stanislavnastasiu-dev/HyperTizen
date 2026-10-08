using HyperTizen.Core;
using Newtonsoft.Json.Linq;

namespace HyperTizen.Core.Tests;

public class HyperionClientTests
{
    private static readonly TimeSpan[] FastBackoff = { TimeSpan.FromMilliseconds(50) };

    [Fact]
    public async Task Sends_an_image_command_once_connected()
    {
        using var server = new FakeHyperionServer();
        var client = new HyperionClient(new ListLog(), FastBackoff);
        client.SetServer(server.Uri);
        client.Start();
        await TestHelpers.WaitUntilAsync(() => client.IsConnected);

        bool sent = await client.SendImageAsync("QUJD");

        Assert.True(sent);
        await TestHelpers.WaitUntilAsync(() => !server.Messages.IsEmpty);
        server.Messages.TryPeek(out string? json);
        var message = JObject.Parse(json!);
        Assert.Equal("image", (string?)message["command"]);
        Assert.Equal("QUJD", (string?)message["imagedata"]);
        Assert.Equal("HyperTizen Data", (string?)message["name"]);
        Assert.Equal("auto", (string?)message["format"]);
        Assert.Equal(99, (int)message["priority"]!);
        Assert.Equal("HyperTizen", (string?)message["origin"]);
        await client.StopAsync();
    }

    [Fact]
    public async Task Sends_the_exact_clear_command()
    {
        using var server = new FakeHyperionServer();
        var client = new HyperionClient(new ListLog(), FastBackoff);
        client.SetServer(server.Uri);
        client.Start();
        await TestHelpers.WaitUntilAsync(() => client.IsConnected);

        Assert.True(await client.SendClearAsync());
        await client.StopAsync();

        await TestHelpers.WaitUntilAsync(() => !server.Messages.IsEmpty);
        server.Messages.TryPeek(out string? json);
        Assert.Equal("{\"command\":\"clear\",\"priority\":99}", json);
    }

    [Fact]
    public async Task Send_returns_false_when_not_connected()
    {
        var client = new HyperionClient(new ListLog(), FastBackoff);

        Assert.False(await client.SendImageAsync("QUJD"));
        Assert.False(await client.SendClearAsync());
    }

    [Fact]
    public async Task Reconnects_after_the_server_drops_the_connection()
    {
        using var server = new FakeHyperionServer();
        var client = new HyperionClient(new ListLog(), FastBackoff);
        client.SetServer(server.Uri);
        client.Start();
        await TestHelpers.WaitUntilAsync(() => server.ConnectionCount == 1);

        server.DropConnections();

        await TestHelpers.WaitUntilAsync(() => server.ConnectionCount == 2);
        await TestHelpers.WaitUntilAsync(() => client.IsConnected);
        await client.StopAsync();
    }

    [Fact]
    public async Task Switches_to_a_new_server()
    {
        using var first = new FakeHyperionServer();
        using var second = new FakeHyperionServer();
        var client = new HyperionClient(new ListLog(), FastBackoff);
        client.SetServer(first.Uri);
        client.Start();
        await TestHelpers.WaitUntilAsync(() => first.ConnectionCount == 1);

        client.SetServer(second.Uri);

        await TestHelpers.WaitUntilAsync(() => second.ConnectionCount == 1);
        await client.StopAsync();
    }

    [Fact]
    public async Task Survives_an_invalid_server_address()
    {
        using var server = new FakeHyperionServer();
        var log = new ListLog();
        var client = new HyperionClient(log, FastBackoff);
        client.SetServer("this is not a uri");
        client.Start();
        await TestHelpers.WaitUntilAsync(() => !log.Errors.IsEmpty);

        client.SetServer(server.Uri);

        await TestHelpers.WaitUntilAsync(() => client.IsConnected);
        await client.StopAsync();
    }

    [Fact]
    public async Task Stop_ends_the_connection_and_does_not_reconnect()
    {
        using var server = new FakeHyperionServer();
        var client = new HyperionClient(new ListLog(), FastBackoff);
        client.SetServer(server.Uri);
        client.Start();
        await TestHelpers.WaitUntilAsync(() => client.IsConnected);

        await client.StopAsync();
        await Task.Delay(300);

        Assert.False(client.IsConnected);
        Assert.Equal(1, server.ConnectionCount);
    }
}
