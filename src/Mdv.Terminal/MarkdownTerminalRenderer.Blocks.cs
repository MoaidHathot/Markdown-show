using Markdig.Extensions.Alerts;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Mdv.Core;

namespace Mdv.Terminal;

public sealed partial class MarkdownTerminalRenderer
{
    // ---------------- word-wrap emission ----------------
    /// <summary>
    /// Appends spans as one or more display lines, wrapping at word boundaries to fit
    /// <c>Width - indent</c>. Hard line breaks ("\n" spans) force a new line.
    /// </summary>
    private void EmitWrapped(List<StyledSpan> spans, int indent, string? headingId = null, int sourceLine = 0,
        Rgb? gutterColor = null, string? gutter = null)
    {
        int max = Math.Max(8, _width - indent);
        var current = NewLine(indent, headingId, sourceLine, gutterColor, gutter);
        int used = 0;
        bool firstLineOfBlock = true;

        void Flush()
        {
            _lines.Add(current);
            current = NewLine(indent, null, sourceLine, gutterColor, firstLineOfBlock ? null : gutter);
            used = 0;
        }

        foreach (var span in spans)
        {
            if (span.Text == "\n")
            {
                firstLineOfBlock = false;
                Flush();
                continue;
            }

            // Split span into words but keep spaces.
            foreach (var token in Tokenize(span.Text))
            {
                if (token == "\n") { firstLineOfBlock = false; Flush(); continue; }
                int tokenLen = token.Length;

                // A single token longer than the whole line (e.g. a long URL or path) would otherwise
                // be clipped at the screen edge. Hard-break it across lines at character boundaries.
                if (tokenLen > max && token.Trim().Length > 0)
                {
                    if (used > 0) { firstLineOfBlock = false; Flush(); }
                    int pos = 0;
                    while (pos < token.Length)
                    {
                        int take = Math.Min(max - used, token.Length - pos);
                        if (take <= 0) { firstLineOfBlock = false; Flush(); continue; }
                        current.Spans.Add(span with { Text = token.Substring(pos, take) });
                        used += take;
                        pos += take;
                        if (used >= max && pos < token.Length) { firstLineOfBlock = false; Flush(); }
                    }
                    continue;
                }

                if (used + tokenLen > max && used > 0)
                {
                    firstLineOfBlock = false;
                    Flush();
                    if (token.Trim().Length == 0) continue; // don't carry leading space to new line
                }
                if (used == 0 && token.Trim().Length == 0) continue; // skip leading spaces
                current.Spans.Add(span with { Text = token });
                used += tokenLen;
            }
        }

        _lines.Add(current);
    }

    private DisplayLine NewLine(int indent, string? headingId, int sourceLine, Rgb? gutterColor, string? gutter)
    {
        var line = new DisplayLine { HeadingId = headingId, SourceLine = sourceLine };
        if (indent > 0)
        {
            if (gutter is not null)
            {
                var pad = indent - gutter.Length;
                if (pad > 0) line.Spans.Add(new StyledSpan(new string(' ', pad)));
                line.Spans.Add(new StyledSpan(gutter, gutterColor ?? Theme.Muted));
            }
            else
            {
                line.Spans.Add(new StyledSpan(new string(' ', indent)));
            }
        }
        return line;
    }

    private static IEnumerable<string> Tokenize(string text)
    {
        int i = 0;
        while (i < text.Length)
        {
            if (text[i] == '\n') { yield return "\n"; i++; continue; }
            int start = i;
            bool space = char.IsWhiteSpace(text[i]);
            while (i < text.Length && text[i] != '\n' && char.IsWhiteSpace(text[i]) == space) i++;
            yield return text.Substring(start, i - start);
        }
    }

