using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Readmd.Core;
using Readmd.Diagrams;

namespace Readmd.Web;

public sealed class WebViewerOptions
{
    public required string FilePath { get; init; }
    public int Port { get; init; }
    public DiagramTheme Theme { get; init; } = DiagramTheme.Dark;

    /// <summary>Sandbox root for local links/images. Defaults to the file's directory.</summary>
    public string? Root { get; init; }
}

/// <summary>
/// Hosts the browser viewer: renders markdown to HTML, serves the SPA shell and assets,
/// renders D2 diagrams server-side, streams live-reload events over SSE, and supports
/// multi-file wiki navigation sandboxed to the document root.
/// </summary>
public sealed class WebViewerServer : IAsyncDisposable
{
    private readonly WebViewerOptions _options;
    private readonly IDiagramRenderer _diagrams;
    private readonly MarkdownRenderer _markdown = new();
    private readonly LinkResolver _resolver;
    private readonly DocumentWatcher _watcher;
    private readonly ConcurrentDictionary<Guid, Channel> _clients = new();
    private WebApplication? _app;
    private string _currentPath;

    public WebViewerServer(WebViewerOptions options, IDiagramRenderer diagrams)
    {
        _options = options;
        _diagrams = diagrams;
        _currentPath = Path.GetFullPath(options.FilePath);
        var root = options.Root is not null ? Path.GetFullPath(options.Root) : Path.GetDirectoryName(_currentPath)!;
        _resolver = new LinkResolver(root);
        _watcher = new DocumentWatcher(_currentPath);
        _watcher.Changed += OnFileChanged;
    }

    public string Url { get; private set; } = "";

    public async Task StartAsync(CancellationToken ct = default)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(k =>
            k.Listen(System.Net.IPAddress.Loopback, _options.Port));
        var app = builder.Build();

        MapEndpoints(app);

        await app.StartAsync(ct);
        _app = app;

