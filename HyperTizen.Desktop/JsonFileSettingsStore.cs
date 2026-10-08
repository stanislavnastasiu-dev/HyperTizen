using System.Text.Json;
using HyperTizen.Core;

namespace HyperTizen.Desktop;

// Settings kept in a JSON file, standing in for the TV's preference store.
public sealed class JsonFileSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private readonly string _path;
    private readonly object _gate = new();
    private readonly Dictionary<string, string> _values;

    public JsonFileSettingsStore(string path)
    {
        _path = path;
        _values = Load(path);
    }

    public bool Contains(string key)
    {
        lock (_gate) return _values.ContainsKey(key);
    }

    public string Get(string key)
    {
        lock (_gate) return _values[key];
    }

    public void Set(string key, string value)
    {
        lock (_gate)
        {
            _values[key] = value;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            File.WriteAllText(_path, JsonSerializer.Serialize(_values, WriteOptions));
        }
    }

    private static Dictionary<string, string> Load(string path)
    {
        if (!File.Exists(path)) return new Dictionary<string, string>();

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))
                ?? new Dictionary<string, string>();
        }
        catch (JsonException)
        {
            // An unreadable settings file means starting from defaults; the next save replaces it.
            return new Dictionary<string, string>();
        }
    }
}