    // ---------------- lists ----------------
    private void RenderList(ListBlock list, int indent)
    {
        int number = 1;
        foreach (var item in list)
        {
            if (item is not ListItemBlock listItem) continue;
            var marker = list.IsOrdered ? $"{number}. " : "• ";
            number++;
            int childIndent = indent + marker.Length;
            bool firstChild = true;
            foreach (var child in listItem)
            {
                if (firstChild && child is ParagraphBlock para && para.Inline is not null)
                {
                    var spans = InlineToSpans(para.Inline, Theme.Text);
                    EmitWrapped(spans, childIndent, gutterColor: Theme.Accent, gutter: marker);
                    firstChild = false;
                }
                else
                {
                    RenderBlock(child, childIndent);
                }
            }
        }
        AddBlank();
    }

    // ---------------- quotes & alerts ----------------
    private void RenderQuote(QuoteBlock quote, int indent)
    {
        EnsureBlankBefore();
        foreach (var child in quote)
        {
            if (child is LeafBlock leaf && leaf.Inline is not null)
            {
                var spans = InlineToSpans(leaf.Inline, Theme.Quote, CellStyle.Italic);
                EmitWrapped(spans, indent + 2, gutterColor: Theme.Rule, gutter: "▌ ");
            }
            else
            {
                RenderBlock(child, indent + 2);
            }
        }
        AddBlank();
    }

    /// <summary>
    /// Renders a GitHub-style alert ([!NOTE], [!TIP], [!IMPORTANT], [!WARNING], [!CAUTION]) with a
    /// colored gutter, an icon + title header, and the body — matching the browser's alert styling.
    /// </summary>
    private void RenderAlert(AlertBlock alert, int indent)
    {
        EnsureBlankBefore();
        var kind = alert.Kind.ToString().ToUpperInvariant();
        (string icon, string title, Rgb color) = kind switch
        {
            "NOTE" => ("ℹ", "Note", Theme.IsDark ? Rgb.FromHex("#4493f8") : Rgb.FromHex("#0969da")),
            "TIP" => ("✔", "Tip", Theme.IsDark ? Rgb.FromHex("#3fb950") : Rgb.FromHex("#1a7f37")),
            "IMPORTANT" => ("◆", "Important", Theme.IsDark ? Rgb.FromHex("#ab7df8") : Rgb.FromHex("#8250df")),
            "WARNING" => ("⚠", "Warning", Theme.IsDark ? Rgb.FromHex("#d29922") : Rgb.FromHex("#9a6700")),
            "CAUTION" => ("⛔", "Caution", Theme.IsDark ? Rgb.FromHex("#f85149") : Rgb.FromHex("#cf222e")),
            _ => ("▌", TitleCase(kind), Theme.Accent),
        };


        // Header: icon + bold title in the alert color.
        var header = new List<StyledSpan>
        {
            new(icon + " ", color, CellStyle.Bold),
            new(title, color, CellStyle.Bold),
        };
        EmitWrapped(header, indent + 2, gutterColor: color, gutter: "▌ ");

        // Body: each child block, with the colored gutter continued.
        foreach (var child in alert)
        {
            if (child is LeafBlock leaf && leaf.Inline is not null)
            {
                var spans = InlineToSpans(leaf.Inline, Theme.Text);
                EmitWrapped(spans, indent + 2, gutterColor: color, gutter: "▌ ");
            }
            else
            {
                RenderBlock(child, indent + 2);
            }
        }
        AddBlank();
    }

    private static string TitleCase(string s) =>
        string.IsNullOrEmpty(s) ? "Note" : char.ToUpperInvariant(s[0]) + s[1..].ToLowerInvariant();

