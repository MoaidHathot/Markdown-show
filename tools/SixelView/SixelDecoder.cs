using SkiaSharp;

namespace SixelView;

/// <summary>
/// Decodes a Sixel escape sequence back into an <see cref="SKBitmap"/>. This is the exact inverse of
/// <c>Mdv.Terminal.SixelEncoder</c>: same DCS envelope, the same <c>#i;2;r;g;b</c> palette registers
/// (RGB as 0..100 percentages), the same 6-pixel vertical bands, <c>!n</c> run-length repetition,
/// <c>$</c> carriage-return (color overlay within a band) and <c>-</c> band advance.
///
/// It is deliberately tolerant of surrounding terminal noise: <see cref="ExtractEnvelope"/> locates the
/// DCS <c>ESC P ... q</c> ... <c>ESC \\</c> payload inside a larger capture (e.g. a dump piped from a
/// real terminal session that also contains cursor moves and text).
/// </summary>
internal static class SixelDecoder
{
    /// <summary>
    /// Finds the first Sixel DCS payload inside <paramref name="text"/> and returns the inner body
    /// (the bytes between the introducer's terminating <c>q</c> and the closing String Terminator).
    /// Returns null if no Sixel sequence is present.
    /// </summary>
    public static string? ExtractEnvelope(string text)
    {
        // DCS can be introduced by ESC P (0x1B 0x50) or the single-byte C1 0x90. We support both.
        int start = -1;
        int introLen = 0;
        for (int i = 0; i < text.Length - 1; i++)
        {
            if (text[i] == '\u001b' && text[i + 1] == 'P') { start = i; introLen = 2; break; }
            if (text[i] == '\u0090') { start = i; introLen = 1; break; }
        }
        if (start < 0) return null;

        // After the introducer come optional params (digits, ';') then 'q'.
        int q = text.IndexOf('q', start + introLen);
        if (q < 0) return null;

        // Body ends at the String Terminator: ESC \\ (0x1B 0x5C) or C1 ST 0x9C.
        int end = text.Length;
        for (int i = q + 1; i < text.Length; i++)
        {
            if (text[i] == '\u001b' && i + 1 < text.Length && text[i + 1] == '\\') { end = i; break; }
            if (text[i] == '\u009c') { end = i; break; }
        }
        return text.Substring(q + 1, end - (q + 1));
    }

    /// <summary>
    /// Decodes a full Sixel string (may include the DCS envelope and surrounding noise, or be just the
    /// body). The result is an opaque RGBA bitmap. <paramref name="fallbackBackground"/> fills any cells
    /// that were never painted (Sixel leaves untouched pixels transparent on a real terminal; we make
    /// that explicit so the saved PNG shows what the terminal background would reveal).
    /// </summary>
    public static SKBitmap Decode(string sixel, SKColor fallbackBackground)
    {
        var body = ExtractEnvelope(sixel) ?? sixel;

        // --- pass 1: parse palette + measure extents ----------------------------------------
        var palette = new Dictionary<int, SKColor>();
        int width = 0, height = 0;

        ParseRasterAttributes(body, ref width, ref height);
        Measure(body, palette, ref width, ref height);

        if (width <= 0) width = 1;
        if (height <= 0) height = 1;

        // --- pass 2: paint -------------------------------------------------------------------
        var bmp = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        var painted = new bool[width * height];

        int x = 0, bandTop = 0, currentColor = -1;
        int i = 0;
        while (i < body.Length)
        {
            char ch = body[i];
            switch (ch)
            {
                case '#':
                {
                    i++;
                    int colorIndex = ReadInt(body, ref i);
                    // A palette definition is "#i;2;r;g;b"; a selector is just "#i".
                    if (i < body.Length && body[i] == ';')
                    {
                        // definition — already captured in pass 1, skip its numbers here
                        SkipColorDefinition(body, ref i);
                    }
                    currentColor = colorIndex;
                    break;
                }
                case '!':
                {
                    i++;
                    int count = ReadInt(body, ref i);
                    if (i < body.Length)
                    {
                        char sx = body[i++];
                        PaintColumn(bmp, painted, ref x, bandTop, sx, count, ColorFor(palette, currentColor), width, height);
                    }
                    break;
                }
                case '$': // carriage return: back to x=0, same band (overlay next color)
                    x = 0;
                    i++;
                    break;
                case '-': // next 6-row band
                    x = 0;
                    bandTop += 6;
                    i++;
                    break;
                default:
                    if (ch >= '?' && ch <= '~')
                    {
                        PaintColumn(bmp, painted, ref x, bandTop, ch, 1, ColorFor(palette, currentColor), width, height);
                    }
                    i++;
                    break;
            }
        }

        // Fill never-painted pixels with the fallback background (what the terminal shows through).
        for (int p = 0; p < painted.Length; p++)
        {
            if (!painted[p])
            {
                int py = p / width, px = p % width;
                bmp.SetPixel(px, py, fallbackBackground);
            }
        }
        return bmp;
    }

