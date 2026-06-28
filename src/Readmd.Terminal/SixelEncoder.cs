using System.Text;
using SkiaSharp;

namespace Readmd.Terminal;

/// <summary>
/// Encodes a raster image as a Sixel escape sequence for display in capable terminals
/// (Windows Terminal ≥ 1.22, WezTerm, xterm, mintty, foot, etc.). Uses a fixed 256-color
/// 3-3-2 palette which is fast and adequate for diagrams.
/// </summary>
public static class SixelEncoder
{
    public static string Encode(SKBitmap bmp, Rgb? background = null)
    {
        int w = bmp.Width, h = bmp.Height;
        var sb = new StringBuilder(w * h / 2 + 4096);
        sb.Append("\eP0;1;0q");          // DCS sixel, 1:1 aspect
        sb.Append("\"1;1;").Append(w).Append(';').Append(h);

        // Register 256-color 3-3-2 palette (RGB percentages 0..100).
        for (int i = 0; i < 256; i++)
        {
            int r = (i >> 5) & 0x7, g = (i >> 2) & 0x7, b = i & 0x3;
            sb.Append('#').Append(i).Append(";2;")
              .Append(r * 100 / 7).Append(';')
              .Append(g * 100 / 7).Append(';')
              .Append(b * 100 / 3);
        }

        var bg = background ?? new Rgb(0, 0, 0);
        byte[] idx = new byte[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                var c = bmp.GetPixel(x, y);
                byte R = c.Red, G = c.Green, B = c.Blue;
                if (c.Alpha < 128) { R = bg.R; G = bg.G; B = bg.B; } // flatten transparency onto bg
                else if (c.Alpha < 255)
                {
                    double a = c.Alpha / 255.0;
                    R = (byte)(R * a + bg.R * (1 - a));
                    G = (byte)(G * a + bg.G * (1 - a));
                    B = (byte)(B * a + bg.B * (1 - a));
                }
                idx[y * w + x] = (byte)(((R >> 5) << 5) | ((G >> 5) << 2) | (B >> 6));
            }
        }

        int bands = (h + 5) / 6;
        for (int band = 0; band < bands; band++)
        {
            int y0 = band * 6;
            int yMax = Math.Min(y0 + 6, h);

            var colors = new HashSet<byte>();
            for (int y = y0; y < yMax; y++)
                for (int x = 0; x < w; x++) colors.Add(idx[y * w + x]);

            bool firstColor = true;
            foreach (var color in colors)
            {
                if (!firstColor) sb.Append('$'); // return to start of band for the next color overlay
                firstColor = false;
                sb.Append('#').Append(color);

                int runStart = 0; char runChar = '\0'; bool have = false;
                for (int x = 0; x < w; x++)
                {
                    int bits = 0;
                    for (int dy = 0; dy < 6; dy++)
                    {
                        int y = y0 + dy;
                        if (y < h && idx[y * w + x] == color) bits |= 1 << dy;
                    }
                    char ch = (char)(0x3F + bits);
                    if (!have) { runChar = ch; runStart = x; have = true; }
                    else if (ch != runChar)
                    {
                        EmitRun(sb, runChar, x - runStart);
                        runChar = ch; runStart = x;
                    }
                }
                if (have) EmitRun(sb, runChar, w - runStart);
            }
            sb.Append('-'); // graphics newline
        }

        sb.Append("\e\\"); // ST
        return sb.ToString();
    }

    private static void EmitRun(StringBuilder sb, char ch, int count)
    {
        if (count >= 4)
        {
            sb.Append('!').Append(count).Append(ch); // RLE
        }
        else
        {
            for (int i = 0; i < count; i++) sb.Append(ch);
        }
    }
}
