using System.CommandLine;
using System.CommandLine.Invocation;
using System.Diagnostics;
using System.Reflection;
using Readmd.Core;
using Readmd.Diagrams;
using Readmd.Terminal;
using Readmd.Web;

var fileArgument = new Argument<string>("file")
{
    Description = "Path to the Markdown file to view.",
};

var browserOption = new Option<bool>("--browser", ["-b"])
{
    Description = "Open in the browser instead of the terminal (full-fidelity mermaid/D2/math).",
};
var portOption = new Option<int>("--port", ["-p"])
{
    Description = "Port for browser mode (0 = pick a free port).",
    DefaultValueFactory = _ => 0,
};
var noOpenOption = new Option<bool>("--no-open")
{
    Description = "In browser mode, start the server but don't launch a browser.",
};
var bestEffortOption = new Option<bool>("--best-effort")
{
    Description = "Terminal mode: skip the headless-browser download; mermaid diagrams open in the browser instead.",
};
var themeOption = new Option<string>("--theme")
{
    Description = "Color theme: dark, light, or auto.",
    DefaultValueFactory = _ => "auto",
};
themeOption.AcceptOnlyFromAmong("dark", "light", "auto");
var backgroundOption = new Option<string>("--background", ["--bg"])
{
    Description = "Background fill: 'solid' paints a solid themed background (overrides terminal transparency); 'terminal' lets the terminal background show through.",
    DefaultValueFactory = _ => "terminal",
};
backgroundOption.AcceptOnlyFromAmong("solid", "terminal", "opaque", "on", "off");
var d2PathOption = new Option<string?>("--d2-path")
{
    Description = "Explicit path to the d2 executable (defaults to 'd2' on PATH).",
};

var version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
              ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
              ?? "0.0.0";

var root = new RootCommand("readmd — a terminal-first Markdown viewer with live reload, search, [[_TOC_]], mermaid and D2 diagrams, and an optional browser mode.")
{
    fileArgument,
    browserOption,
    portOption,
    noOpenOption,
    bestEffortOption,
    themeOption,
    backgroundOption,
    d2PathOption,
};

// Replace the built-in --version (which prints the bare version) with a branded one that
// prints "readmd <version>" consistently, in any argument position.
for (var i = root.Options.Count - 1; i >= 0; i--)
{
    if (root.Options[i] is VersionOption)
        root.Options.RemoveAt(i);
}
root.Options.Add(new VersionOption("--version", ["-v"]) { Action = new PrintVersionAction(version) });

root.SetAction(async (parse, ct) =>
{
    var file = parse.GetValue(fileArgument)!;
    var full = Path.GetFullPath(file);
    if (!File.Exists(full))
    {
        Console.Error.WriteLine($"readmd: file not found: {file}");
        return 2;
    }

    var browser = parse.GetValue(browserOption);
    var port = parse.GetValue(portOption);
    var noOpen = parse.GetValue(noOpenOption);
    var bestEffort = parse.GetValue(bestEffortOption);
    var themeStr = parse.GetValue(themeOption) ?? "auto";
    var backgroundStr = parse.GetValue(backgroundOption) ?? "terminal";
    var d2Path = parse.GetValue(d2PathOption);

    if (port is < 0 or > 65535)
    {
        Console.Error.WriteLine($"readmd: --port must be between 0 and 65535 (got {port}).");
        return 2;
    }
    if (browser && (bestEffort || backgroundStr is "solid" or "opaque" or "on"))
        Console.Error.WriteLine("readmd: note — --best-effort and --background only affect terminal mode and are ignored with --browser.");

    var dark = ResolveDark(themeStr);
    var solidBackground = backgroundStr.Trim().ToLowerInvariant() is "solid" or "opaque" or "on";
    var diagramTheme = dark ? DiagramTheme.Dark : DiagramTheme.Light;

    var diagrams = new DiagramRenderer(new DiagramRendererOptions
    {
        BestEffort = bestEffort,
        D2Path = d2Path,
    });

    try
    {
        if (browser)
        {
            return await RunBrowserAsync(full, port, noOpen, diagramTheme, diagrams, ct);
        }
        return await RunTerminalAsync(full, dark, diagramTheme, diagrams, port, solidBackground, ct);
    }
    catch (OperationCanceledException)
    {
        return 0;   // Ctrl+C / shutdown
    }
    catch (System.Net.Sockets.SocketException ex)
    {
        Console.Error.WriteLine($"readmd: could not start the browser server (port {port}): {ex.Message}");
        return 1;
    }
    catch (IOException ex) when (browser)
    {
        Console.Error.WriteLine($"readmd: server error: {ex.Message}");
        return 1;
    }
    catch (Exception ex)
    {
        // Last-resort handler: a concise message, not a stack trace.
        Console.Error.WriteLine($"readmd: {ex.Message}");
        return 1;
    }
    finally
    {
        await diagrams.DisposeAsync();
    }
});

