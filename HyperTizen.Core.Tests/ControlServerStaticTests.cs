using System.Net;
using HyperTizen.Core;

namespace HyperTizen.Core.Tests;

public class ControlServerStaticTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "HyperTizenTests-" + Guid.NewGuid().ToString("N"));
    private readonly int _port = TestHelpers.FreePort();
    private readonly ControlServer _server;
    private readonly HttpClient _http = new();

    public ControlServerStaticTests()
    {
        string ui = Path.Combine(_directory, "ui");
        Directory.CreateDirectory(Path.Combine(ui, "js"));
        File.WriteAllText(Path.Combine(ui, "index.html"), "<h1>hello</h1>");
        File.WriteAllText(Path.Combine(ui, "main.css"), "body{}");
        File.WriteAllText(Path.Combine(ui, "js", "app.js"), "var a;");
        File.WriteAllText(Path.Combine(_directory, "secret.txt"), "secret");

        _server = new ControlServer("http://127.0.0.1:" + _port + "/", new MemorySettingsStore(), new FakeControlActions(), new ListLog(), ui);
        _server.Start();
    }

    public void Dispose()
    {
        _http.Dispose();
        _server.StopAsync().GetAwaiter().GetResult();
        Directory.Delete(_directory, true);
    }

    private string Url(string path) => "http://127.0.0.1:" + _port + path;

    [Fact]
    public async Task Root_serves_index_html()
    {
        var response = await _http.GetAsync(Url("/"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("<h1>hello</h1>", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/main.css", "text/css")]
    [InlineData("/js/app.js", "text/javascript")]
    public async Task Files_are_served_with_their_content_type(string path, string contentType)
    {
        var response = await _http.GetAsync(Url(path));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(contentType, response.Content.Headers.ContentType!.MediaType);
    }

    [Theory]
    [InlineData("/missing.html")]
    [InlineData("/js")]
    [InlineData("/js/")]
    [InlineData("/..%2Fsecret.txt")]
    [InlineData("/js/..%2F..%2Fsecret.txt")]
    [InlineData("/%2e%2e/secret.txt")]
    public async Task Anything_else_is_refused_and_never_returns_file_contents(string path)
    {
        var response = await _http.GetAsync(Url(path));

        // Windows itself answers 403 to encoded slashes before the request reaches the server;
        // StaticFilesTests covers the server's own guard for those paths.
        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.Forbidden });
        Assert.DoesNotContain("secret", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Websocket_requests_still_work_next_to_static_files()
    {
        using var client = await Ws.ConnectAsync("ws://127.0.0.1:" + _port + "/");

        await Ws.SendAsync(client, "{\"event\":5}");

        Assert.Contains("\"Event\":6", await Ws.ReceiveAsync(client));
    }
}
