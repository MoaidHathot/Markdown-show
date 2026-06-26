using System.Text;
using Mdv.Core;
using SkiaSharp;

namespace Mdv.Terminal;

public sealed partial class TerminalViewer
{
    private int ViewportHeight => _screen.Height - 1; // last row is the status bar

    // ---------------- main loop ----------------
    private void RenderLoop()
    {
        // Guaranteed escape hatch: Ctrl+C always quits cleanly (the screen is restored in
        // RunAsync's finally). We keep ENABLE_PROCESSED_INPUT on so this still fires.
        ConsoleCancelEventHandler onCancel = (_, e) =>
        {
            e.Cancel = true;     // don't hard-kill; let the loop exit and restore the screen
            _running = false;
        };
        Console.CancelKeyPress += onCancel;

        // Note: we deliberately do NOT synchronously query the terminal for its pixel size here.
        // That reply (CSI 4;h;w t) ends in 't' and, if not perfectly drained, could leak in as a
        // keystroke (opening the TOC). The input reader swallows such report sequences instead, and
        // the per-cell size falls back to a sensible estimate (refined on resize).

        // Read keys on a dedicated background thread via the platform key reader (ReadConsoleInput
        // on Windows, Console.ReadKey elsewhere) and hand them to the loop through a queue. This is
        // the reliable path on Windows Terminal and can't go deaf to 'q'.
        var keyQueue = new System.Collections.Concurrent.ConcurrentQueue<KeyEvent>();
        Thread? reader = null;
        if (!Console.IsInputRedirected)
        {
            reader = new Thread(() =>
            {
                foreach (var ev in KeyReader.Read(() => _running))
                    keyQueue.Enqueue(ev);
            })
            {
                IsBackground = true,
                Name = "mdv-input",
            };
            reader.Start();
        }

        // Ask the terminal for its pixel size (CSI 14t). The reply is captured + swallowed by the
        // input reader (so it never leaks as a keystroke); we read it back a moment later.
        if (!Console.IsInputRedirected)
            _screen.WriteEscape("\e[14t").Flush();

        try
        {
            RefreshCellSize();
            int lastW = _screen.Width, lastH = _screen.Height;
            bool pixelApplied = _windowPixelWidth > 0;
            var pixelDeadline = DateTime.UtcNow.AddSeconds(2);
            while (_running)
            {
                if (_screen.Width != lastW || _screen.Height != lastH)
                {
                    lastW = _screen.Width; lastH = _screen.Height;
                    RefreshCellSize();
                    ReflowForResize();
                }

                // The CSI 14t reply arrives shortly after startup; apply it once it's available.
                if (!pixelApplied && DateTime.UtcNow < pixelDeadline)
                {
                    if (KeyReader.WindowPixelSize is not null) { RefreshCellSize(); pixelApplied = _windowPixelWidth > 0; }
                }

                if (_dirty || _forceRepaintNextFrame)
                {
                    _forceRepaintNextFrame = false;
                    Draw();
                    _dirty = false;
                }

                bool handledAny = false;
                while (keyQueue.TryDequeue(out var key))
                {
                    HandleKeyEvent(key);
                    handledAny = true;
                    if (!_running) break;
                }

                if (!handledAny)
                {
                    Thread.Sleep(16);
                    if (_statusMessage is not null && DateTime.UtcNow > _statusUntil)
                    {
                        _statusMessage = null;
                        _dirty = true;
                    }
                }
            }
        }
        finally
        {
            Console.CancelKeyPress -= onCancel;
        }
    }