    // ----------------------------------------------------------------------------------------

    private static void ParseRasterAttributes(string body, ref int width, ref int height)
    {
        // Optional raster attributes immediately after 'q' in the original stream are "1;1;W;H,
        // but ExtractEnvelope hands us the post-'q' body which may start with the '"' attributes.
        int i = 0;
        while (i < body.Length && (body[i] == ' ' || body[i] == '\n' || body[i] == '\r')) i++;
        if (i < body.Length && body[i] == '"')
        {
            i++;
            ReadInt(body, ref i);                 // pan
            if (i < body.Length && body[i] == ';') i++;
            ReadInt(body, ref i);                 // pad
            if (i < body.Length && body[i] == ';') i++;
            int w = ReadInt(body, ref i);
            if (i < body.Length && body[i] == ';') i++;
            int h = ReadInt(body, ref i);
            if (w > 0) width = Math.Max(width, w);
            if (h > 0) height = Math.Max(height, h);
        }
    }

    private static void Measure(string body, Dictionary<int, SKColor> palette, ref int width, ref int height)
    {
        int x = 0, bandTop = 0, maxX = 0, maxBandBottom = 0;
        int i = 0;
        while (i < body.Length)
        {
            char ch = body[i];
            if (ch == '#')
            {
                i++;
                int idx = ReadInt(body, ref i);
                if (i < body.Length && body[i] == ';')
                {
                    i++;
                    int sys = ReadInt(body, ref i);          // color system: 2 = RGB
                    int r = 0, g = 0, b = 0;
                    if (i < body.Length && body[i] == ';') { i++; r = ReadInt(body, ref i); }
                    if (i < body.Length && body[i] == ';') { i++; g = ReadInt(body, ref i); }
                    if (i < body.Length && body[i] == ';') { i++; b = ReadInt(body, ref i); }
                    // RGB declared as 0..100 percentages (sys==2). Convert to 0..255.
                    palette[idx] = sys == 2
                        ? new SKColor(Pct(r), Pct(g), Pct(b))
                        : new SKColor((byte)r, (byte)g, (byte)b);
                }
            }
            else if (ch == '!')
            {
                i++;
                int count = ReadInt(body, ref i);
                if (i < body.Length) { i++; x += Math.Max(1, count); }   // skip the repeated char
                maxX = Math.Max(maxX, x);
            }
            else if (ch == '$') { x = 0; i++; }
            else if (ch == '-') { x = 0; maxBandBottom = bandTop + 6; bandTop += 6; i++; }
            else
            {
                if (ch >= '?' && ch <= '~') { x++; maxX = Math.Max(maxX, x); maxBandBottom = Math.Max(maxBandBottom, bandTop + 6); }
                i++;
            }
        }
        width = Math.Max(width, maxX);
        height = Math.Max(height, maxBandBottom);
    }

    private static void PaintColumn(SKBitmap bmp, bool[] painted, ref int x, int bandTop, char sixelChar,
                                    int count, SKColor color, int width, int height)
    {
        int bits = sixelChar - '?';          // 0x3F; low 6 bits = 6 vertical pixels
        for (int rep = 0; rep < Math.Max(1, count); rep++, x++)
        {
            if (x < 0 || x >= width) continue;
            for (int dy = 0; dy < 6; dy++)
            {
                if ((bits & (1 << dy)) == 0) continue;
                int y = bandTop + dy;
                if (y < 0 || y >= height) continue;
                bmp.SetPixel(x, y, color);
                painted[y * width + x] = true;
            }
        }
    }

    private static SKColor ColorFor(Dictionary<int, SKColor> palette, int index) =>
        palette.TryGetValue(index, out var c) ? c : SKColors.Black;

    private static void SkipColorDefinition(string body, ref int i)
    {
        // at the ';' after the color index: ";2;r;g;b"
        if (i < body.Length && body[i] == ';') i++;
        ReadInt(body, ref i);                                   // system
        if (i < body.Length && body[i] == ';') { i++; ReadInt(body, ref i); }
        if (i < body.Length && body[i] == ';') { i++; ReadInt(body, ref i); }
        if (i < body.Length && body[i] == ';') { i++; ReadInt(body, ref i); }
    }

    private static int ReadInt(string s, ref int i)
    {
        int start = i;
        while (i < s.Length && s[i] >= '0' && s[i] <= '9') i++;
        return i > start ? int.Parse(s.AsSpan(start, i - start)) : 0;
    }

    private static byte Pct(int pct) => (byte)Math.Clamp((int)Math.Round(pct * 255.0 / 100.0), 0, 255);
}
