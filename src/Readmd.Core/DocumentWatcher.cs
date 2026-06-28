namespace Readmd.Core;

/// <summary>
/// Watches a single file for changes and raises <see cref="Changed"/> after a short debounce.
/// Editors often write files in several steps (truncate + write, or atomic rename), so we
/// debounce and also re-arm the watcher if the file is briefly replaced.
/// </summary>
public sealed class DocumentWatcher : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly System.Timers.Timer _debounce;
    private string _path;

    public event Action<string>? Changed;

    public DocumentWatcher(string path, int debounceMs = 120)
    {
        _path = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(_path)!;

        _watcher = new FileSystemWatcher(dir)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            IncludeSubdirectories = false,
            EnableRaisingEvents = true,
        };
        _watcher.Changed += OnFsEvent;
        _watcher.Created += OnFsEvent;
        _watcher.Renamed += OnFsEvent;

        _debounce = new System.Timers.Timer(debounceMs) { AutoReset = false };
        _debounce.Elapsed += (_, _) => Changed?.Invoke(_path);
    }

    /// <summary>Switches the watched file (used for multi-file wiki navigation).</summary>
    public void Retarget(string path)
    {
        _path = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(_path)!;
        if (!string.Equals(_watcher.Path, dir, StringComparison.OrdinalIgnoreCase))
        {
            _watcher.Path = dir;
        }
    }

    private void OnFsEvent(object sender, FileSystemEventArgs e)
    {
        if (!string.Equals(Path.GetFullPath(e.FullPath), _path, StringComparison.OrdinalIgnoreCase))
            return;
        _debounce.Stop();
        _debounce.Start();
    }

    /// <summary>Reads the watched file, tolerating transient locks from the writing editor.</summary>
    public static async Task<string> ReadWithRetryAsync(string path, CancellationToken ct = default)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(fs);
                return await reader.ReadToEndAsync(ct);
            }
            catch (IOException) when (attempt < 10)
            {
                await Task.Delay(25, ct);
            }
        }
    }

    public void Dispose()
    {
        _watcher.Dispose();
        _debounce.Dispose();
    }
}