    /// <summary>
    /// Recomputes the per-cell pixel size from the terminal's window pixel size divided by the
    /// current rows/cols. Because the window pixel size is constant across font zoom (Ctrl +/-),
    /// this picks up zoom automatically (the grid changes). When the cell size changes, diagram
    /// bitmaps/reservations are recomputed and the screen is hard-cleared so images don't overlap.
    /// </summary>
    private void RefreshCellSize()
    {
        // Prefer the terminal-reported pixel size (captured by the input reader from a CSI 14t
        // reply); it stays constant across font zoom, so dividing by the live grid tracks zoom.
        if (KeyReader.WindowPixelSize is { } wp)
        {
            _windowPixelWidth = wp.WidthPx;
            _windowPixelHeight = wp.HeightPx;
        }

        int cols = _screen.Width, rows = _screen.Height;
        int cw, ch;
        if (_windowPixelWidth > 0 && _windowPixelHeight > 0 && cols > 0 && rows > 0)
        {
            cw = Math.Max(1, _windowPixelWidth / cols);
            ch = Math.Max(1, _windowPixelHeight / rows);
        }
        else
        {
            cw = 10; ch = 20; // estimate (non-Windows / no reply)
        }

        if (cw == _cellWidthPx && ch == _cellHeightPx) return;

        lock (_stateLock)
        {
            _cellWidthPx = cw;
            _cellHeightPx = ch;
            ClearDiagramCaches();
            _forceHardClear = true;
            // Re-reserve rows for all ready diagrams at the new cell size.
            foreach (var (key, result) in _diagramResults)
                if (result.Status == DiagramStatus.Ready)
                    ReserveDiagramRows(key, result);
            _dirty = true;
        }
    }

    private void ReflowForResize()
    {
        lock (_stateLock)
        {
            // Width changed: scaled diagram bitmaps and their crops are no longer valid.
            ClearDiagramCaches();
            _forceHardClear = true;
            // Re-render lines at the new width.
            try
            {
                var markdown = File.ReadAllText(_currentPath);
                var parsed = ParseToLines(_currentPath, markdown);
                _lines = parsed.Lines;
                _links = parsed.Links;
                _pendingDiagrams = parsed.Diagrams;
                _pendingImages = parsed.Images;
            }
            catch { /* keep old layout */ }

            // Re-parsing produced fresh anchors with NO reservation rows; re-reserve for every
            // already-rendered diagram/image so the layout (and images) stay intact after resize.
            foreach (var (key, result) in _diagramResults)
                if (result.Status == DiagramStatus.Ready)
                    ReserveDiagramRows(key, result);
            _dirty = true;
        }
        RecomputeSearch();
    }

    // ---------------- drawing ----------------
    private bool _diagramsOnScreenLastFrame;
    private bool _overlayOpenLastFrame;
    private int _lastDrawScroll = -1;
    private volatile bool _forceHardClear;
    // Set only by an explicit refresh ('r') to perform a clean wipe: one diagram-free frame followed
    // by a repaint, guaranteeing stubborn Sixel artifacts are cleared. Not used for theme/bg/zoom
    // toggles (those would blink). See the clean-wipe logic in Draw().
    private volatile bool _refreshWipe;
    // Forces one extra repaint after a clean-wipe frame (which actually draws the diagrams) even
    // though the loop resets _dirty after Draw().
    private bool _forceRepaintNextFrame;

