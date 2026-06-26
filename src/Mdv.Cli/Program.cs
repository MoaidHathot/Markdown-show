using System.CommandLine;
using System.Diagnostics;
using Mdv.Core;
using Mdv.Diagrams;
using Mdv.Terminal;
using Mdv.Web;

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
var backgroundOption = new Option<string>("--background", ["--bg"])
{
    Description = "Background fill: 'solid' paints a solid themed background (overrides terminal transparency); 'terminal' lets the terminal background show through.",
    DefaultValueFactory = _ => "terminal",
};
var d2PathOption = new Option<string?>("--d2-path")
{
    Description = "Explicit path to the d2 executable (defaults to 'd2' on PATH).",
};

var root = new RootCommand("mdv — a terminal-first Markdown viewer with live reload, search, [[_TOC_]], mermaid and D2 diagrams, and an optional browser mode.")
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

root.SetAction(async (parse, ct) =>
{
    var file = parse.GetValue(fileArgument)!;
    var full = Path.GetFullPath(file);
    if (!File.Exists(full))
    {
        Console.Error.WriteLine($"mdv: file not found: {file}");
        return 1;
    }

    var browser = parse.GetValue(browserOption);
    var port = parse.GetValue(portOption);
    var noOpen = parse.GetValue(noOpenOption);
    var bestEffort = parse.GetValue(bestEffortOption);
    var themeStr = parse.GetValue(themeOption) ?? "auto";
    var backgroundStr = parse.GetValue(backgroundOption) ?? "terminal";
    var d2Path = parse.GetValue(d2PathOption);

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
    _ => true, // auto: terminals are usually dark; browser respects this too
};

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
    Console.WriteLine($"mdv: serving {Path.GetFileName(file)} at {url}");
    Console.WriteLine("mdv: watching for changes — edit the file to live-reload. Press Ctrl+C to stop.");

    if (!noOpen) OpenBrowser(url);

    var tcs = new TaskCompletionSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; tcs.TrySetResult(); };
    using var reg = ct.Register(() => tcs.TrySetResult());
    await tcs.Task;
    return 0;
}

static async Task<int> RunTerminalAsync(string file, bool dark, DiagramTheme theme, IDiagramRenderer diagrams, int port, bool solidBackground, CancellationToken ct)
{
    await using var viewer = new TerminalViewer(new TerminalViewerOptions
    {
        FilePath = file,
        DarkTerminal = dark,
        DiagramTheme = theme,
        SolidBackground = solidBackground,
        OpenInBrowser = async path =>
        {
            // Spin up a one-off browser server for the requested file and open it.
            var server = new WebViewerServer(new WebViewerOptions { FilePath = path, Port = 0, Theme = theme }, diagrams);
            await server.StartAsync();
            OpenBrowser(server.Url);
        },
    }, diagrams);

    await viewer.RunAsync();
    return 0;
}

static void OpenBrowser(string url)
{
    try
    {
        Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
    }
    catch
    {
        Console.WriteLine($"mdv: open {url} in your browser.");
    }
}
