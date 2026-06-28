using Readmd.Core;
using Readmd.Diagrams;
using Readmd.Terminal;
using SkiaSharp;

namespace Readmd.Tests;

public class SixelEncoderTests
{
    private static SKBitmap Solid(int w, int h, SKColor c)
    {
        var bmp = new SKBitmap(w, h);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(c);
        return bmp;
    }

    [Fact]
    public void Output_is_wrapped_in_a_sixel_dcs_sequence()
    {
        using var bmp = Solid(8, 8, SKColors.Red);
        var sixel = SixelEncoder.Encode(bmp, new Rgb(0, 0, 0));
        Assert.StartsWith("\eP", sixel);        // DCS introducer
        Assert.EndsWith("\e\\", sixel);          // ST terminator
        Assert.Contains("q", sixel);             // sixel mode 'q'
    }

    [Fact]
    public void Declares_image_dimensions()
    {
        using var bmp = Solid(12, 6, SKColors.Blue);
        var sixel = SixelEncoder.Encode(bmp, new Rgb(0, 0, 0));
        // The raster attributes encode the pixel size: "1;1;<w>;<h>".
        Assert.Contains("1;1;12;6", sixel);
    }

    [Fact]
    public void Encodes_without_throwing_for_transparent_pixels()
    {
        using var bmp = new SKBitmap(4, 4); // fully transparent
        var sixel = SixelEncoder.Encode(bmp, new Rgb(10, 20, 30));
        Assert.False(string.IsNullOrEmpty(sixel));
    }
}

public class DiagramCacheTests
{
    private static string TempDir() => Path.Combine(Path.GetTempPath(), "readmd-cache-test-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Store_then_TryGet_round_trips_from_memory_and_disk()
    {
        var dir = TempDir();
        try
        {
            var cache = new DiagramCache(dir);
            var req = DiagramRequest.Create(DiagramKind.D2, "a -> b");
            var result = new DiagramResult(req.Key, DiagramStatus.Ready, new byte[] { 9, 8, 7 }, "<svg id=\"x\"/>", 5, 5, null);
            cache.Store(req.Key, DiagramTheme.Dark, result);

            // A fresh cache (new in-memory map) must hydrate the SVG from disk.
            var cache2 = new DiagramCache(dir);
            var got = cache2.TryGet(req.Key, DiagramTheme.Dark);
            Assert.NotNull(got);
            Assert.Equal(DiagramStatus.Ready, got!.Status);
            Assert.Equal("<svg id=\"x\"/>", got.Svg);
        }
        finally { TryDelete(dir); }
    }

    [Fact]
    public void TryGet_is_theme_specific()
    {
        var dir = TempDir();
        try
        {
            var cache = new DiagramCache(dir);
            var req = DiagramRequest.Create(DiagramKind.D2, "a -> b");
            cache.Store(req.Key, DiagramTheme.Dark, new DiagramResult(req.Key, DiagramStatus.Ready, new byte[] { 1 }, "<svg/>", 1, 1, null));
            Assert.NotNull(cache.TryGet(req.Key, DiagramTheme.Dark));
            Assert.Null(cache.TryGet(req.Key, DiagramTheme.Light)); // different theme = different key
        }
        finally { TryDelete(dir); }
    }

    private static void TryDelete(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { /* ignore */ }
    }
}