    private void Draw()
    {
        lock (_stateLock)
        {
            _screen.BeginFrame();

            int height = ViewportHeight;
            int width = _screen.Width;

            // Sixel graphics aren't erased by per-line text clears, so when the view scrolled and a
            // diagram is (or just was) on screen, hard-clear once to wipe leftover image pixels.
            // Also hard-clear when an overlay (help/TOC) opens or closes so diagrams don't bleed
            // through the panel, or when the cell size changed (font zoom).
            bool anyDiagramVisible = AnyDiagramVisible(height);
            bool overlayOpen = _helpMode || _tocMode;
            bool scrolled = _scroll != _lastDrawScroll;
            bool overlayChanged = overlayOpen != _overlayOpenLastFrame;
            // An explicit refresh ('r') performs a "clean wipe": render ONE diagram-free frame (text
            // + hard clear only), flush it, then repaint the images on the next frame over a
            // verified-empty buffer. This reliably clears stubborn Sixel artifacts on Windows
            // Terminal that ED (\e[2J) leaves behind. We DON'T do this on theme/background/zoom
            // toggles — there it would cause a visible one-frame blink of the diagrams/images, and a
            // normal hard clear is enough for those. The user can press 'r' if anything lingers.
            bool cleanWipe = _refreshWipe;
            _refreshWipe = false;
            bool suppressDiagramsThisFrame = false;
            if (_forceHardClear || cleanWipe || ((scrolled || overlayChanged) && (anyDiagramVisible || _diagramsOnScreenLastFrame)))
            {
                _screen.HardClear();
                _forceHardClear = false;
                if (cleanWipe)
                {
                    suppressDiagramsThisFrame = true;
                    _forceRepaintNextFrame = true;   // follow-up frame paints the diagrams over the cleared buffer
                }
            }
            _diagramsOnScreenLastFrame = anyDiagramVisible;
            _overlayOpenLastFrame = overlayOpen;
            _lastDrawScroll = _scroll;

            // Paint the whole screen with the solid background first (overrides terminal
            // transparency, like OpenCode). When solid mode is off, we just clear normally so
            // the terminal's own background shows through.
            if (_solidBackground)
                _screen.FillBackground(_theme.Background, _screen.Height, _screen.Width);
            _screen.MoveTo(0, 0);

            for (int row = 0; row < height; row++)
            {
                _screen.MoveTo(row, 0);
                if (_solidBackground) { _screen.SetBackground(_theme.Background); _screen.ClearLineToEnd(); }
                else _screen.ClearLine();
                int lineIndex = _scroll + row;
                if (lineIndex < _lines.Count)
                    DrawLine(_lines[lineIndex], lineIndex, row, width);
            }

            // Don't draw Sixel diagrams while an overlay panel is open — they'd bleed through it.
            // Also skip on the single clear-only frame after a deep clear (see above).
            if (!_helpMode && !_tocMode && !suppressDiagramsThisFrame)
                DrawDiagrams(height);
            DrawStatusBar();

            if (_tocMode) DrawTocOverlay();
            if (_helpMode) DrawHelpOverlay();

            _screen.Reset();
            _screen.Flush();
        }
    }

    private bool AnyDiagramVisible(int height)
    {
        foreach (var (key, result) in _diagramResults)
        {
            if (result.Status != DiagramStatus.Ready || result.Png is null) continue;
            int anchorIndex = FindDiagramLine(key);
            if (anchorIndex < 0) continue;
            int rowOnScreen = anchorIndex - _scroll;
            int rowsTall = DiagramRows(result);
            if (rowOnScreen < height && rowOnScreen + rowsTall > 0) return true;
        }
        return false;
    }

    private void DrawLine(DisplayLine line, int lineIndex, int row, int width)
    {
        // If the line wants a full-width background (e.g. code blocks), paint it first.
        if (line.LineBackground is { } lineBg)
        {
            _screen.MoveTo(row, 0).SetBackground(lineBg).ClearLineToEnd();
        }

        _screen.MoveTo(row, 0);
        int col = 0;
        foreach (var span in line.Spans)
        {
            if (col >= width) break;
            var text = span.Text;
            if (col + text.Length > width) text = text[..Math.Max(0, width - col)];

            // Search highlight?
            if (_searchHits.Count > 0 && SpanHasHit(lineIndex))
            {
                DrawSpanWithHighlight(line, lineIndex, span, ref col, width);
                continue;
            }

            _screen.Reset();
            var bg = span.Background ?? line.LineBackground ?? (_solidBackground ? _theme.Background : (Rgb?)null);
            if (bg is { } b) _screen.SetBackground(b);
            if (span.Color is { } c) _screen.SetForeground(c);
            else _screen.SetForeground(_theme.Text);
            if (span.Style != CellStyle.None) _screen.SetStyle(span.Style);
            _screen.Write(text);
            col += text.Length;
        }

        // Pad the rest of a backgrounded line so the block fills the full width.
        if (line.LineBackground is { } fill && col < width)
        {
            _screen.Reset();
            _screen.SetBackground(fill);
            _screen.Write(new string(' ', width - col));
        }
        _screen.Reset();
    }