    // ---------------- footnotes ----------------
    /// <summary>Renders the footnote definitions at the end of the document: a labeled rule, then a
    /// numbered list where each entry is "ⁿ. body" (the same numbers used by the inline references).</summary>
    private void RenderFootnotes(Markdig.Extensions.Footnotes.FootnoteGroup group)
    {
        var notes = group.OfType<Markdig.Extensions.Footnotes.Footnote>().OrderBy(f => f.Order).ToList();
        if (notes.Count == 0) return;

        EnsureBlankBefore();
        // A short labeled separator: ── Footnotes ───
        var sep = new DisplayLine();
        int dash = Math.Max(0, Math.Min(_width - 1, 40) - " Footnotes ".Length);
        sep.Spans.Add(new StyledSpan("── ", Theme.Rule));
        sep.Spans.Add(new StyledSpan("Footnotes", Theme.Muted, CellStyle.Bold));
        sep.Spans.Add(new StyledSpan(" " + new string('─', dash), Theme.Rule));
        _lines.Add(sep);
        AddBlank();

        foreach (var note in notes)
        {
            string num = note.Order + ". ";
            bool first = true;
            foreach (var child in note)
            {
                if (child is Markdig.Syntax.LeafBlock leaf && leaf.Inline is not null)
                {
                    var spans = new List<StyledSpan>
                    {
                        new(first ? num : new string(' ', num.Length), Theme.Accent, CellStyle.Bold),
                    };
                    spans.AddRange(InlineToSpans(leaf.Inline, Theme.Text));
                    EmitWrapped(spans, 2);
                    first = false;
                }
                else
                {
                    RenderBlock(child, 2 + num.Length);
                }
            }
        }
        AddBlank();
    }

    // ---------------- definition lists ----------------
    /// <summary>Renders a definition list: each term is bold, and its definitions are indented and
    /// prefixed with a ':' marker (like the Markdown source), so terms and definitions are distinct.</summary>
    private void RenderDefinitionList(Markdig.Extensions.DefinitionLists.DefinitionList list, int indent)
    {
        EnsureBlankBefore();
        foreach (var itemBlock in list)
        {
            if (itemBlock is not Markdig.Extensions.DefinitionLists.DefinitionItem item) continue;
            foreach (var child in item)
            {
                if (child is Markdig.Extensions.DefinitionLists.DefinitionTerm term && term.Inline is not null)
                {
                    var spans = InlineToSpans(term.Inline, Theme.Heading, CellStyle.Bold);
                    EmitWrapped(spans, indent);
                }
                else if (child is Markdig.Syntax.LeafBlock leaf && leaf.Inline is not null)
                {
                    var spans = new List<StyledSpan> { new(": ", Theme.Accent) };
                    spans.AddRange(InlineToSpans(leaf.Inline, Theme.Text));
                    EmitWrapped(spans, indent + 2);
                }
                else
                {
                    RenderBlock(child, indent + 2);
                }
            }
        }
        AddBlank();
    }

    // ---------------- code blocks ----------------
    private void RenderFenced(FencedCodeBlock fenced, int indent)
    {
        var kind = DiagramExtractor.MapKind(fenced.Info);
        if (kind is not null)
        {
            var source = fenced.Lines.ToString();
            var request = DiagramRequest.Create(kind.Value, source);
            EmitDiagramAnchor(request, kind.Value, indent);
            return;
        }

        EnsureBlankBefore();
        var code = fenced.Lines.ToString().TrimEnd('\n');
        var language = (fenced.Info ?? "").Trim();
        var highlighted = CodeHighlighter.Highlight(code, language, Theme);
        EmitCodeBlock(highlighted, indent, language);
        AddBlank();
    }

    private void RenderIndentedCode(CodeBlock code, int indent)
    {
        EnsureBlankBefore();
        var text = code.Lines.ToString().TrimEnd('\n');
        var lines = text.Split('\n')
            .Select(raw => (List<StyledSpan>)[new StyledSpan(raw, Theme.Text)])
            .ToList();
        EmitCodeBlock(lines, indent, language: "");
        AddBlank();
    }

