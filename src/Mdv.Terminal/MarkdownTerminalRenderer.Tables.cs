using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Mdv.Terminal;

public sealed partial class MarkdownTerminalRenderer
{
    private void RenderTable(Table table, int indent)
    {
        EnsureBlankBefore();

        // Collect cell text per row/column.
        var rows = new List<List<string>>();
        var styleRows = new List<List<List<StyledSpan>>>();
        foreach (var rowObj in table)
        {
            if (rowObj is not TableRow row) continue;
            var cells = new List<string>();
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
                cells.Add(string.Concat(spans.Select(s => s.Text)));
            }
            rows.Add(cells);
            styleRows.Add(styled);
        }

        if (rows.Count == 0) return;

        int colCount = rows.Max(r => r.Count);
        var widths = new int[colCount];
        foreach (var r in rows)
            for (int c = 0; c < r.Count; c++)
                widths[c] = Math.Max(widths[c], DisplayWidth(r[c]));

        // Clamp total width to terminal.
        int budget = _width - indent - (colCount * 3) - 1;
        if (budget < colCount) budget = colCount;
        int sum = widths.Sum();
        if (sum > budget)
        {
            double scale = budget / (double)sum;
            for (int c = 0; c < colCount; c++) widths[c] = Math.Max(3, (int)(widths[c] * scale));
        }

        string pad = new(' ', indent);
        EmitBorder(pad, widths, '┌', '┬', '┐');
        for (int i = 0; i < rows.Count; i++)
        {
            EmitRow(pad, widths, styleRows[i], header: i == 0);
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

    private void EmitRow(string pad, int[] widths, List<List<StyledSpan>> cells, bool header)
    {
        var line = new DisplayLine();
        line.Spans.Add(new StyledSpan(pad + "│ ", Theme.Rule));
        for (int c = 0; c < widths.Length; c++)
        {
            var spans = c < cells.Count ? cells[c] : [];
            int textLen = spans.Sum(s => s.Text.Length);
            int truncateTo = widths[c];
            var emitted = TruncateSpans(spans, truncateTo, header);
            line.Spans.AddRange(emitted);
            int pad2 = widths[c] - Math.Min(textLen, truncateTo);
            if (pad2 > 0) line.Spans.Add(new StyledSpan(new string(' ', pad2)));
            line.Spans.Add(new StyledSpan(" │ ", Theme.Rule));
        }
        _lines.Add(line);
    }

    private List<StyledSpan> TruncateSpans(List<StyledSpan> spans, int max, bool header)
    {
        var result = new List<StyledSpan>();
        int used = 0;
        foreach (var span in spans)
        {
            if (used >= max) break;
            var text = span.Text;
            if (used + text.Length > max) text = text[..(max - used)];
            var style = header ? span.Style | CellStyle.Bold : span.Style;
            var color = header ? Theme.Heading : span.Color;
            result.Add(span with { Text = text, Style = style, Color = color });
            used += text.Length;
        }
        return result;
    }

    private static int DisplayWidth(string s) => s.Length;
}