    private bool SpanHasHit(int lineIndex) => _searchHits.Any(h => h.Line == lineIndex);

    private void DrawSpanWithHighlight(DisplayLine line, int lineIndex, StyledSpan span, ref int col, int width)
    {
        // Render character-by-character so we can flip background on hits within this line.
        var hits = _searchHits.Where(h => h.Line == lineIndex).ToList();
        var active = _searchHitIndex >= 0 && _searchHitIndex < _searchHits.Count ? _searchHits[_searchHitIndex] : (Line: -1, Col: -1, Len: 0);
        foreach (var ch in span.Text)
        {
            if (col >= width) break;
            int here = col;
            bool isHit = hits.Any(h => here >= h.Col && here < h.Col + h.Len);
            bool isActive = active.Line == lineIndex && here >= active.Col && here < active.Col + active.Len;
            _screen.Reset();
            if (isHit)
            {
                _screen.SetBackground(isActive ? _theme.SearchActiveBg : _theme.SearchBg);
                _screen.SetForeground(new Rgb(0, 0, 0));
            }
            else
            {
                var bg = span.Background ?? line.LineBackground ?? (_solidBackground ? _theme.Background : (Rgb?)null);
                if (bg is { } b) _screen.SetBackground(b);
                _screen.SetForeground(span.Color ?? _theme.Text);
                if (span.Style != CellStyle.None) _screen.SetStyle(span.Style);
            }
            _screen.Write(ch.ToString());
            col++;
        }
        _screen.Reset();
    }

    // ---------------- diagrams (Sixel) ----------------
    // Diagrams render at a FIXED size (scaled once to fit). When scrolling, we crop the source
    // bitmap to just the visible row-window and draw that — so the image scrolls smoothly like a
    // browser instead of resizing. Scaled bitmaps are cached per (key, theme, width).
    private readonly Dictionary<string, SKBitmap> _scaledDiagramCache = new();
    private readonly Dictionary<string, string> _sixelCache = new();

    private void ClearDiagramCaches()
    {
        foreach (var bmp in _scaledDiagramCache.Values) bmp.Dispose();
        _scaledDiagramCache.Clear();
        _sixelCache.Clear();
    }

    private SKBitmap GetScaledDiagram(string key, DiagramResult result)
    {
        var cacheKey = $"{key}-{(_theme.IsDark ? "d" : "l")}-{_screen.Width}-{_cellHeightPx}-{_diagramZoom}";
        if (_scaledDiagramCache.TryGetValue(cacheKey, out var cached) && !cached.IsNull) return cached;

        using var bmp = SKBitmap.Decode(result.Png);
        var (w, h) = ScaledSize(bmp.Width, bmp.Height, MaxDiagramRows);
        // Snap height UP to a whole number of cell rows so every scroll crop aligns exactly to a
        // row boundary — otherwise the partial last row makes the image jitter while scrolling.
        int rows = Math.Max(1, (int)Math.Ceiling(h / (double)_cellHeightPx));
        int snappedH = rows * _cellHeightPx;

        // Dark, transparent images (logos/icons, e.g. a black SVG mark) would vanish when flattened
        // onto the dark theme background. For *images* (not diagrams, which are theme-aware), give
        // such content a light "card" backdrop so it stays visible — like GitHub does in dark mode.
        bool isImage = key.StartsWith("img-") || key.StartsWith("imgrp-");
        var backdrop = (_theme.IsDark && isImage && NeedsLightCard(key, bmp))
            ? new Rgb(0xf6, 0xf8, 0xfa)
            : _theme.Background;

        var scaled = new SKBitmap(w, snappedH);
        using (var canvas = new SKCanvas(scaled))
        {
            canvas.Clear(ToSkColor(backdrop));
            using var resized = bmp.Resize(new SKImageInfo(w, h), SKSamplingOptions.Default);
            if (resized is not null)
            {
                // Center vertically within the snapped canvas.
                float top = (snappedH - h) / 2f;
                canvas.DrawBitmap(resized, 0, top);
            }
        }
        _scaledDiagramCache[cacheKey] = scaled;
        return scaled;
    }