    /// <summary>
    /// Emits a code block in the style of <c>bat</c>: a header line with the language, a thin grid
    /// with line numbers in a left gutter separated by a vertical rule, and top/bottom borders.
    /// No heavy background fill — the structure (numbers + grid) is what sets it apart from prose.
    /// </summary>
    private void EmitCodeBlock(List<List<StyledSpan>> codeLines, int indent, string language)
    {
        var rule = Theme.CodeBorder;          // border / gutter color
        var numColor = Theme.Muted;           // line-number color
        string pad = indent > 0 ? new string(' ', indent) : "";

        int lineCount = codeLines.Count;
        int numWidth = Math.Max(2, lineCount.ToString().Length);
        string gutter = new string(' ', numWidth + 1);   // gutter width before the '│'
        int frameWidth = Math.Max(20, _width - indent - 1);
        int interior = Math.Max(8, frameWidth - (numWidth + 1) - 2); // minus gutter, "│ "

        // Top border:  ───────┬────────────────
        EmitGridBorder(pad, numWidth, frameWidth, '┬', rule);

        // Header:      <lang>  │
        if (!string.IsNullOrEmpty(language))
        {
            var header = new DisplayLine();
            if (pad.Length > 0) header.Spans.Add(new StyledSpan(pad));
            header.Spans.Add(new StyledSpan(gutter, rule));
            header.Spans.Add(new StyledSpan("│ ", rule));
            header.Spans.Add(new StyledSpan(language.ToLowerInvariant(), Theme.Heading, CellStyle.Bold));
            _lines.Add(header);
            // Header separator:  ───────┼────────────────
            EmitGridBorder(pad, numWidth, frameWidth, '┼', rule);
        }

        int n = 1;
        foreach (var spans in codeLines)
        {
            var line = new DisplayLine();
            if (pad.Length > 0) line.Spans.Add(new StyledSpan(pad));
            // Line number, right-aligned in a gutter exactly (numWidth + 1) wide so the '│'
            // lines up with the ┬/┼/┴ junctions in the borders.
            line.Spans.Add(new StyledSpan(n.ToString().PadLeft(numWidth) + " ", numColor));
            line.Spans.Add(new StyledSpan("│ ", rule));

            int used = 0;
            if (spans.Count > 0)
            {
                foreach (var span in spans)
                {
                    if (used >= interior) break;
                    var text = span.Text;
                    if (used + text.Length > interior) text = text[..Math.Max(0, interior - used)];
                    if (text.Length == 0) continue;
                    line.Spans.Add(span);
                    used += text.Length;
                }
            }
            _lines.Add(line);
            n++;
        }

        // Bottom border:  ───────┴────────────────
        EmitGridBorder(pad, numWidth, frameWidth, '┴', rule);
    }

    private void EmitGridBorder(string pad, int numWidth, int frameWidth, char junction, Rgb color)
    {
        int left = numWidth + 1;              // gutter chars before the junction
        int right = Math.Max(1, frameWidth - left - 1);
        var bar = new string('─', left) + junction + new string('─', right);
        var line = new DisplayLine();
        if (pad.Length > 0) line.Spans.Add(new StyledSpan(pad));
        line.Spans.Add(new StyledSpan(bar, color));
        _lines.Add(line);
    }

    // ---------------- thematic break ----------------
    private void RenderRule()
    {
        EnsureBlankBefore();
        var line = new DisplayLine();
        line.Spans.Add(new StyledSpan(new string('─', Math.Max(8, _width - 1)), Theme.Rule));
        _lines.Add(line);
        AddBlank();
    }

    // ---------------- display math ($$...$$) ----------------
    private void RenderMathBlock(Markdig.Extensions.Mathematics.MathBlock math, int indent)
    {
        EnsureBlankBefore();
        var latex = math.Lines.ToString();
        var rendered = MathRenderer.ToUnicode(latex);
        foreach (var raw in rendered.Split('\n'))
        {
            // Center the equation within the content width.
            int pad = Math.Max(indent + 2, (_width - raw.Length) / 2);
            var line = new DisplayLine();
            line.Spans.Add(new StyledSpan(new string(' ', pad)));
            line.Spans.Add(new StyledSpan(raw, Theme.Heading));
            _lines.Add(line);
        }
        AddBlank();
    }

