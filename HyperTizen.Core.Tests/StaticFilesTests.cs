using HyperTizen.Core;

namespace HyperTizen.Core.Tests;

public class StaticFilesTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "HyperTizenTests-" + Guid.NewGuid().ToString("N"));
    private readonly string _ui;

    public StaticFilesTests()
    {
        _ui = Path.Combine(_directory, "ui");
        Directory.CreateDirectory(Path.Combine(_ui, "js"));
        File.WriteAllText(Path.Combine(_ui, "index.html"), "");
        File.WriteAllText(Path.Combine(_ui, "js", "app.js"), "");
        File.WriteAllText(Path.Combine(_directory, "secret.txt"), "secret");
        // A sibling whose name merely starts with the UI folder's name must not count as inside it.
        Directory.CreateDirectory(_ui + "-private");
        File.WriteAllText(Path.Combine(_ui + "-private", "notes.txt"), "secret");
    }

    public void Dispose() => Directory.Delete(_directory, true);

    [Fact]
    public void The_root_path_is_index_html()
    {
        Assert.Equal(Path.Combine(_ui, "index.html"), StaticFiles.Resolve(_ui, "/"));
    }

    [Fact]
    public void A_nested_file_is_found()
    {
        Assert.Equal(Path.Combine(_ui, "js", "app.js"), StaticFiles.Resolve(_ui, "/js/app.js"));
    }

    [Theory]
    [InlineData("/missing.html")]
    [InlineData("/js")]
    [InlineData("/js/")]
    [InlineData("/../secret.txt")]
    [InlineData("/js/../../secret.txt")]
    [InlineData("/..\\secret.txt")]
    [InlineData("/../ui-private/notes.txt")]
    [InlineData("//secret.txt")]
    public void Paths_that_are_not_files_inside_the_folder_resolve_to_nothing(string urlPath)
    {
        Assert.Null(StaticFiles.Resolve(_ui, urlPath));
    }

    [Fact]
    public void An_absolute_path_resolves_to_nothing()
    {
        string secret = Path.Combine(_directory, "secret.txt");

        Assert.Null(StaticFiles.Resolve(_ui, "/" + secret));
        Assert.Null(StaticFiles.Resolve(_ui, "/" + secret.Replace('\\', '/')));
    }

    [Theory]
    [InlineData("index.html", "text/html")]
    [InlineData("main.css", "text/css")]
    [InlineData("app.JS", "text/javascript")]
    [InlineData("icon.png", "image/png")]
    [InlineData("data.bin", "application/octet-stream")]
    public void Content_type_follows_the_extension(string file, string expected)
    {
        Assert.Equal(expected, StaticFiles.ContentTypeFor(file));
    }
}
