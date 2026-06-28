using Markdig.Parsers;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;

namespace Readmd.Core;

/// <summary>
/// Replaces the default fenced-code rendering for <c>mermaid</c>/<c>d2</c> blocks with a
/// container the browser front-end can fill (mermaid client-side; D2 server-rendered SVG).
/// All other code blocks fall through to the standard renderer.
/// </summary>
public sealed class DiagramAwareCodeBlockRenderer(CodeBlockRenderer? fallback) : HtmlObjectRenderer<CodeBlock>
{
    private readonly CodeBlockRenderer _fallback = fallback ?? new CodeBlockRenderer();

    protected override void Write(HtmlRenderer renderer, CodeBlock obj)
    {
        if (obj is FencedCodeBlock fenced)
        {
            var kind = DiagramExtractor.MapKind(fenced.Info);
            if (kind is not null)
            {
                var source = fenced.Lines.ToString();
                var request = DiagramRequest.Create(kind.Value, source);
                WriteDiagram(renderer, kind.Value, request, source);
                return;
            }
        }

        _fallback.Write(renderer, obj);
    }

    private static void WriteDiagram(HtmlRenderer renderer, DiagramKind kind, DiagramRequest request, string source)
    {
        var kindClass = kind.ToString().ToLowerInvariant();
        renderer.Write("<figure class=\"readmd-diagram readmd-diagram-")
            .Write(kindClass)
            .Write("\" data-readmd-diagram=\"")
            .Write(kindClass)
            .Write("\" data-readmd-key=\"")
            .Write(request.Key)
            .Write("\">");

        if (kind == DiagramKind.Mermaid)
        {
            // Client-side mermaid renders the text content of .mermaid.
            renderer.Write("<pre class=\"mermaid\">");
            renderer.WriteEscape(source);
            renderer.Write("</pre>");
        }
        else
        {
            // D2 is rendered server-side; the placeholder is swapped for inline SVG.
            renderer.Write("<div class=\"readmd-d2-slot\" data-readmd-key=\"")
                .Write(request.Key)
                .Write("\"><div class=\"readmd-diagram-placeholder\">Rendering D2 diagram…</div></div>");
        }

        renderer.Write("</figure>");
        renderer.EnsureLine();
    }
}
