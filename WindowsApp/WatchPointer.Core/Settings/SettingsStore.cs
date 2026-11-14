using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace WatchPointer.Core.Settings;

public interface ISettingsStore
{
    AppSettings Current { get; }
    event Action<AppSettings>? Changed;
    void Update(Func<AppSettings, AppSettings> change);
    void Save();
}

/// <summary>Settings in a JSON file (default %APPDATA%\WatchPointer\settings.json). Writes are atomic (temp file + move)
/// and debounced; a corrupt file is kept as settings.json.bad and defaults are used.</summary>
public sealed class JsonSettingsStore : ISettingsStore, IDisposable
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly string _path;
    private readonly ILogger _log;
    private readonly object _gate = new();
    private readonly Timer _saveTimer;
    private AppSettings _current;

    public JsonSettingsStore(string path, ILogger<JsonSettingsStore>? log = null)
    {
        _path = path;
        _log = (ILogger?)log ?? NullLogger.Instance;
        _current = Load();
        _saveTimer = new Timer(_ => Save(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WatchPointer", "settings.json");

    public AppSettings Current { get { lock (_gate) return _current; } }

    public event Action<AppSettings>? Changed;

    public void Update(Func<AppSettings, AppSettings> change)
    {
        AppSettings updated;
        lock (_gate)
        {
            updated = change(_current).Normalize();
            _current = updated;
        }
        _saveTimer.Change(500, Timeout.Infinite);
        Changed?.Invoke(updated);
    }

    public void Save()
    {
        AppSettings snapshot;
        lock (_gate) snapshot = _current;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(snapshot, JsonOptions));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.LogError(ex, "Could not save settings to {Path}", _path);
        }
    }

    private AppSettings Load()
    {
        if (!File.Exists(_path)) return new AppSettings().Normalize();
        try
        {
            var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), JsonOptions);
            return (loaded ?? new AppSettings()).Normalize();
        }
        catch (Exception ex) when (ex is JsonException or IOException or NotSupportedException)
        {
            _log.LogWarning(ex, "Settings file {Path} is unreadable; using defaults and keeping a copy as .bad", _path);
            try { File.Copy(_path, _path + ".bad", overwrite: true); } catch (IOException) { }
            return new AppSettings().Normalize();
        }
    }

    public void Dispose()
    {
        _saveTimer.Dispose();
        Save();
    }
}

/// <summary>In-memory store for tests and the console tool.</summary>
public sealed class InMemorySettingsStore(AppSettings? initial = null) : ISettingsStore
{
    private AppSettings _current = (initial ?? new AppSettings()).Normalize();
    public AppSettings Current => _current;
    public event Action<AppSettings>? Changed;
    public void Update(Func<AppSettings, AppSettings> change) { _current = change(_current).Normalize(); Changed?.Invoke(_current); }
    public void Save() { }
}
