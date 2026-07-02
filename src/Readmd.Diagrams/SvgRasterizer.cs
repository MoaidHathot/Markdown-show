using SkiaSharp;
using Svg.Skia;

namespace Readmd.Diagrams;

/// <summary>
/// Rasterizes an SVG string to a bitmap at a caller-chosen target size. Because SVG is vector, this
/// re-renders sharply at whatever size the viewer needs (e.g. when zooming a diagram), instead of
/// upscaling a fixed-resolution PNG and going blurry.
/// </summary>
public static class SvgRasterizer
{
    /// <summary>
    /// Renders <paramref name="svg"/> scaled to fit within <paramref name="targetWidth"/> ×
    /// <paramref name="targetHeight"/> (preserving aspect ratio). Returns null if the SVG can't be
    /// parsed or has no size. The caller owns and must dispose the returned bitmap.
    /// </summary>
    public static SKBitmap? RenderToFit(string svg, int targetWidth, int targetHeight)
    {
        if (string.IsNullOrWhiteSpace(svg) || targetWidth < 1 || targetHeight < 1) return null;
        try
        {
            using var skSvg = new SKSvg();
            skSvg.FromSvg(svg);
            var picture = skSvg.Picture;
            if (picture is null) return null;

            var rect = picture.CullRect;
            if (rect.Width <= 0 || rect.Height <= 0) return null;

            double scale = Math.Min(targetWidth / (double)rect.Width, targetHeight / (double)rect.Height);
            int w = Math.Max(1, (int)Math.Round(rect.Width * scale));
            int h = Math.Max(1, (int)Math.Round(rect.Height * scale));

            var bitmap = new SKBitmap(w, h);
            using (var canvas = new SKCanvas(bitmap))
            {
                canvas.Clear(SKColors.Transparent);
                canvas.Scale((float)scale);
                canvas.DrawPicture(picture);
            }
            return bitmap;
        }
        catch
        {
            return null;
        }
    }
}