    private readonly Dictionary<string, bool> _lightCardDecision = new();

    /// <summary>
    /// Decides whether an image needs a light backdrop in dark mode: true when it has meaningful
    /// transparency AND its visible (opaque) pixels are predominantly dark, so flattening onto the
    /// dark theme background would make it disappear. Sampled and cached per image key.
    /// </summary>
    private bool NeedsLightCard(string key, SKBitmap bmp)
    {
        if (_lightCardDecision.TryGetValue(key, out var cachedDecision)) return cachedDecision;

        long opaque = 0, transparent = 0;
        double lumaSum = 0;
        int stepX = Math.Max(1, bmp.Width / 64);
        int stepY = Math.Max(1, bmp.Height / 64);
        for (int y = 0; y < bmp.Height; y += stepY)
        for (int x = 0; x < bmp.Width; x += stepX)
        {
            var c = bmp.GetPixel(x, y);
            if (c.Alpha < 32) { transparent++; continue; }
            opaque++;
            // Rec. 601 luma, weighted by alpha (semi-transparent edges count less).
            double luma = 0.299 * c.Red + 0.587 * c.Green + 0.114 * c.Blue;
            lumaSum += luma * (c.Alpha / 255.0);
        }

        long total = opaque + transparent;
        bool decision = false;
        if (total > 0 && opaque > 0)
        {
            double transparentFrac = transparent / (double)total;
            double avgLuma = lumaSum / opaque;
            // Significant transparency (it's a logo/icon, not a photo) AND dark visible content.
            decision = transparentFrac > 0.15 && avgLuma < 110;
        }
        _lightCardDecision[key] = decision;
        return decision;
    }


    private static SKColor ToSkColor(Rgb c) => new(c.R, c.G, c.B);

    /// <summary>Maximum on-screen height (in rows) a diagram is allowed to occupy at its fixed size.</summary>
    private int MaxDiagramRows => Math.Max(8, (int)(Math.Min(ViewportHeight - 2, 30) * Math.Pow(1.25, Math.Max(0, _diagramZoom))));

    private void DrawDiagrams(int height)
    {
        foreach (var (key, result) in _diagramResults)
        {
            if (result.Status != DiagramStatus.Ready || result.Png is null) continue;

            var scaled = GetScaledDiagram(key, result);
            int imgRows = (int)Math.Ceiling(scaled.Height / (double)_cellHeightPx);

            // The same image/diagram can appear multiple times in a document (e.g. the same logo.png
            // used both standalone and inside a link). Draw at EVERY anchor with this key, not just
            // the first, so duplicate references all render.
            foreach (int anchorIndex in FindAllDiagramLines(key))
            {
                int imageTopRow = (anchorIndex + 1) - _scroll;
                int imageBottomRow = imageTopRow + imgRows;     // exclusive

                if (imageBottomRow <= 0 || imageTopRow >= height) continue;

                int topCropRows = imageTopRow < 0 ? -imageTopRow : 0;          // rows hidden above
                int drawRow = imageTopRow < 0 ? 0 : imageTopRow;              // first on-screen row
                int visibleRows = Math.Min(imageBottomRow, height) - drawRow; // rows visible on screen
                if (visibleRows < 1) continue;

                int srcY = Math.Clamp(topCropRows * _cellHeightPx, 0, scaled.Height);
                int srcH = Math.Clamp(visibleRows * _cellHeightPx, 1, scaled.Height - srcY);
                if (srcH < 1) continue;

                var cacheKey = $"{key}-{(_theme.IsDark ? "d" : "l")}-{_screen.Width}-{srcY}-{srcH}";
                if (!_sixelCache.TryGetValue(cacheKey, out var sixel))
                {
                    sixel = BuildCroppedSixel(scaled, srcY, srcH);
                    _sixelCache[cacheKey] = sixel;
                }

                _screen.MoveTo(drawRow, 1);
                _screen.WriteEscape(sixel);
            }
        }
    }

