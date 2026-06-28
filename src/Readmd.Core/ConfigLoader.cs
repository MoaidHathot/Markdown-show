namespace Readmd.Core;

/// <summary>
/// Discovers and loads <see cref="ReadmdConfig"/> from disk: a user-level file plus an optional
/// project-level <c>.readmd.json</c> found by walking up from the document directory. Project
/// settings override user settings. Loading never throws — a malformed or missing file is ignored
/// (with the reason available via <see cref="LastError"/>).
/// </summary>
public static class ConfigLoader
{
    /// <summary>The last load/parse error, if any (for optional diagnostics). Null when all is well.</summary>
    public static string? LastError { get; private set; }

    /// <summary>
    /// Loads the effective config for a document at <paramref name="documentPath"/> (its directory
    /// is the starting point for the upward <c>.readmd.json</c> search). Pass null to load only the
    /// user-level config.
    /// </summary>
    public static ReadmdConfig Load(string? documentPath)
    {
        LastError = null;
        var config = ReadFile(UserConfigPath());

        var projectFile = documentPath is null ? null : FindProjectConfig(documentPath);
        if (projectFile is not null)
            config = config.MergedWith(ReadFile(projectFile));

        return config;
    }

    /// <summary>The user-level config path for this OS (created lazily by the user, not by readmd).</summary>
    public static string UserConfigPath()
    {
        // ~/.config/readmd/config.json on Unix; %APPDATA%\readmd\config.json on Windows.
        if (OperatingSystem.IsWindows())
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "readmd", "config.json");
        }
        var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        var baseDir = !string.IsNullOrEmpty(xdg)
            ? xdg
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        return Path.Combine(baseDir, "readmd", "config.json");
    }

    /// <summary>Walks up from the document's directory looking for a <c>.readmd.json</c> file.</summary>
    public static string? FindProjectConfig(string documentPath)
    {
        try
        {
            var dir = Directory.Exists(documentPath)
                ? new DirectoryInfo(documentPath)
                : new FileInfo(Path.GetFullPath(documentPath)).Directory;
            while (dir is not null)
            {
                var candidate = Path.Combine(dir.FullName, ".readmd.json");
                if (File.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }
        }
        catch (Exception ex) { LastError = ex.Message; }
        return null;
    }

    private static ReadmdConfig ReadFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return ReadmdConfig.Empty;
            return ReadmdConfig.Parse(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            LastError = $"{Path.GetFileName(path)}: {ex.Message}";
            return ReadmdConfig.Empty;
        }
    }
}
