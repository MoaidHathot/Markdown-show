using System.Diagnostics;
using Mdv.Core;
using SkiaSharp;
using Svg.Skia;

namespace Mdv.Diagrams;

/// <summary>
/// Renders D2 diagrams by invoking the <c>d2</c> binary to produce SVG, then rasterizing the
/// SVG to PNG with SkiaSharp/Svg.Skia entirely in-process (no headless browser required).
/// </summary>
internal sealed class D2Renderer
{
    private readonly string _d2Path;

    public D2Renderer(string? d2Path = null)
    {
        _d2Path = d2Path ?? "d2";
    }

    public bool IsAvailable()
    {
        try
        {
            var psi = new ProcessStartInfo(_d2Path, "--version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            using var proc = Process.Start(psi);
            if (proc is null) return false;
            proc.WaitForExit(5000);
            return proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public async Task<DiagramResult> RenderAsync(DiagramRequest request, DiagramTheme theme, CancellationToken ct)
    {
        try
        {
            var svg = await RunD2Async(request.Source, theme, ct);
            if (string.IsNullOrWhiteSpace(svg))
                return DiagramResult.Fail(request.Key, "d2 produced no output");

            var png = Rasterizer.SvgToPng(svg, out var width, out var height);
            return new DiagramResult(request.Key, DiagramStatus.Ready, png, svg, width, height, null);
        }
        catch (Exception ex)
        {
            return DiagramResult.Fail(request.Key, "D2 render failed: " + ex.Message);
        }
    }

    private async Task<string> RunD2Async(string source, DiagramTheme theme, CancellationToken ct)
    {
        // d2 theme ids: 0 = neutral default (light), 200 = dark mauve.
        var themeId = theme == DiagramTheme.Dark ? "200" : "0";
        var psi = new ProcessStartInfo(_d2Path)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add($"--theme={themeId}");
        psi.ArgumentList.Add("-");   // read from stdin
        psi.ArgumentList.Add("-");   // write to stdout

        using var proc = new Process { StartInfo = psi };
        proc.Start();

        await proc.StandardInput.WriteAsync(source.AsMemory(), ct);
        proc.StandardInput.Close();

        var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);

        var stdout = await stdoutTask;
        if (proc.ExitCode != 0)
        {
            var err = await stderrTask;
            throw new InvalidOperationException(err.Trim());
        }
        return stdout;
    }
}

internal static class Rasterizer
{
    public static byte[] SvgToPng(string svg, out int width, out int height, float scale = 1.5f)
    {
        using var skSvg = new SKSvg();
        skSvg.FromSvg(svg);
        var picture = skSvg.Picture
            ?? throw new InvalidOperationException("SVG could not be parsed for rasterization");

        var rect = picture.CullRect;
        width = (int)Math.Ceiling(rect.Width * scale);
        height = (int)Math.Ceiling(rect.Height * scale);
        if (width <= 0 || height <= 0) throw new InvalidOperationException("SVG had zero size");

        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.Scale(scale);
            canvas.DrawPicture(picture);
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
