using HyperTizen.Desktop;

namespace HyperTizen.Core.Tests;

public class UiFolderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "HyperTizenTests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    [Fact]
    public void Finds_the_folder_from_a_nested_directory()
    {
        string ui = Path.Combine(_root, "HyperTizenUI");
        string nested = Path.Combine(_root, "HyperTizen.Desktop", "bin", "Debug", "net10.0");
        Directory.CreateDirectory(ui);
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(ui, "index.html"), "");

        Assert.Equal(ui, UiFolder.Find(nested));
    }

    [Fact]
    public void Ignores_a_folder_without_index_html()
    {
        string nested = Path.Combine(_root, "a", "b");
        Directory.CreateDirectory(Path.Combine(_root, "a", "HyperTizenUI"));
        Directory.CreateDirectory(nested);

        Assert.Null(UiFolder.Find(nested));
    }
}
