using System.Text;
using SkiaSharp;

namespace Readmd.Terminal;

/// <summary>
/// Renders a raster image as Unicode half-block (▀) text: each character cell stacks two vertical
/// pixels, using the upper-half glyph with the top pixel as the foreground color and the bottom
/// pixel as the background color. Works on any truecolor terminal, so it's the fallback where Sixel
/// (or another graphics protocol) isn't available.
/// </summary>
public static class HalfBlockEncoder
{
    private const char UpperHalf = '\u2580'; // ▀

    /// <summary>
    /// Encodes a bitmap to one ANSI string per <em>text row</em> (each row covers two pixel rows).
    /// Transparent pixels are flattened onto <paramref name="background"/>.
    /// </summary>
    public static IReadOnlyList<string> Encode(SKBitmap bmp, Rgb background)
    {
        int w = bmp.Width, h = bmp.Height;
        int rows = (h + 1) / 2;
        var lines = new List<string>(rows);

        for (int row = 0; row < rows; row++)
        {
            int yTop = row * 2;
            int yBot = yTop + 1;
            var sb = new StringBuilder(w * 24);

            Rgb? lastFg = null;
            Rgb? lastBg = null;
            for (int x = 0; x < w; x++)
            {
                var top = Sample(bmp, x, yTop, background);
                var bot = yBot < h ? Sample(bmp, x, yBot, background) : background;

                if (lastFg != top) { sb.Append("\e[38;2;").Append(top.R).Append(';').Append(top.G).Append(';').Append(top.B).Append('m'); lastFg = top; }
                if (lastBg != bot) { sb.Append("\e[48;2;").Append(bot.R).Append(';').Append(bot.G).Append(';').Append(bot.B).Append('m'); lastBg = bot; }
                sb.Append(UpperHalf);
            }
            sb.Append("\e[0m");
            lines.Add(sb.ToString());
        }
        return lines;
    }

    private static Rgb Sample(SKBitmap bmp, int x, int y, Rgb bg)
    {
        var c = bmp.GetPixel(x, y);
        if (c.Alpha == 0) return bg;
        if (c.Alpha == 255) return new Rgb(c.Red, c.Green, c.Blue);
        double a = c.Alpha / 255.0;
        return new Rgb(
            (byte)(c.Red * a + bg.R * (1 - a)),
            (byte)(c.Green * a + bg.G * (1 - a)),
            (byte)(c.Blue * a + bg.B * (1 - a)));
    }
}
