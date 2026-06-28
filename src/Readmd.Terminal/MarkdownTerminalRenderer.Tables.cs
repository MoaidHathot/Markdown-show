using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Readmd.Terminal;

public sealed partial class MarkdownTerminalRenderer
{
    private void RenderTable(Table table, int indent)
    {
        EnsureBlankBefore();

        // Column alignment metadata (left/center/right) from the table definition.
        var aligns = new List<TableColumnAlign?>();
        foreach (var def in table.ColumnDefinitions) aligns.Add(def.Alignment);

        // Collect styled spans per row/column.
        var styleRows = new List<List<List<StyledSpan>>>();
        foreach (var rowObj in table)
        {
            if (rowObj is not TableRow row) continue;
            var styled = new List<List<StyledSpan>>();
            foreach (var cellObj in row)
            {
                if (cellObj is not TableCell cell) continue;
                var spans = new List<StyledSpan>();
                foreach (var block in cell)
                {
                    if (block is LeafBlock leaf && leaf.Inline is not null)
                        spans.AddRange(InlineToSpans(leaf.Inline, Theme.Text));
                }
                styled.Add(spans);
            }
            styleRows.Add(styled);
        }
        if (styleRows.Count == 0) return;

        int colCount = styleRows.Max(r => r.Count);
        var widths = new int[colCount];
        foreach (var r in styleRows)
            for (int c = 0; c < r.Count; c++)
                widths[c] = Math.Max(widths[c], CellWidth(r[c]));

        // Fit to the terminal: shrink the widest columns first (so narrow columns stay intact),
        // then content that still overflows a column wraps onto extra lines instead of truncating.
        int budget = _width - indent - (colCount * 3) - 1;
        if (budget < colCount) budget = colCount;
        int sum = widths.Sum();
        while (sum > budget)
        {
            int widest = 0;
            for (int c = 1; c < colCount; c++) if (widths[c] > widths[widest]) widest = c;
            if (widths[widest] <= 3) break;          // can't shrink further sensibly
            widths[widest]--; sum--;
        }

        string pad = new(' ', indent);
        EmitBorder(pad, widths, '┌', '┬', '┐');
        for (int i = 0; i < styleRows.Count; i++)
        {
            EmitRowWrapped(pad, widths, styleRows[i], aligns, header: i == 0);
            if (i == 0) EmitBorder(pad, widths, '├', '┼', '┤');
        }
        EmitBorder(pad, widths, '└', '┴', '┘');
        AddBlank();
    }

    private void EmitBorder(string pad, int[] widths, char left, char mid, char right)
    {
        var line = new DisplayLine();
        var sb = new System.Text.StringBuilder();
        sb.Append(pad).Append(left);
        for (int c = 0; c < widths.Length; c++)
        {
            sb.Append(new string('─', widths[c] + 2));
            sb.Append(c == widths.Length - 1 ? right : mid);
        }
        line.Spans.Add(new StyledSpan(sb.ToString(), Theme.Rule));
        _lines.Add(line);
    }

