using HyperTizen.Desktop;

namespace HyperTizen.Core.Tests;

public class JsonFileSettingsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "HyperTizenTests-" + Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(_directory, "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    [Fact]
    public void Values_survive_a_reload()
    {
        new JsonFileSettingsStore(FilePath).Set("rpcServer", "ws://10.0.0.5:8090");

        var reloaded = new JsonFileSettingsStore(FilePath);

        Assert.True(reloaded.Contains("rpcServer"));
        Assert.Equal("ws://10.0.0.5:8090", reloaded.Get("rpcServer"));
    }

    [Fact]
    public void A_missing_file_means_no_settings()
    {
        Assert.False(new JsonFileSettingsStore(FilePath).Contains("enabled"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{ this is not json")]
    [InlineData("[1,2,3]")]
    [InlineData("null")]
    public void An_unreadable_file_starts_empty_and_is_overwritten_on_save(string content)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FilePath, content);

        var store = new JsonFileSettingsStore(FilePath);
        Assert.False(store.Contains("enabled"));
        store.Set("enabled", "true");

        Assert.Equal("true", new JsonFileSettingsStore(FilePath).Get("enabled"));
    }
}
