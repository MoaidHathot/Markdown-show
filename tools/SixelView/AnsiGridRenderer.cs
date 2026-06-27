using SkiaSharp;

namespace SixelView;

/// <summary>
/// Renders a stream of ANSI escapes + text (cursor positioning, truecolor SGR, basic styles) into a
/// PNG by emulating a fixed terminal grid and drawing each cell with a monospace font. Used to
/// visually verify the TUI overlays (help / TOC) the way they'd appear on screen.
/// </summary>
internal static class AnsiGridRenderer
{
    private struct Cell
    {
        public char Ch;
        public SKColor Fg;
        public SKColor Bg;
        public bool Bold;
        public bool Underline;
    }

    public static SKBitmap Render(string ansi, int cols, int rows, SKColor defaultBg, SKColor defaultFg)
    {
        var grid = new Cell[rows, cols];
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                grid[r, c] = new Cell { Ch = ' ', Fg = defaultFg, Bg = defaultBg };

        int curR = 0, curC = 0;
        SKColor fg = defaultFg, bg = defaultBg;
        bool bold = false, underline = false;

        int i = 0;
        while (i < ansi.Length)
        {
            char ch = ansi[i];
            if (ch == '\u001b' && i + 1 < ansi.Length && ansi[i + 1] == '[')
            {
                // CSI ... final-byte
                int j = i + 2;
                while (j < ansi.Length && !(ansi[j] >= '@' && ansi[j] <= '~')) j++;
                if (j >= ansi.Length) break;
                char final = ansi[j];
                string paramsStr = ansi.Substring(i + 2, j - (i + 2));
                if (final == 'H' || final == 'f')
                {
                    var parts = paramsStr.Split(';');
                    int row = parts.Length > 0 && int.TryParse(parts[0], out var rr) ? rr - 1 : 0;
                    int col = parts.Length > 1 && int.TryParse(parts[1], out var cc) ? cc - 1 : 0;
                    curR = Math.Clamp(row, 0, rows - 1);
                    curC = Math.Clamp(col, 0, cols - 1);
                }
                else if (final == 'm')
                {
                    ApplySgr(paramsStr, ref fg, ref bg, ref bold, ref underline, defaultFg, defaultBg);
                }
                // ignore other CSI (e.g. clear-line) for capture purposes
                i = j + 1;
                continue;
            }
            if (ch == '\u001b')
            {
                // Other escape (e.g. ESC \ ); skip the next char.
                i += 2;
                continue;
            }
            if (ch == '\n') { curR = Math.Min(rows - 1, curR + 1); curC = 0; i++; continue; }
            if (ch == '\r') { curC = 0; i++; continue; }

            if (curR >= 0 && curR < rows && curC >= 0 && curC < cols)
            {
                grid[curR, curC] = new Cell { Ch = ch, Fg = fg, Bg = bg, Bold = bold, Underline = underline };
            }
            curC++;
            if (curC >= cols) { curC = 0; curR = Math.Min(rows - 1, curR + 1); }
            i++;
        }

        return Rasterize(grid, rows, cols);
    }

    private static void ApplySgr(string paramsStr, ref SKColor fg, ref SKColor bg,
        ref bool bold, ref bool underline, SKColor defaultFg, SKColor defaultBg)
    {
        var p = paramsStr.Length == 0 ? new[] { "0" } : paramsStr.Split(';');
        for (int k = 0; k < p.Length; k++)
        {
            if (!int.TryParse(p[k], out int code)) continue;
            switch (code)
            {
                case 0: fg = defaultFg; bg = defaultBg; bold = false; underline = false; break;
                case 1: bold = true; break;
                case 2: break; // dim — approximate as normal
                case 3: break; // italic
                case 4: underline = true; break;
                case 7: (fg, bg) = (bg, fg); break;
                case 22: bold = false; break;
                case 24: underline = false; break;
                case 38 when k + 4 < p.Length && p[k + 1] == "2":
                    fg = new SKColor((byte)int.Parse(p[k + 2]), (byte)int.Parse(p[k + 3]), (byte)int.Parse(p[k + 4]));
                    k += 4; break;
                case 48 when k + 4 < p.Length && p[k + 1] == "2":
                    bg = new SKColor((byte)int.Parse(p[k + 2]), (byte)int.Parse(p[k + 3]), (byte)int.Parse(p[k + 4]));
                    k += 4; break;
                case 39: fg = defaultFg; break;
                case 49: bg = defaultBg; break;
            }
        }
    }

    private static SKBitmap Rasterize(Cell[,] grid, int rows, int cols)
    {
        // Cell metrics chosen to resemble a typical terminal font box.
        const int cw = 11, chh = 24;
        int W = cols * cw, H = rows * chh;
        var bmp = new SKBitmap(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Black);

        using var typeface = SKTypeface.FromFamilyName("Cascadia Mono")
            ?? SKTypeface.FromFamilyName("Consolas")
            ?? SKTypeface.FromFamilyName("Courier New")
            ?? SKTypeface.Default;
        var fontMgr = SKFontManager.Default;

        using var fill = new SKPaint { IsAntialias = false };
        using var text = new SKPaint { IsAntialias = true };

        // First pass: backgrounds.
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                fill.Color = grid[r, c].Bg;
                canvas.DrawRect(c * cw, r * chh, cw, chh, fill);
            }

        // Second pass: glyphs. Use a fallback font for any char the primary face lacks (so symbols
        // like arrows/bullets render in the preview the way a configured terminal font would).
        float baseline = chh - 6;
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                var cell = grid[r, c];
                if (cell.Ch == ' ' || cell.Ch == '\0') continue;
                text.Color = cell.Fg;
                var face = typeface;
                if (typeface.GetGlyph(cell.Ch) == 0)
                    face = fontMgr.MatchCharacter(cell.Ch) ?? typeface;
                using var f = new SKFont(face, 18) { Embolden = cell.Bold, Subpixel = true };
                canvas.DrawText(cell.Ch.ToString(), c * cw + 1, r * chh + baseline, SKTextAlign.Left, f, text);
                if (face != typeface) face.Dispose();
                if (cell.Underline)
                {
                    using var up = new SKPaint { Color = cell.Fg, StrokeWidth = 1 };
                    canvas.DrawLine(c * cw, r * chh + baseline + 2, c * cw + cw, r * chh + baseline + 2, up);
                }
            }

        return bmp;
    }
}
