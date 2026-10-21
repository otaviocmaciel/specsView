using System.Text.Json;
using SpecsView.Core.Diagnostics;

namespace SpecsView.Core.Config;

/// <summary>
/// Loads and saves <c>config.json</c> and watches it for manual edits. <see cref="Changed"/> is
/// raised on a thread-pool thread.
/// </summary>
public sealed class ConfigService : IDisposable
{
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(300);

    private readonly Lock _gate = new();
    private FileSystemWatcher? _watcher;
    private Timer? _debounceTimer;
    private string? _lastText;

    public ConfigService(string? directory = null)
    {
        Directory = directory ?? DefaultDirectory;
        FilePath = Path.Combine(Directory, "config.json");
    }

    public static string DefaultDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SpecsView");

    public event Action<AppConfig>? Changed;

    public string Directory { get; }

    public string FilePath { get; }

    public AppConfig Current { get; private set; } = new AppConfig().Normalize();

    /// <summary>Reads the config file, creating it with defaults if it does not exist.</summary>
    public AppConfig Load()
    {
        lock (_gate)
        {
            if (!File.Exists(FilePath))
            {
                SaveCore(new AppConfig());
                return Current;
            }

            var text = ReadWithRetry();
            _lastText = text;
            Current = text is null ? Current : Parse(text) ?? Current;
            return Current;
        }
    }

    public void Save(AppConfig config)
    {
        lock (_gate)
        {
            SaveCore(config);
        }
    }

    public void StartWatching()
    {
        if (_watcher is not null)
        {
            return;
        }

        _debounceTimer = new Timer(_ => ReloadFromDisk());
        _watcher = new FileSystemWatcher(Directory, Path.GetFileName(FilePath))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
        };
        _watcher.Changed += OnFileEvent;
        _watcher.Created += OnFileEvent;
        _watcher.Renamed += OnFileEvent;
        _watcher.EnableRaisingEvents = true;
    }

    /// <summary>Parses and normalizes a config; returns null (and logs) when the JSON is invalid.</summary>
    internal static AppConfig? Parse(string json)
    {
        try
        {
            return JsonSerializer.Deserialize(json, ConfigJsonContext.Readable.AppConfig)?.Normalize();
        }
        catch (JsonException ex)
        {
            Log.Warn("Invalid config.json; keeping the previous configuration.", ex);
            return null;
        }
    }

    internal static string Serialize(AppConfig config) =>
        JsonSerializer.Serialize(config, ConfigJsonContext.Readable.AppConfig);

    private void SaveCore(AppConfig config)
    {
        var normalized = config.Normalize();
        var text = Serialize(normalized);
        System.IO.Directory.CreateDirectory(Directory);

        // Write to a temp file and swap so a crash never leaves a half-written config behind.
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, text);
        File.Move(temp, FilePath, overwrite: true);

        _lastText = text;
        Current = normalized;
    }

    private void OnFileEvent(object sender, FileSystemEventArgs e) =>
        _debounceTimer?.Change(Debounce, Timeout.InfiniteTimeSpan);

    private void ReloadFromDisk()
    {
        AppConfig? updated;
        lock (_gate)
        {
            var text = ReadWithRetry();
            if (text is null || text == _lastText)
            {
                return; // Our own save, or nothing changed.
            }

            _lastText = text;
            updated = Parse(text);
            if (updated is null)
            {
                return;
            }

            Current = updated;
        }

        Log.Info("config.json changed on disk; reloading.");
        Changed?.Invoke(updated);
    }

    private string? ReadWithRetry()
    {
        // Editors often hold the file briefly while saving.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                return File.ReadAllText(FilePath);
            }
            catch (IOException) when (attempt < 4)
            {
                Thread.Sleep(50);
            }
            catch (IOException ex)
            {
                Log.Warn("Could not read config.json.", ex);
            }
        }

        return null;
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _debounceTimer?.Dispose();
    }
}