return await root.Parse(args).InvokeAsync();

static bool ResolveDark(string theme) => theme.ToLowerInvariant() switch
{
    "light" => false,
    "dark" => true,
    _ => DetectDark(),   // auto
};

// Best-effort dark/light detection: honor COLORFGBG and common env hints; default to dark.
static bool DetectDark()
{
    // COLORFGBG="fg;bg" — a bg index < 8 (or the literal 0) indicates a dark background.
    var cfb = Environment.GetEnvironmentVariable("COLORFGBG");
    if (!string.IsNullOrEmpty(cfb))
    {
        var parts = cfb.Split(';');
        if (parts.Length >= 2 && int.TryParse(parts[^1], out var bg))
            return bg <= 6 || bg == 0;
    }
    // Apple Terminal/light hints are unreliable; default to dark (most terminals are dark).
    return true;
}

static async Task<int> RunBrowserAsync(string file, int port, bool noOpen, DiagramTheme theme, IDiagramRenderer diagrams, CancellationToken ct)
{
    await using var server = new WebViewerServer(new WebViewerOptions
    {
        FilePath = file,
        Port = port,
        Theme = theme,
    }, diagrams);

    await server.StartAsync(ct);
    var url = server.Url;
    Console.WriteLine($"readmd: serving {Path.GetFileName(file)} at {url}");
    Console.WriteLine("readmd: watching for changes — edit the file to live-reload. Press Ctrl+C to stop.");

    if (!noOpen) OpenBrowser(url);

    var tcs = new TaskCompletionSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; tcs.TrySetResult(); };
    using var reg = ct.Register(() => tcs.TrySetResult());
    await tcs.Task;
    return 0;
}

static async Task<int> RunTerminalAsync(string file, bool dark, DiagramTheme theme, IDiagramRenderer diagrams, int port, bool solidBackground, CancellationToken ct)
{
    // One-off browser server spun up by the viewer's 'o' action; tracked so we can dispose it.
    WebViewerServer? sideServer = null;
    await using var viewer = new TerminalViewer(new TerminalViewerOptions
    {
        FilePath = file,
        DarkTerminal = dark,
        DiagramTheme = theme,
        SolidBackground = solidBackground,
        OpenInBrowser = async path =>
        {
            // Reuse a single side server across 'o' presses instead of leaking one each time.
            sideServer ??= new WebViewerServer(new WebViewerOptions { FilePath = path, Port = 0, Theme = theme }, diagrams);
            await sideServer.StartAsync();
            OpenBrowser(sideServer.Url);
        },
    }, diagrams);

    try
    {
        await viewer.RunAsync();
        return 0;
    }
    finally
    {
        if (sideServer is not null) await sideServer.DisposeAsync();
    }
}

static void OpenBrowser(string url)
{
    try
    {
        Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
    }
    catch
    {
        Console.WriteLine($"readmd: open {url} in your browser.");
    }
}

/// <summary>Prints "readmd &lt;version&gt;" for --version, regardless of argument position.</summary>
internal sealed class PrintVersionAction(string version) : SynchronousCommandLineAction
{
    public override int Invoke(ParseResult parseResult)
    {
        parseResult.InvocationConfiguration.Output.WriteLine($"readmd {version}");
        return 0;
    }
}
