using System.Collections.Concurrent;
using Readmd.Core;

namespace Readmd.Diagrams;

public sealed class DiagramRendererOptions
{
    /// <summary>When true, mermaid is NOT rendered via headless browser (skips the Chromium download).</summary>
    public bool BestEffort { get; init; }

    /// <summary>Optional explicit path to the <c>d2</c> binary; defaults to "d2" on PATH.</summary>
    public string? D2Path { get; init; }

    /// <summary>
    /// Optional path to a local <c>mmdc</c> (mermaid-cli). When set (or when "mmdc" is found on
    /// PATH), mermaid is rendered via mmdc instead of the bundled Playwright/Chromium, avoiding the
    /// one-time browser download. A null value still lets readmd auto-detect "mmdc" on PATH.
    /// </summary>
    public string? MermaidCliPath { get; init; }

    /// <summary>Directory for the on-disk diagram cache.</summary>
    public string CacheDirectory { get; init; } =
        Path.Combine(Path.GetTempPath(), "readmd", "diagram-cache");
}

/// <summary>
/// The composite diagram renderer used by both front-ends. Renders D2 in-process (SVG→PNG) and
/// mermaid via a local <c>mmdc</c> when available, otherwise headless Chromium. It caches every
/// result by content hash and coalesces duplicate concurrent requests so a diagram that appears
/// twice is only rendered once.
/// </summary>
public sealed class DiagramRenderer : IDiagramRenderer
{
    private readonly DiagramCache _cache;
    private readonly D2Renderer _d2;
    private readonly MermaidRenderer? _mermaid;
    private readonly MermaidCliRenderer? _mmdc;
    private readonly ConcurrentDictionary<string, Task<DiagramResult>> _inflight = new();
    // Cap concurrent renders so a document with many diagrams can't spawn an unbounded number of
    // Chromium pages / d2 processes at once (memory spike protection).
    private readonly SemaphoreSlim _renderGate = new(Math.Max(2, Environment.ProcessorCount / 2));

    public DiagramRenderer(DiagramRendererOptions? options = null)
    {
        options ??= new DiagramRendererOptions();
        _cache = new DiagramCache(options.CacheDirectory);
        _cache.EvictOldEntries();   // best-effort cleanup of orphaned/old cache files at startup
        _d2 = new D2Renderer(options.D2Path);

        // Prefer a local mmdc (no Chromium download). Use it if a path is configured or "mmdc" is
        // on PATH. Fall back to the bundled Playwright renderer unless we're in --best-effort mode.
        var mmdc = new MermaidCliRenderer(options.MermaidCliPath);
        if (mmdc.IsAvailable())
            _mmdc = mmdc;
        else if (!options.BestEffort)
            _mermaid = new MermaidRenderer();
    }

    public DiagramResult? TryGet(string key)
    {
        // Probe both themes; callers that care about theme use RenderAsync.
        return _cache.TryGet(key, DiagramTheme.Dark) ?? _cache.TryGet(key, DiagramTheme.Light);
    }

    public Task<DiagramResult> RenderAsync(DiagramRequest request, DiagramTheme theme, CancellationToken ct = default)
    {
        var cached = _cache.TryGet(request.Key, theme);
        if (cached is { Status: DiagramStatus.Ready }) return Task.FromResult(cached);

        var composedKey = request.Key + "-" + theme;
        return _inflight.GetOrAdd(composedKey, _ => RenderUncachedAsync(request, theme, composedKey, ct));
    }

    private async Task<DiagramResult> RenderUncachedAsync(
        DiagramRequest request, DiagramTheme theme, string composedKey, CancellationToken ct)
    {
        await _renderGate.WaitAsync(ct);
        try
        {
            DiagramResult result = request.Kind switch
            {
                DiagramKind.D2 => await _d2.RenderAsync(request, theme, ct),
                DiagramKind.Mermaid => await RenderMermaidAsync(request, theme, ct),
                _ => DiagramResult.Fail(request.Key, "Unsupported diagram kind"),
            };
            return _cache.Store(request.Key, theme, result);
        }
        finally
        {
            _renderGate.Release();
            _inflight.TryRemove(composedKey, out _);
        }
    }

    private async Task<DiagramResult> RenderMermaidAsync(DiagramRequest request, DiagramTheme theme, CancellationToken ct)
    {
        // Prefer a local mmdc (lighter, no Chromium); fall back to the Playwright renderer.
        if (_mmdc is not null)
            return await _mmdc.RenderAsync(request, theme, ct);
        if (_mermaid is not null)
            return await _mermaid.RenderAsync(request, theme, ct);

        return DiagramResult.Fail(request.Key,
            "Mermaid rendering is disabled in --best-effort mode (and no local 'mmdc' was found). " +
            "Install mermaid-cli (npm i -g @mermaid-js/mermaid-cli) or open in the browser to view this diagram.");
    }

    public async ValueTask DisposeAsync()
    {
        if (_mermaid is not null) await _mermaid.DisposeAsync();
        _renderGate.Dispose();
    }
}
