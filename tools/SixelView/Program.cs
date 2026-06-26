using Mdv.Core;
using Mdv.Diagrams;
using Mdv.Terminal;
using SkiaSharp;

namespace SixelView;

/// <summary>
/// DEV-ONLY diagnostic. Turns the Sixel bytes Mdv.Terminal would emit back into a PNG so the agent
/// can visually verify rendering. NOT shipped, NOT referenced by mdv, NOT run at mdv runtime.
///
/// Modes:
///   --raw &lt;file|-&gt;                       decode a Sixel dump (incl. one captured from a real terminal)
///   --diagram &lt;file.(mmd|d2)&gt;            render a diagram, run the real on-screen pipeline, decode
///   --images "&lt;a.png,b.png,...&gt;"         compose an image group (side-by-side), decode
///   --png &lt;file.png&gt;                     round-trip an arbitrary PNG through encode-&gt;decode
///
/// Common flags:
///   --theme dark|light    (default dark)      --kind mermaid|d2   (default inferred from extension)
///   --zoom N              diagram zoom steps   --cols N            terminal width in cells (default 120)
///   --cell-w PX --cell-h PX   cell pixel size (default 10x20)
///   --crop-row A --crop-rows N  crop to a vertical row window (mirrors scroll crop)
///   --bg #rrggbb          background to flatten onto / fill (default: theme background)
///   --out &lt;file.png&gt;      output path (default tools/SixelView/out/&lt;mode&gt;-&lt;ticks&gt;.png)
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length == 0) { PrintUsage(); return 1; }

        var opt = Options.Parse(args);
        var bg = opt.Background ?? (opt.Dark ? TerminalTheme.Dark.Background : TerminalTheme.Light.Background);
        var bgColor = new SKColor(bg.R, bg.G, bg.B);

        try
        {
            return opt.Mode switch
            {
                "--raw" => DoRaw(opt, bgColor),
                "--png" => DoPng(opt, bg, bgColor),
                "--diagram" => await DoDiagramAsync(opt, bg, bgColor),
                "--images" => await DoImagesAsync(opt, bg, bgColor),
                _ => Fail($"Unknown mode '{opt.Mode}'."),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("ERROR: " + ex);
            return 1;
        }
    }

    // ---- modes -----------------------------------------------------------------------------

    private static int DoRaw(Options opt, SKColor bgColor)
    {
        if (opt.Input is null) return Fail("--raw needs a file path or '-' for stdin.");
        string text = opt.Input == "-" ? Console.In.ReadToEnd() : File.ReadAllText(opt.Input);
        if (SixelDecoder.ExtractEnvelope(text) is null)
            Console.Error.WriteLine("warning: no DCS Sixel envelope found; trying to decode whole input as a body.");
        using var bmp = SixelDecoder.Decode(text, bgColor);
        return Save(bmp, opt, "raw", sourceBytes: text.Length);
    }

    private static int DoPng(Options opt, Rgb bg, SKColor bgColor)
    {
        if (opt.Input is null) return Fail("--png needs a PNG file path.");
        using var src = SKBitmap.Decode(opt.Input) ?? throw new InvalidOperationException("could not decode PNG: " + opt.Input);
        string sixel = SixelEncoder.Encode(src, bg);
        using var bmp = SixelDecoder.Decode(sixel, bgColor);
        Console.WriteLine($"png round-trip: src {src.Width}x{src.Height} -> sixel {sixel.Length} bytes -> decoded {bmp.Width}x{bmp.Height}");
        return Save(bmp, opt, "png", sourceBytes: sixel.Length);
    }

    private static async Task<int> DoDiagramAsync(Options opt, Rgb bg, SKColor bgColor)
    {
        if (opt.Input is null) return Fail("--diagram needs a .mmd/.d2 file path.");
        string source = File.ReadAllText(opt.Input);
        var kind = opt.Kind ?? (opt.Input.EndsWith(".d2", StringComparison.OrdinalIgnoreCase) ? DiagramKind.D2 : DiagramKind.Mermaid);

        await using var renderer = new DiagramRenderer(new DiagramRendererOptions());
        var req = DiagramRequest.Create(kind, source);
        var theme = opt.Dark ? DiagramTheme.Dark : DiagramTheme.Light;
        Console.WriteLine($"rendering {kind} ({theme})...");
        var res = await renderer.RenderAsync(req, theme);
        if (res.Status != DiagramStatus.Ready || res.Png is null)
            return Fail($"diagram render failed: {res.Status} {res.Error}");
        Console.WriteLine($"diagram PNG {res.PixelWidth}x{res.PixelHeight}, {res.Png.Length} bytes");

        using var decoded = SKBitmap.Decode(res.Png);
        using var scaled = ScaleAndComposite(decoded, opt, bgColor, out int rows);
        Console.WriteLine($"on-screen scaled {scaled.Width}x{scaled.Height} ({rows} rows @ {opt.CellH}px)");

        using var final = ApplyCrop(scaled, opt);
        string sixel = SixelEncoder.Encode(final, bg);
        using var bmp = SixelDecoder.Decode(sixel, bgColor);
        Console.WriteLine($"sixel {sixel.Length} bytes -> decoded {bmp.Width}x{bmp.Height}");
        return Save(bmp, opt, $"diagram-{kind}-{(opt.Dark ? "dark" : "light")}", sourceBytes: sixel.Length);
    }

    private static async Task<int> DoImagesAsync(Options opt, Rgb bg, SKColor bgColor)
    {
        if (opt.Input is null) return Fail("--images needs a comma-separated list of image paths/URLs.");
        var urls = opt.Input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (urls.Length == 0) return Fail("--images list was empty.");

        // Root the loader at the first local image's directory (so relative paths resolve), else cwd.
        string firstLocal = urls.FirstOrDefault(u => !u.StartsWith("http", StringComparison.OrdinalIgnoreCase)) ?? ".";
        string root = Path.GetDirectoryName(Path.GetFullPath(firstLocal)) ?? Directory.GetCurrentDirectory();
        await using var loader = new ImageLoader(root);
        string currentFile = Path.Combine(root, "doc.md");

        DiagramResult res;
        if (urls.Length == 1)
        {
            Console.WriteLine($"loading single image {urls[0]}...");
            res = await loader.LoadAsync(urls[0], currentFile);
        }
        else
        {
            string groupKey = "imgrp-probe";
            Console.WriteLine($"composing image group ({urls.Length}) side-by-side...");
            res = await loader.LoadGroupAsync(groupKey, urls, currentFile);
        }
        if (res.Status != DiagramStatus.Ready || res.Png is null)
            return Fail($"image load failed: {res.Status} {res.Error}");
        Console.WriteLine($"image PNG {res.PixelWidth}x{res.PixelHeight}, {res.Png.Length} bytes");

        using var decoded = SKBitmap.Decode(res.Png);
        using var scaled = ScaleAndComposite(decoded, opt, bgColor, out int rows);
        Console.WriteLine($"on-screen scaled {scaled.Width}x{scaled.Height} ({rows} rows)");

        using var final = ApplyCrop(scaled, opt);
        string sixel = SixelEncoder.Encode(final, bg);
        using var bmp = SixelDecoder.Decode(sixel, bgColor);
        Console.WriteLine($"sixel {sixel.Length} bytes -> decoded {bmp.Width}x{bmp.Height}");
        return Save(bmp, opt, "images", sourceBytes: sixel.Length);
    }

    // ---- shared pipeline (mirrors TerminalViewer.GetScaledDiagram / BuildCroppedSixel) ------

    /// <summary>Replicates the on-screen scale: fit-to-width with zoom, snap height up to whole cell rows, center, composite onto the opaque theme background.</summary>
    private static SKBitmap ScaleAndComposite(SKBitmap src, Options opt, SKColor bgColor, out int rows)
    {
        // Mirror TerminalViewer: dark, transparent images get a light card backdrop so they stay visible.
        SKColor backdrop = bgColor;
        if (opt.IsImageMode && opt.Dark && NeedsLightCard(src))
        {
            backdrop = new SKColor(0xf6, 0xf8, 0xfa);
            Console.WriteLine("light-card: image is dark+transparent -> using light backdrop");
        }

        double zoom = Math.Pow(1.25, opt.Zoom);
        int maxWidthPx = Math.Max(opt.CellW, (opt.Cols - 2) * opt.CellW);
        int maxRows = Math.Max(8, Math.Min(28, 30));               // matches MaxDiagramRows baseline (zoom applied below)
        int maxHeightPx = Math.Max(opt.CellH, (int)(maxRows * opt.CellH * zoom));

        double fit = Math.Min(maxWidthPx / (double)src.Width, maxHeightPx / (double)src.Height);
        double scale = Math.Min(1.0, fit) * zoom;
        scale = Math.Min(scale, maxWidthPx / (double)src.Width);
        int w = Math.Max(1, (int)(src.Width * scale));
        int h = Math.Max(1, (int)(src.Height * scale));

        rows = Math.Max(1, (int)Math.Ceiling(h / (double)opt.CellH));
        int snappedH = rows * opt.CellH;

        var outBmp = new SKBitmap(w, snappedH, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(outBmp))
        {
            canvas.Clear(backdrop);
            using var resized = src.Resize(new SKImageInfo(w, h), SKSamplingOptions.Default);
            if (resized is not null)
            {
                float top = (snappedH - h) / 2f;
                canvas.DrawBitmap(resized, 0, top);
            }
        }
        return outBmp;
    }

    /// <summary>Mirrors TerminalViewer.NeedsLightCard: dark visible content + significant transparency.</summary>
    private static bool NeedsLightCard(SKBitmap bmp)
    {
        long opaque = 0, transparent = 0; double lumaSum = 0;
        int stepX = Math.Max(1, bmp.Width / 64), stepY = Math.Max(1, bmp.Height / 64);
        for (int y = 0; y < bmp.Height; y += stepY)
        for (int x = 0; x < bmp.Width; x += stepX)
        {
            var c = bmp.GetPixel(x, y);
            if (c.Alpha < 32) { transparent++; continue; }
            opaque++;
            lumaSum += (0.299*c.Red + 0.587*c.Green + 0.114*c.Blue) * (c.Alpha/255.0);
        }
        long total = opaque + transparent;
        if (total == 0 || opaque == 0) return false;
        return (transparent/(double)total) > 0.15 && (lumaSum/opaque) < 110;
    }

    /// <summary>Optional vertical crop window (mirrors the scroll crop in BuildCroppedSixel).</summary>
    private static SKBitmap ApplyCrop(SKBitmap scaled, Options opt)
    {
        if (opt.CropRow is null && opt.CropRows is null) return Copy(scaled);
        int srcY = Math.Clamp((opt.CropRow ?? 0) * opt.CellH, 0, scaled.Height);
        int srcH = Math.Clamp((opt.CropRows ?? (scaled.Height / opt.CellH)) * opt.CellH, 1, scaled.Height - srcY);
        var crop = new SKBitmap(scaled.Width, srcH, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(crop))
            canvas.DrawBitmap(scaled, new SKRect(0, srcY, scaled.Width, srcY + srcH), new SKRect(0, 0, scaled.Width, srcH));
        return crop;
    }

    private static SKBitmap Copy(SKBitmap b)
    {
        var c = new SKBitmap(b.Width, b.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(c);
        canvas.DrawBitmap(b, 0, 0);
        return c;
    }

    // ---- output ----------------------------------------------------------------------------

    private static int Save(SKBitmap bmp, Options opt, string label, int sourceBytes)
    {
        string outPath = opt.Out ?? DefaultOut(label);
        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        using (var img = SKImage.FromBitmap(bmp))
        using (var data = img.Encode(SKEncodedImageFormat.Png, 100))
        using (var fs = File.Create(outPath))
            data.SaveTo(fs);
        Console.WriteLine($"wrote {outPath}  ({bmp.Width}x{bmp.Height}, from {sourceBytes} sixel bytes)");
        return 0;
    }

    private static string DefaultOut(string label)
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "out");
        return Path.GetFullPath(Path.Combine(dir, $"{label}-{DateTime.Now:HHmmss}.png"));
    }

    private static int Fail(string msg) { Console.Error.WriteLine("ERROR: " + msg); return 1; }

    private static void PrintUsage() => Console.WriteLine(
        "SixelView (dev-only Sixel decoder)\n" +
        "  --raw <file|->                 decode a Sixel dump\n" +
        "  --diagram <file.mmd|.d2>       render + on-screen pipeline + decode\n" +
        "  --images \"a.png,b.png\"         compose group (side-by-side) + decode\n" +
        "  --png <file.png>               round-trip a PNG through encode->decode\n" +
        "  flags: --theme dark|light --kind mermaid|d2 --zoom N --cols N\n" +
        "         --cell-w PX --cell-h PX --crop-row A --crop-rows N --bg #rrggbb --out file.png");

    // ---- options ---------------------------------------------------------------------------

    private sealed class Options
    {
        public string Mode = "";
        public string? Input;
        public bool Dark = true;
        public DiagramKind? Kind;
        public int Zoom;
        public int Cols = 120;
        public int CellW = 10;
        public int CellH = 20;
        public int? CropRow;
        public int? CropRows;
        public Rgb? Background;
        public string? Out;

        /// <summary>True for the inline-image mode (where the dark-transparent light-card backdrop applies).</summary>
        public bool IsImageMode => Mode is "--images";

        public static Options Parse(string[] args)
        {
            var o = new Options { Mode = args[0] };
            // The mode's positional value (if the next token isn't a flag).
            int i = 1;
            if (i < args.Length && !args[i].StartsWith("--")) { o.Input = args[i]; i++; }
            for (; i < args.Length; i++)
            {
                string a = args[i];
                string? Next() => (i + 1 < args.Length) ? args[++i] : null;
                switch (a)
                {
                    case "--raw": case "--png": case "--diagram": case "--images":
                        o.Mode = a; if (i + 1 < args.Length && !args[i + 1].StartsWith("--")) o.Input = args[++i]; break;
                    case "--theme": o.Dark = (Next() ?? "dark").Equals("dark", StringComparison.OrdinalIgnoreCase); break;
                    case "--kind": o.Kind = (Next() ?? "mermaid").Equals("d2", StringComparison.OrdinalIgnoreCase) ? DiagramKind.D2 : DiagramKind.Mermaid; break;
                    case "--zoom": o.Zoom = int.Parse(Next() ?? "0"); break;
                    case "--cols": o.Cols = int.Parse(Next() ?? "120"); break;
                    case "--cell-w": o.CellW = int.Parse(Next() ?? "10"); break;
                    case "--cell-h": o.CellH = int.Parse(Next() ?? "20"); break;
                    case "--crop-row": o.CropRow = int.Parse(Next() ?? "0"); break;
                    case "--crop-rows": o.CropRows = int.Parse(Next() ?? "0"); break;
                    case "--bg": o.Background = Rgb.FromHex(Next() ?? "#000000"); break;
                    case "--out": o.Out = Next(); break;
                    default:
                        if (o.Input is null && !a.StartsWith("--")) o.Input = a;
                        break;
                }
            }
            return o;
        }
    }
}