    /// <summary>Emits one logical table row, wrapping each cell to its column width across as many
    /// visual lines as the tallest cell needs, honoring per-column alignment.</summary>
    private void EmitRowWrapped(string pad, int[] widths, List<List<StyledSpan>> cells,
        List<TableColumnAlign?> aligns, bool header)
    {
        // Wrap each cell into lines of styled segments.
        var wrappedCells = new List<List<List<StyledSpan>>>();
        int maxLines = 1;
        for (int c = 0; c < widths.Length; c++)
        {
            var spans = c < cells.Count ? cells[c] : [];
            if (header) spans = spans.Select(s => s with { Style = s.Style | CellStyle.Bold, Color = Theme.Heading }).ToList();
            var wrapped = WrapSpans(spans, widths[c]);
            if (wrapped.Count == 0) wrapped.Add([]);
            wrappedCells.Add(wrapped);
            maxLines = Math.Max(maxLines, wrapped.Count);
        }

        for (int lineIdx = 0; lineIdx < maxLines; lineIdx++)
        {
            var line = new DisplayLine();
            line.Spans.Add(new StyledSpan(pad + "│ ", Theme.Rule));
            for (int c = 0; c < widths.Length; c++)
            {
                var cellLines = wrappedCells[c];
                var segs = lineIdx < cellLines.Count ? cellLines[lineIdx] : [];
                int contentW = segs.Sum(s => DisplayWidth(s.Text));
                int free = Math.Max(0, widths[c] - contentW);

                var align = c < aligns.Count ? aligns[c] : null;
                int leftPad = align switch
                {
                    TableColumnAlign.Right => free,
                    TableColumnAlign.Center => free / 2,
                    _ => 0,
                };
                int rightPad = free - leftPad;

                if (leftPad > 0) line.Spans.Add(new StyledSpan(new string(' ', leftPad)));
                line.Spans.AddRange(segs);
                if (rightPad > 0) line.Spans.Add(new StyledSpan(new string(' ', rightPad)));
                line.Spans.Add(new StyledSpan(" │ ", Theme.Rule));
            }
            _lines.Add(line);
        }
    }

    /// <summary>Greedy word-wraps a list of styled spans to a target display width, preserving styles.</summary>
    private static List<List<StyledSpan>> WrapSpans(List<StyledSpan> spans, int width)
    {
        var lines = new List<List<StyledSpan>>();
        var current = new List<StyledSpan>();
        int curW = 0;

        void Flush() { if (current.Count > 0) { lines.Add(current); current = []; curW = 0; } }

        foreach (var span in spans)
        {
            // Split each span into word/space tokens so we can wrap at boundaries but keep style.
            foreach (var token in TokenizeKeepSpaces(span.Text))
            {
                int tw = DisplayWidth(token);
                bool isSpace = token.Length > 0 && char.IsWhiteSpace(token[0]);

                if (tw > width)
                {
                    // A single token longer than the column: hard-break it across lines.
                    Flush();
                    foreach (var chunk in HardChunks(token, width))
                    {
                        lines.Add([span with { Text = chunk }]);
                    }
                    // last chunk becomes the new current line
                    if (lines.Count > 0) { current = lines[^1]; curW = DisplayWidth(string.Concat(current.Select(s => s.Text))); lines.RemoveAt(lines.Count - 1); }
                    continue;
                }

                if (curW + tw > width)
                {
                    if (isSpace) continue;        // don't carry a leading space to the next line
                    Flush();
                }
                if (curW == 0 && isSpace) continue; // skip leading space at line start
                current.Add(span with { Text = token });
                curW += tw;
            }
        }
        Flush();
        return lines;
    }

    private static IEnumerable<string> TokenizeKeepSpaces(string s)
    {
        int i = 0;
        while (i < s.Length)
        {
            bool space = char.IsWhiteSpace(s[i]);
            int j = i;
            while (j < s.Length && char.IsWhiteSpace(s[j]) == space) j++;
            yield return s[i..j];
            i = j;
        }
    }

    private static IEnumerable<string> HardChunks(string s, int width)
    {
        // Break a long token at grapheme boundaries so we never split an emoji/combining sequence,
        // and so each chunk is at most `width` display columns.
        var sb = new System.Text.StringBuilder();
        int w = 0;
        foreach (var g in TextWidth.Graphemes(s))
        {
            int gw = TextWidth.ElementWidth(g);
            if (w + gw > width && sb.Length > 0)
            {
                yield return sb.ToString();
                sb.Clear();
                w = 0;
            }
            sb.Append(g);
            w += gw;
        }
        if (sb.Length > 0) yield return sb.ToString();
    }

    private static int CellWidth(List<StyledSpan> spans) =>
        spans.Sum(s => DisplayWidth(s.Text));

    /// <summary>Terminal display width (East-Asian wide / emoji = 2, combining marks = 0).</summary>
    private static int DisplayWidth(string s) => TextWidth.Of(s);
}