    /// <summary>
    /// Computes the scaled pixel size of a diagram so it fits the available width and a given number
    /// of terminal rows of height.
    /// </summary>
    private (int Width, int Height) ScaledSize(int pxWidth, int pxHeight, int maxRows)
    {
        double zoom = Math.Pow(1.25, _diagramZoom);   // Ctrl+wheel zoom steps
        int maxWidthPx = Math.Max(_cellWidthPx, (_screen.Width - 2) * _cellWidthPx);
        int maxHeightPx = Math.Max(_cellHeightPx, maxRows * _cellHeightPx);
        // Base fit scale (never upscales past the source at zoom 0), then apply the zoom factor.
        double fit = Math.Min(maxWidthPx / (double)pxWidth, maxHeightPx / (double)pxHeight);
        double scale = Math.Min(1.0, fit) * zoom;
        // Still clamp so an enlarged image can't exceed the usable width.
        scale = Math.Min(scale, maxWidthPx / (double)pxWidth);
        int w = Math.Max(1, (int)(pxWidth * scale));
        int h = Math.Max(1, (int)(pxHeight * scale));
        return (w, h);
    }

    /// <summary>Encodes a vertical slice [srcY, srcY+srcH) of a pre-scaled bitmap as Sixel.</summary>
    private string BuildCroppedSixel(SKBitmap scaled, int srcY, int srcH)
    {
        if (srcY == 0 && srcH == scaled.Height)
            return SixelEncoder.Encode(scaled, _theme.Background);

        using var crop = new SKBitmap(scaled.Width, srcH);
        using (var canvas = new SKCanvas(crop))
        {
            canvas.DrawBitmap(scaled, new SKRect(0, srcY, scaled.Width, srcY + srcH),
                new SKRect(0, 0, scaled.Width, srcH));
        }
        return SixelEncoder.Encode(crop, _theme.Background);
    }

    private int DiagramRows(DiagramResult result)
    {
        if (result.Png is null || result.PixelWidth <= 0 || result.PixelHeight <= 0) return 1;
        var (_, h) = ScaledSize(result.PixelWidth, result.PixelHeight, MaxDiagramRows);
        return Math.Max(1, (int)Math.Ceiling(h / (double)_cellHeightPx));
    }

    private int FindDiagramLine(string key)
    {
        for (int i = 0; i < _lines.Count; i++)
            if (_lines[i].DiagramKey == key) return i;
        return -1;
    }

    /// <summary>Yields every anchor line index for a key (a diagram/image may appear more than once).</summary>
    private IEnumerable<int> FindAllDiagramLines(string key)
    {
        for (int i = 0; i < _lines.Count; i++)
            if (_lines[i].DiagramKey == key) yield return i;
    }

