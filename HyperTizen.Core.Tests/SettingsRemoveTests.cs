using HyperTizen.Desktop;

namespace HyperTizen.Core.Tests;

public class SettingsRemoveTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "HyperTizenTests-" + Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(_directory, "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    [Fact]
    public void A_removed_value_stays_removed_after_a_reload()
    {
        var store = new JsonFileSettingsStore(FilePath);
        store.Set("rpcServer", "ws://10.0.0.5:8090");
        store.Set("enabled", "true");

        store.Remove("rpcServer");

        var reloaded = new JsonFileSettingsStore(FilePath);
        Assert.False(reloaded.Contains("rpcServer"));
        Assert.Equal("true", reloaded.Get("enabled"));
    }

    [Fact]
    public void Removing_a_missing_key_does_nothing()
    {
        var store = new JsonFileSettingsStore(FilePath);

        store.Remove("rpcServer");

        Assert.False(store.Contains("rpcServer"));
    }
}