    // ---------------- diagram anchor ----------------
    private void EmitDiagramAnchor(DiagramRequest request, DiagramKind kind, int indent)
    {
        EnsureBlankBefore();
        // Caption line for the diagram. The rendered image is drawn on the rows below this line;
        // the caption is just a label so it's clear what the image is (and where it begins).
        var label = kind == DiagramKind.Mermaid ? "Mermaid" : "D2";
        var anchor = new DisplayLine { DiagramKey = request.Key };
        anchor.Spans.Add(new StyledSpan("◆ ", Theme.Accent));
        anchor.Spans.Add(new StyledSpan(label + " diagram", Theme.Muted, CellStyle.Italic));
        _lines.Add(anchor);
        _pendingDiagrams[request.Key] = request;
        AddBlank();
    }

    private readonly Dictionary<string, DiagramRequest> _pendingDiagrams = [];
    public IReadOnlyDictionary<string, DiagramRequest> PendingDiagrams => _pendingDiagrams;

    // ---------------- inline images ----------------
    private readonly Dictionary<string, string> _pendingImages = []; // key -> url
    public IReadOnlyDictionary<string, string> PendingImages => _pendingImages;

    /// <summary>
    /// Emits an anchor line for an image (PNG/SVG/remote). The viewer loads and draws it inline via
    /// Sixel below the caption, the same way diagrams are drawn. If the image is wrapped in a link
    /// (<c>[![alt](img)](href)</c>), the caption becomes clickable and follows that link.
    /// </summary>
    internal void EmitImageAnchor(string url, string altText, string? linkUrl = null)
    {
        EnsureBlankBefore();
        var key = ImageKey(url);
        var caption = string.IsNullOrWhiteSpace(altText) ? "image" : altText;
        var anchor = new DisplayLine { DiagramKey = key };

        if (!string.IsNullOrEmpty(linkUrl))
        {
            // Clickable image: caption is a link, and the reserved image rows are tagged with the
            // same link id so clicking anywhere on the image follows it.
            var id = RegisterLink(linkUrl);
            anchor.ImageLinkId = id;
            anchor.Spans.Add(new StyledSpan("▣ ", Theme.Accent, LinkId: id));
            anchor.Spans.Add(new StyledSpan(caption, Theme.Link, CellStyle.Underline, id));
            AppendLinkMarker(anchor.Spans, id);
        }
        else
        {
            anchor.Spans.Add(new StyledSpan("▣ ", Theme.Accent));
            anchor.Spans.Add(new StyledSpan(caption, Theme.Muted, CellStyle.Italic));
        }
        _lines.Add(anchor);
        _pendingImages[key] = url;
        AddBlank();
    }

    internal static string ImageKey(string url) => "img-" + Hashing.Sha256Hex(url)[..16];

    // ---------------- inline image groups (badges side-by-side) ----------------
    private readonly Dictionary<string, List<string>> _pendingImageGroups = []; // groupKey -> urls
    public IReadOnlyDictionary<string, List<string>> PendingImageGroups => _pendingImageGroups;

    /// <summary>
    /// Emits a single anchor for several images laid out horizontally (e.g. a row of badges). The
    /// viewer loads each image, composes them side-by-side into one strip, and draws it.
    /// </summary>
    internal void EmitImageGroupAnchor(IReadOnlyList<(string Url, string Alt, string? LinkUrl)> images)
    {
        EnsureBlankBefore();
        var urls = images.Select(i => i.Url).ToList();
        var groupKey = "imgrp-" + Hashing.Sha256Hex(string.Join("|", urls))[..16];

        var anchor = new DisplayLine { DiagramKey = groupKey };
        anchor.Spans.Add(new StyledSpan("▣ ", Theme.Accent));
        anchor.Spans.Add(new StyledSpan($"{images.Count} images", Theme.Muted, CellStyle.Italic));
        _lines.Add(anchor);
        _pendingImageGroups[groupKey] = urls;
        AddBlank();
    }
}