    // ---------------- status bar ----------------
    private void DrawStatusBar()
    {
        int row = _screen.Height - 1;
        _screen.MoveTo(row, 0).ClearLine();
        _screen.SetBackground(_theme.IsDark ? new Rgb(22, 27, 34) : new Rgb(246, 248, 250));
        _screen.SetForeground(_theme.Muted);

        string left;
        if (_searchMode)
        {
            left = $" /{_searchQuery}";
            if (_searchHits.Count > 0) left += $"  [{_searchHitIndex + 1}/{_searchHits.Count}]";
            else if (_searchQuery.Length > 0) left += "  (no matches)";
        }
        else if (_statusMessage is not null)
        {
            left = " " + _statusMessage;
        }
        else
        {
            int pct = _lines.Count <= ViewportHeight ? 100
                : (int)(100.0 * _scroll / Math.Max(1, _lines.Count - ViewportHeight));
            left = $" {Path.GetFileName(_currentPath)}  {pct}%";
            if (_selectionMode) left += "  [SELECT]";
        }

        // Pick a help hint that fits the available width (vim keys shown when there's room).
        int width = _screen.Width;
        string help = width >= 112
            ? "j/k·gg/G·mouse move  Ctrl+f/b·d/u page  /:search n/N  t:toc  [:theme ]:bg  r:refresh  o:browser  ?:help  q:quit "
            : width >= 72
                ? "j/k gg/G  Ctrl+f/b page  /:find  t:toc  [/]:theme/bg  q:quit "
                : "j/k /:find t:toc q:quit ";
        var sb = new StringBuilder();
        sb.Append(left);
        int padding = width - left.Length - help.Length;
        if (padding > 0) sb.Append(new string(' ', padding));
        if (left.Length + help.Length <= width) sb.Append(help);
        var text = sb.ToString();
        if (text.Length > width) text = text[..width];
        _screen.Write(text);
        _screen.Reset();
    }

    // ---------------- TOC overlay ----------------
    private void DrawTocOverlay()
    {
        int width = Math.Min(50, _screen.Width - 4);
        int height = Math.Min(_toc.Count + 2, _screen.Height - 4);
        int top = 1, left = _screen.Width - width - 2;

        for (int i = 0; i < height; i++)
        {
            _screen.MoveTo(top + i, left);
            _screen.SetBackground(_theme.IsDark ? new Rgb(22, 27, 34) : new Rgb(246, 248, 250));
            _screen.SetForeground(_theme.Muted);
            _screen.Write(new string(' ', width));
        }

        _screen.MoveTo(top, left + 1).SetForeground(_theme.Heading).SetStyle(CellStyle.Bold)
            .SetBackground(_theme.IsDark ? new Rgb(22, 27, 34) : new Rgb(246, 248, 250)).Write("Contents (Enter to jump, Esc to close)");

        int visibleRows = height - 2;
        int start = Math.Max(0, Math.Min(_tocIndex - visibleRows / 2, Math.Max(0, _toc.Count - visibleRows)));
        for (int i = 0; i < visibleRows && start + i < _toc.Count; i++)
        {
            var entry = _toc[start + i];
            int idx = start + i;
            _screen.MoveTo(top + 1 + i, left + 1);
            _screen.SetBackground(idx == _tocIndex
                ? _theme.Accent
                : (_theme.IsDark ? new Rgb(22, 27, 34) : new Rgb(246, 248, 250)));
            _screen.SetForeground(idx == _tocIndex ? new Rgb(0, 0, 0) : _theme.Text);
            var indent = new string(' ', (entry.Level - 1) * 2);
            var label = indent + entry.Title;
            if (label.Length > width - 2) label = label[..(width - 2)];
            _screen.Write(label.PadRight(width - 2));
        }
        _screen.Reset();
    }

    // ---------------- help overlay (which-key style) ----------------
    private static readonly (string Heading, (string Keys, string Desc)[] Items)[] HelpSections =
    {
        ("Move", new[]
        {
            ("j / k", "down / up a line"),
            ("Ctrl+e / Ctrl+y", "scroll a line"),
            ("Ctrl+d / Ctrl+u", "half page"),
            ("Ctrl+f / Ctrl+b", "full page"),
            ("Space", "page down"),
            ("gg / G", "top / bottom"),
            ("mouse wheel", "scroll"),
        }),
        ("Find & navigate", new[]
        {
            ("/", "search"),
            ("n / N", "next / prev match"),
            ("t", "table of contents"),
            ("Enter / 1-9", "follow link ¹²³…"),
            ("← / → (Bksp)", "back / forward"),
        }),
        ("Diagrams & view", new[]
        {
            ("Ctrl + wheel", "zoom diagrams"),
            ("Ctrl + 0", "reset zoom"),
            ("[", "light / dark theme"),
            ("]", "solid background"),
            ("r", "re-render (refresh)"),
        }),
        ("Other", new[]
        {
            ("Shift + drag", "select text (mouse)"),
            ("m", "toggle select mode"),
            ("o", "open in browser"),
            ("? / q", "help / quit"),
        }),
    };

