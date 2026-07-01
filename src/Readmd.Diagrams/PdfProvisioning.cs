using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Readmd.Diagrams;

/// <summary>
/// The outcome of checking/ensuring the tooling that <see cref="PdfRenderer"/> needs (a Node.js
/// runtime to drive Playwright, plus a downloaded Chromium build).
/// </summary>
public enum PdfReadiness
{
    /// <summary>Node.js and a Chromium build are both available; PDF export can run.</summary>
    Ready,

    /// <summary>The Playwright JS driver is present but no Node.js runtime was found on PATH.</summary>
    NodeMissing,

    /// <summary>Node.js is available but the Chromium browser build has not been installed yet.</summary>
    BrowserMissing,

    /// <summary>The bundled Playwright JS driver itself is missing (unexpected packaging state).</summary>
    DriverMissing,
}

/// <summary>Result of a readiness check or a provisioning attempt for PDF export.</summary>
public readonly record struct PdfProvisionResult(PdfReadiness Readiness, string? NodePath, string? Message)
{
    public bool IsReady => Readiness == PdfReadiness.Ready;
}

/// <summary>
/// Discovers and provisions the runtime PDF export depends on. The published readmd tool ships the
/// small (~12 MB) Playwright JS driver but not the ~88 MB bundled Node.js binary, so we point
/// Playwright at a Node.js already on the user's machine (via PLAYWRIGHT_NODEJS_PATH) and download
/// the Chromium build on demand. Keeping this separate from <see cref="PdfRenderer"/> lets the CLI
/// and web server query readiness and trigger installation without launching a browser.
/// </summary>
public static class PdfProvisioning
{
    /// <summary>
    /// Locates a Node.js executable to drive the Playwright JS driver: an explicit
    /// PLAYWRIGHT_NODEJS_PATH wins, otherwise 'node' on PATH. Returns null if none is found.
    /// </summary>
    public static string? FindNode()
    {
        var explicitPath = Environment.GetEnvironmentVariable("PLAYWRIGHT_NODEJS_PATH");
        if (!string.IsNullOrWhiteSpace(explicitPath) && File.Exists(explicitPath))
            return explicitPath;

        return ExecutableResolver.Find("node");
    }

    /// <summary>Path to the bundled Playwright JS driver entry point, or null if it was stripped.</summary>
    private static string? DriverCliPath()
    {
        // Playwright lays the driver out next to the app as .playwright/package/cli.js.
        var baseDir = AppContext.BaseDirectory;
        var cli = Path.Combine(baseDir, ".playwright", "package", "cli.js");
        return File.Exists(cli) ? cli : null;
    }

    /// <summary>
    /// Ensures Playwright can find a Node.js runtime by setting PLAYWRIGHT_NODEJS_PATH for this
    /// process when it isn't already set. Returns the node path used, or null if none was found.
    /// </summary>
    public static string? EnsureNodeEnvironment()
    {
        var existing = Environment.GetEnvironmentVariable("PLAYWRIGHT_NODEJS_PATH");
        if (!string.IsNullOrWhiteSpace(existing) && File.Exists(existing))
            return existing;

        var node = ExecutableResolver.Find("node");
        if (node is not null)
            Environment.SetEnvironmentVariable("PLAYWRIGHT_NODEJS_PATH", node);
        return node;
    }

    /// <summary>
    /// Checks whether PDF export can run right now, without downloading anything. Reports the
    /// specific missing piece so callers can prompt the user appropriately.
    /// </summary>
    public static PdfProvisionResult CheckReadiness()
    {
        if (DriverCliPath() is null)
            return new PdfProvisionResult(PdfReadiness.DriverMissing,
                null, "The Playwright driver is missing from this build.");

        var node = EnsureNodeEnvironment();
        if (node is null)
            return new PdfProvisionResult(PdfReadiness.NodeMissing,
                null, "Node.js was not found on PATH. Install Node.js (https://nodejs.org) to enable high-quality PDF export, or use Print to PDF.");

        return IsChromiumInstalled()
            ? new PdfProvisionResult(PdfReadiness.Ready, node, null)
            : new PdfProvisionResult(PdfReadiness.BrowserMissing, node,
                "The headless browser for PDF export is not installed yet.");
    }

    /// <summary>
    /// Ensures Chromium is installed for PDF export, downloading it on first use (~150 MB to the
    /// user cache). Returns Ready on success, or a specific reason it could not complete. Safe to
    /// call repeatedly; it is a no-op once the browser is present.
    /// </summary>
    public static PdfProvisionResult EnsureInstalled(Action<string>? log = null)
    {
        var ready = CheckReadiness();
        if (ready.Readiness is PdfReadiness.DriverMissing or PdfReadiness.NodeMissing)
            return ready;
        if (ready.IsReady) return ready;

        // BrowserMissing -> run Playwright's installer through the JS driver + system Node.
        log?.Invoke("Downloading the headless browser for PDF export (one-time, ~150 MB)…");
        try
        {
            var exit = Microsoft.Playwright.Program.Main(["install", "chromium"]);
            if (exit != 0)
                return new PdfProvisionResult(PdfReadiness.BrowserMissing, ready.NodePath,
                    "Failed to download the headless browser. Check your network connection and try again.");
        }
        catch (Exception ex)
        {
            return new PdfProvisionResult(PdfReadiness.BrowserMissing, ready.NodePath,
                "Failed to download the headless browser: " + ex.Message);
        }

        return IsChromiumInstalled()
            ? new PdfProvisionResult(PdfReadiness.Ready, ready.NodePath, null)
            : new PdfProvisionResult(PdfReadiness.BrowserMissing, ready.NodePath,
                "The headless browser did not install correctly.");
    }

    /// <summary>
    /// Detects whether a Playwright Chromium build is already present in the browser cache, so we
    /// can avoid spawning the installer on the happy path. Mirrors Playwright's cache location
    /// (PLAYWRIGHT_BROWSERS_PATH override, else the per-OS default), looking for a chromium-* build.
    /// </summary>
    private static bool IsChromiumInstalled()
    {
        foreach (var dir in BrowserCacheDirs())
        {
            if (!Directory.Exists(dir)) continue;
            try
            {
                if (Directory.EnumerateDirectories(dir, "chromium-*").Any() ||
                    Directory.EnumerateDirectories(dir, "chromium_headless_shell-*").Any())
                    return true;
            }
            catch { /* unreadable cache dir -> treat as not installed */ }
        }
        return false;
    }

    private static IEnumerable<string> BrowserCacheDirs()
    {
        var overridePath = Environment.GetEnvironmentVariable("PLAYWRIGHT_BROWSERS_PATH");
        if (!string.IsNullOrWhiteSpace(overridePath) && overridePath != "0")
        {
            yield return overridePath;
            yield break;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrEmpty(local)) yield return Path.Combine(local, "ms-playwright");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(home)) yield return Path.Combine(home, "Library", "Caches", "ms-playwright");
        }
        else
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(home)) yield return Path.Combine(home, ".cache", "ms-playwright");
        }
    }
}