        var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
        var address = addresses?.Addresses.FirstOrDefault() ?? $"http://127.0.0.1:{_options.Port}";
        Url = address.Replace("127.0.0.1", "localhost");
    }

    private void MapEndpoints(WebApplication app)
    {
        app.MapGet("/", async (HttpContext ctx) =>
        {
            var path = ctx.Request.Query["path"].FirstOrDefault();
            var target = ResolveRequestedPath(path);
            if (target is null) { ctx.Response.StatusCode = 404; return; }
            _currentPath = target;
            var doc = await RenderDocumentAsync(target, ctx.RequestAborted);
            var shell = BuildShell(doc, target);
            ctx.Response.ContentType = "text/html; charset=utf-8";
            await ctx.Response.WriteAsync(shell, ctx.RequestAborted);
        });

        app.MapGet("/_readmd/doc", async (HttpContext ctx) =>
        {
            var path = ctx.Request.Query["path"].FirstOrDefault();
            var target = ResolveRequestedPath(path) ?? _currentPath;
            _currentPath = target;
            var doc = await RenderDocumentAsync(target, ctx.RequestAborted);
            ctx.Response.ContentType = "application/json; charset=utf-8";
            await ctx.Response.WriteAsync(JsonSerializer.Serialize(new { title = doc.Title, html = doc.Html }), ctx.RequestAborted);
        });

        app.MapGet("/_readmd/diagram/{key}", async (HttpContext ctx, string key) =>
        {
            var themeStr = ctx.Request.Query["theme"].FirstOrDefault();
            var theme = string.Equals(themeStr, "light", StringComparison.OrdinalIgnoreCase) ? DiagramTheme.Light : DiagramTheme.Dark;
            // Re-extract the current document's diagrams to find the source for this key.
            var doc = await RenderDocumentAsync(_currentPath, ctx.RequestAborted);
            var req = doc.Diagrams.FirstOrDefault(d => d.Key == key);
            if (req is null) { ctx.Response.StatusCode = 404; return; }
            var result = await _diagrams.RenderAsync(req, theme, ctx.RequestAborted);
            if (result.Status != DiagramStatus.Ready)
            {
                ctx.Response.StatusCode = 500;
                await ctx.Response.WriteAsync(result.Error ?? "render failed");
                return;
            }
            var format = ctx.Request.Query["format"].FirstOrDefault() ?? "svg";
            if (format == "svg" && result.Svg is not null)
            {
                ctx.Response.ContentType = "image/svg+xml";
                await ctx.Response.WriteAsync(result.Svg, ctx.RequestAborted);
            }
            else if (result.Png is not null)
            {
                ctx.Response.ContentType = "image/png";
                await ctx.Response.Body.WriteAsync(result.Png, ctx.RequestAborted);
            }
            else { ctx.Response.StatusCode = 404; }
        });

        app.MapGet("/_readmd/file", async (HttpContext ctx) =>
        {
            var path = ctx.Request.Query["path"].FirstOrDefault();
            if (string.IsNullOrEmpty(path)) { ctx.Response.StatusCode = 400; return; }
            var full = Path.GetFullPath(path);
            if (!_resolver.IsInsideRoot(full) || !File.Exists(full)) { ctx.Response.StatusCode = 404; return; }
            ctx.Response.ContentType = WebAssets.ContentType(full);
            await ctx.Response.SendFileAsync(full, ctx.RequestAborted);
        });

        app.MapGet("/_readmd/events", async (HttpContext ctx) =>
        {
            ctx.Response.Headers.CacheControl = "no-cache";
            ctx.Response.Headers.ContentType = "text/event-stream";
            ctx.Response.Headers["X-Accel-Buffering"] = "no";
            var channel = new Channel();
            var id = Guid.NewGuid();
            _clients[id] = channel;
            try
            {
                await ctx.Response.WriteAsync(": connected\n\n", ctx.RequestAborted);
                await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
                while (!ctx.RequestAborted.IsCancellationRequested)
                {
                    var msg = await channel.Reader.ReadAsync(ctx.RequestAborted);
                    await ctx.Response.WriteAsync(msg, ctx.RequestAborted);
                    await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
                }
            }
            catch (OperationCanceledException) { /* client disconnected */ }
            finally { _clients.TryRemove(id, out _); }
        });

        // Static assets: /_readmd/app.js, /_readmd/app.css, /_readmd/vendor/*, /_readmd/vendor/fonts/*
        app.MapGet("/_readmd/{**rest}", async (HttpContext ctx, string rest) =>
        {
            var bytes = WebAssets.TryReadBytes(rest);
            if (bytes is null) { ctx.Response.StatusCode = 404; return; }
            ctx.Response.ContentType = WebAssets.ContentType(rest);
            ctx.Response.Headers.CacheControl = "public, max-age=3600";
            await ctx.Response.Body.WriteAsync(bytes, ctx.RequestAborted);
        });
    }

    private string? ResolveRequestedPath(string? requested)
    {
        if (string.IsNullOrEmpty(requested)) return _currentPath;
        var full = Path.GetFullPath(requested);
        if (!_resolver.IsInsideRoot(full) || !File.Exists(full)) return null;
        return full;
    }

    private async Task<MarkdownDocument> RenderDocumentAsync(string path, CancellationToken ct)
    {
        var markdown = await DocumentWatcher.ReadWithRetryAsync(path, ct);
        var doc = _markdown.Parse(path, markdown);
        var rewritten = HtmlLinkRewriter.Rewrite(doc.Html, path, _resolver);
        return doc with { Html = rewritten };
    }

    private string BuildShell(MarkdownDocument doc, string path)
    {
        var template = WebAssets.ReadText("shell.html");
        var themeName = _options.Theme == DiagramTheme.Dark ? "dark" : "light";
        return template
            .Replace("__THEME__", themeName)
            .Replace("__TITLE__", System.Net.WebUtility.HtmlEncode(doc.Title))
            .Replace("__MERMAID_THEMES__", MermaidTheme.ThemeVariablesByMode())
            .Replace("__BODY__", doc.Html);
    }

    private void OnFileChanged(string path)
    {
        var json = JsonSerializer.Serialize(new { path });
        var payload = $"event: reload\ndata: {json}\n\n";
        foreach (var client in _clients.Values)
        {
            client.Writer.TryWrite(payload);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _watcher.Dispose();
        if (_app is not null) await _app.DisposeAsync();
    }

    /// <summary>A simple unbounded single-consumer message channel for one SSE client.</summary>
    private sealed class Channel
    {
        private readonly System.Threading.Channels.Channel<string> _channel =
            System.Threading.Channels.Channel.CreateUnbounded<string>();
        public System.Threading.Channels.ChannelReader<string> Reader => _channel.Reader;
        public System.Threading.Channels.ChannelWriter<string> Writer => _channel.Writer;
    }
}