    private void DrawHelpOverlay()
    {
        var panelBg = _theme.IsDark ? new Rgb(22, 27, 34) : new Rgb(246, 248, 250);
        var border = _theme.Rule;

        // Build the rows: a heading line per section, then its items.
        var rows = new List<(string Keys, string Desc, bool Heading)>();
        foreach (var (heading, items) in HelpSections)
        {
            rows.Add((heading, "", true));
            foreach (var (keys, desc) in items) rows.Add((keys, desc, false));
            rows.Add(("", "", false)); // spacer
        }
        if (rows.Count > 0 && rows[^1] is { Keys: "", Desc: "" }) rows.RemoveAt(rows.Count - 1);

        int keyCol = rows.Max(r => r.Keys.Length);
        int descCol = rows.Where(r => !r.Heading).Select(r => r.Desc.Length).DefaultIfEmpty(0).Max();
        int innerWidth = Math.Max(34, keyCol + 2 + descCol);
        int boxWidth = Math.Min(_screen.Width - 2, innerWidth + 4);
        int boxHeight = Math.Min(_screen.Height - 2, rows.Count + 4);
        int top = Math.Max(0, (_screen.Height - boxHeight) / 2);
        int left = Math.Max(0, (_screen.Width - boxWidth) / 2);
        int contentW = boxWidth - 2;

        // Top border with a title.
        _screen.MoveTo(top, left).SetBackground(panelBg).SetForeground(border);
        var title = " Keybindings ";
        int dashLeft = (contentW - title.Length) / 2;
        int dashRight = contentW - title.Length - dashLeft;
        _screen.Write("╭" + new string('─', Math.Max(0, dashLeft)));
        _screen.SetForeground(_theme.Heading).SetStyle(CellStyle.Bold).Write(title);
        _screen.SetForeground(border).Write(new string('─', Math.Max(0, dashRight)) + "╮");

        for (int i = 0; i < boxHeight - 2; i++)
        {
            int r = top + 1 + i;
            _screen.MoveTo(r, left).SetBackground(panelBg).SetForeground(border).Write("│");
            _screen.SetBackground(panelBg);

            if (i < rows.Count)
            {
                var (keys, desc, heading) = rows[i];
                _screen.Write(" ");
                if (heading)
                {
                    _screen.SetForeground(_theme.Accent).SetStyle(CellStyle.Bold).Write(keys.PadRight(contentW - 1));
                }
                else if (keys.Length == 0 && desc.Length == 0)
                {
                    _screen.Write(new string(' ', contentW - 1));
                }
                else
                {
                    _screen.Reset(); _screen.SetBackground(panelBg);
                    _screen.SetForeground(_theme.Link).Write(keys.PadRight(keyCol));
                    _screen.SetForeground(_theme.Muted).Write("  ");
                    _screen.SetForeground(_theme.Text).Write(desc);
                    int written = keyCol + 2 + desc.Length;
                    if (written < contentW - 1) _screen.Write(new string(' ', contentW - 1 - written));
                }
            }
            else
            {
                _screen.Write(new string(' ', contentW));
            }
            _screen.SetForeground(border).Write("│");
        }

        // Bottom border with a hint.
        _screen.MoveTo(top + boxHeight - 1, left).SetBackground(panelBg).SetForeground(border);
        var hint = " press any key to close ";
        int hl = (contentW - hint.Length) / 2;
        int hr = contentW - hint.Length - hl;
        _screen.Write("╰" + new string('─', Math.Max(0, hl)));
        _screen.SetForeground(_theme.Muted).Write(hint);
        _screen.SetForeground(border).Write(new string('─', Math.Max(0, hr)) + "╯");
        _screen.Reset();
    }
}