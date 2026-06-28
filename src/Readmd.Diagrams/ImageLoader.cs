using System.Collections.Concurrent;
using Readmd.Core;
using SkiaSharp;
using Svg.Skia;

namespace Readmd.Diagrams;

/// <summary>
/// Loads images referenced by markdown (local files or remote URLs) and decodes them to PNG bytes
/// plus pixel dimensions, so the terminal can draw them inline via Sixel — exactly like diagrams.
/// PNG/JPG/GIF/WebP are decoded with SkiaSharp; SVG is rasterized with Svg.Skia. Results are cached.
/// </summary>
public sealed class ImageLoader : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, DiagramResult> _cache = new();
    private readonly ConcurrentDictionary<string, Task<DiagramResult>> _inflight = new();
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly string _rootDirectory;

    public ImageLoader(string rootDirectory)
    {
        _rootDirectory = Path.GetFullPath(rootDirectory);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("readmd/0.1");
    }

    public static string KeyFor(string url) => "img-" + Hashing.Sha256Hex(url)[..16];

    public DiagramResult? TryGet(string key) => _cache.TryGetValue(key, out var r) ? r : null;

    /// <summary>Loads (or returns cached) an image for the given markdown URL relative to a doc path.</summary>
    public Task<DiagramResult> LoadAsync(string url, string currentFileAbsolutePath, CancellationToken ct = default)
    {
        var key = KeyFor(url);
        if (_cache.TryGetValue(key, out var cached) && cached.Status == DiagramStatus.Ready)
            return Task.FromResult(cached);
        return _inflight.GetOrAdd(key, _ => LoadUncachedAsync(key, url, currentFileAbsolutePath, ct));
    }

    /// <summary>
    /// Loads several images and composes them into one horizontal strip (badges side-by-side),
    /// stored under <paramref name="groupKey"/>. Heights are normalised to the tallest image.
    /// </summary>
    public Task<DiagramResult> LoadGroupAsync(string groupKey, IReadOnlyList<string> urls, string currentFile, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(groupKey, out var cached) && cached.Status == DiagramStatus.Ready)
            return Task.FromResult(cached);
        return _inflight.GetOrAdd(groupKey, _ => LoadGroupUncachedAsync(groupKey, urls, currentFile, ct));
    }

    private async Task<DiagramResult> LoadGroupUncachedAsync(string groupKey, IReadOnlyList<string> urls, string currentFile, CancellationToken ct)
    {
        try
        {
            var bitmaps = new List<SKBitmap>();
            foreach (var url in urls)
            {
                var r = await LoadAsync(url, currentFile, ct);
                if (r.Status == DiagramStatus.Ready && r.Png is not null)
                {
                    var bmp = SKBitmap.Decode(r.Png);
                    if (bmp is not null) bitmaps.Add(bmp);
                }
            }
            if (bitmaps.Count == 0)
                return DiagramResult.Fail(groupKey, "No images in group could be loaded");

            int gap = 12;                                   // px gap between images
            // Normalise to the SMALLEST image height. Using the tallest would blurrily upscale small
            // images (e.g. a badge next to a logo blows the badge up many times its native size).
            // Downscaling to the common minimum keeps every image crisp. Clamp so a degenerate 1px
            // image can't collapse the whole strip.
            int targetH = Math.Max(8, bitmaps.Min(b => b.Height));

            // Scale each to the common height (skip the resize when already that height to avoid an
            // identity Resize that would alias the source and complicate disposal).
            var scaled = new List<SKBitmap>(bitmaps.Count);
            var scaledOwned = new List<bool>(bitmaps.Count); // true if we own (must dispose) the scaled bitmap
            foreach (var b in bitmaps)
            {
                if (b.Height == targetH)
                {
                    scaled.Add(b);
                    scaledOwned.Add(false);
                    continue;
                }
                int w = Math.Max(1, (int)Math.Round(b.Width * (targetH / (double)b.Height)));
                var rb = b.Resize(new SKImageInfo(w, targetH), SKSamplingOptions.Default);
                if (rb is null) { scaled.Add(b); scaledOwned.Add(false); }
                else { scaled.Add(rb); scaledOwned.Add(true); }
            }
            int totalW = scaled.Sum(b => b.Width) + gap * (scaled.Count - 1);

            using var canvas2 = new SKBitmap(totalW, targetH);
            using (var c = new SKCanvas(canvas2))
            {
                c.Clear(SKColors.Transparent);
                int x = 0;
                foreach (var b in scaled)
                {
                    c.DrawBitmap(b, x, 0);
                    x += b.Width + gap;
                }
            }
            // Dispose only the resized copies we own; the originals are disposed via `bitmaps`.
            for (int i = 0; i < scaled.Count; i++)
                if (scaledOwned[i]) scaled[i].Dispose();
            foreach (var b in bitmaps) b.Dispose();

            using var img = SKImage.FromBitmap(canvas2);
            using var data = img.Encode(SKEncodedImageFormat.Png, 100);
            var result = new DiagramResult(groupKey, DiagramStatus.Ready, data.ToArray(), null, totalW, targetH, null);
            _cache[groupKey] = result;
            return result;
        }
        catch (Exception ex)
        {
            var fail = DiagramResult.Fail(groupKey, "Image group failed: " + ex.Message);
            _cache[groupKey] = fail;
            return fail;
        }
        finally
        {
            _inflight.TryRemove(groupKey, out _);
        }
    }

    private async Task<DiagramResult> LoadUncachedAsync(string key, string url, string currentFile, CancellationToken ct)
    {
        try
        {
            byte[] raw;
            bool isSvg;
            if (IsRemote(url))
            {
                using var resp = await _http.GetAsync(url, ct);
                resp.EnsureSuccessStatusCode();
                raw = await resp.Content.ReadAsByteArrayAsync(ct);
                isSvg = (resp.Content.Headers.ContentType?.MediaType?.Contains("svg") ?? false)
                        || url.EndsWith(".svg", StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                var path = ResolveLocal(url, currentFile);
                if (path is null) return DiagramResult.Fail(key, "Image not found or outside the document folder");
                raw = await File.ReadAllBytesAsync(path, ct);
                isSvg = path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase);
            }

            var (png, w, h) = isSvg ? RasterizeSvg(raw) : DecodeRaster(raw);
            var result = png is null
                ? DiagramResult.Fail(key, "Could not decode image")
                : new DiagramResult(key, DiagramStatus.Ready, png, null, w, h, null);
            _cache[key] = result;
            return result;
        }
        catch (Exception ex)
        {
            var fail = DiagramResult.Fail(key, "Image load failed: " + ex.Message);
            _cache[key] = fail;
            return fail;
        }
        finally
        {
            _inflight.TryRemove(key, out _);
        }
    }

    private static (byte[]? Png, int W, int H) DecodeRaster(byte[] raw)
    {
        using var bmp = SKBitmap.Decode(raw);
        if (bmp is null) return (null, 0, 0);
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        return (data.ToArray(), bmp.Width, bmp.Height);
    }

    private static (byte[]? Png, int W, int H) RasterizeSvg(byte[] raw)
    {
        using var svg = new SKSvg();
        using var ms = new MemoryStream(raw);
        if (svg.Load(ms) is null || svg.Picture is null) return (null, 0, 0);
        var rect = svg.Picture.CullRect;
        const float scale = 2f; // crisp on high-DPI
        int w = Math.Max(1, (int)(rect.Width * scale));
        int h = Math.Max(1, (int)(rect.Height * scale));
        using var bmp = new SKBitmap(w, h);
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.Scale(scale);
            canvas.DrawPicture(svg.Picture);
        }
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        return (data.ToArray(), w, h);
    }

    private string? ResolveLocal(string url, string currentFile)
    {
        try
        {
            var baseDir = Path.GetDirectoryName(currentFile) ?? _rootDirectory;
            var full = Path.GetFullPath(Uri.UnescapeDataString(url.Split('#')[0]), baseDir);
            // Use a trailing-separator boundary so a sibling dir can't slip past a bare prefix check.
            var rootWithSep = _rootDirectory.EndsWith(Path.DirectorySeparatorChar)
                ? _rootDirectory
                : _rootDirectory + Path.DirectorySeparatorChar;
            bool inside = string.Equals(full, _rootDirectory, StringComparison.OrdinalIgnoreCase)
                          || full.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase);
            if (!inside) return null;
            return File.Exists(full) ? full : null;
        }
        catch { return null; }
    }

    private static bool IsRemote(string url) =>
        url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    public ValueTask DisposeAsync()
    {
        _http.Dispose();
        return ValueTask.CompletedTask;
    }
}
