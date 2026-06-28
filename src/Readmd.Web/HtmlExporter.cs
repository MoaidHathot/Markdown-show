using System.Text;
using System.Text.RegularExpressions;
using Readmd.Core;
using Readmd.Diagrams;

namespace Readmd.Web;

/// <summary>
/// Produces a single, self-contained HTML file for a document: all CSS, JavaScript and fonts are
/// inlined, D2 diagrams are pre-rendered to inline SVG, and mermaid/KaTeX/highlight.js run
/// client-side from the embedded libraries. The result can be opened or shared without readmd
/// or a server, and is also the input used for PDF export.
/// </summary>
public static partial class HtmlExporter
{
    /// <summary>Renders <paramref name="filePath"/> to a standalone HTML string.</summary>
    public static async Task<string> ExportAsync(
        string filePath,
        IDiagramRenderer diagrams,
        DiagramTheme theme,
        CancellationToken ct = default)
    {
        var full = Path.GetFullPath(filePath);
        var root = Path.GetDirectoryName(full)!;
        var markdown = await File.ReadAllTextAsync(full, ct);

        var renderer = new MarkdownRenderer();
        var doc = renderer.Parse(full, markdown);
        var resolver = new LinkResolver(root);

        // Inline local images as data URIs and mark external links to open in a new tab.
        var body = HtmlLinkRewriter.RewriteForExport(doc.Html, full, resolver);
        // Pre-render server-side diagrams (D2/Graphviz/PlantUML) to inline SVG so the static file
        // needs no server. Mermaid still renders client-side from the embedded library.
        body = await InlineServerDiagramsAsync(body, doc, diagrams, theme, ct);

        return BuildDocument(doc.Title, body, theme);
    }

    private static async Task<string> InlineServerDiagramsAsync(
        string html, MarkdownDocument doc, IDiagramRenderer diagrams, DiagramTheme theme, CancellationToken ct)
    {
        foreach (var req in doc.Diagrams)
        {
            if (req.Kind == DiagramKind.Mermaid) continue; // mermaid renders client-side
            var slot = $"<div class=\"readmd-d2-slot\" data-readmd-key=\"{req.Key}\">";
            // The placeholder text varies by kind; match on the slot opening and replace the whole div.
            var startIdx = html.IndexOf(slot, StringComparison.Ordinal);
            if (startIdx < 0) continue;
            var endIdx = html.IndexOf("</div>", startIdx, StringComparison.Ordinal);
            if (endIdx < 0) continue;
            endIdx += "</div>".Length;

            string replacement;
            try
            {
                var result = await diagrams.RenderAsync(req, theme, ct);
                replacement = result.Status == DiagramStatus.Ready && result.Svg is not null
                    ? result.Svg
                    : $"<div class=\"readmd-diagram-error\">{req.Kind} error: {System.Net.WebUtility.HtmlEncode(result.Error ?? "render failed")}</div>";
            }
            catch (Exception ex)
            {
                replacement = $"<div class=\"readmd-diagram-error\">{req.Kind} error: {System.Net.WebUtility.HtmlEncode(ex.Message)}</div>";
            }
            html = html[..startIdx] + replacement + html[endIdx..];
        }
        return html;
    }

    private static string BuildDocument(string title, string body, DiagramTheme theme)
    {
        var themeName = theme == DiagramTheme.Dark ? "dark" : "light";
        var appCss = WebAssets.ReadText("app.css");
        var katexCss = InlineFonts(WebAssets.ReadText("vendor/katex.min.css"));
        var hljsCss = WebAssets.ReadText(theme == DiagramTheme.Dark ? "vendor/github-dark.min.css" : "vendor/github.min.css");

        var katexJs = WebAssets.ReadText("vendor/katex.min.js");
        var autoRender = WebAssets.ReadText("vendor/auto-render.min.js");
        var hljsJs = WebAssets.ReadText("vendor/highlight.min.js");
        var powershell = WebAssets.ReadText("vendor/powershell.min.js");
        var mermaidJs = WebAssets.ReadText("vendor/mermaid.min.js");
        var mermaidThemes = MermaidTheme.ThemeVariablesByMode();

        var sb = new StringBuilder();
        sb.Append("<!doctype html>\n<html lang=\"en\" data-theme=\"").Append(themeName).Append("\">\n<head>\n");
        sb.Append("<meta charset=\"utf-8\" />\n");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />\n");
        sb.Append("<title>").Append(System.Net.WebUtility.HtmlEncode(title)).Append("</title>\n");
        sb.Append("<style>\n").Append(katexCss).Append("\n</style>\n");
        sb.Append("<style>\n").Append(hljsCss).Append("\n</style>\n");
        sb.Append("<style>\n").Append(appCss).Append("\n</style>\n");
        // Export tweaks: no fixed sidebar/toolbar; content centered for printing/sharing.
        sb.Append("<style>\n").Append(ExportOverrideCss).Append("\n</style>\n");
        sb.Append("</head>\n<body>\n");
        sb.Append("<article id=\"readmd-content\">").Append(body).Append("</article>\n");

        sb.Append("<script>").Append(katexJs).Append("</script>\n");
        sb.Append("<script>").Append(autoRender).Append("</script>\n");
        sb.Append("<script>").Append(hljsJs).Append("</script>\n");
        sb.Append("<script>").Append(powershell).Append("</script>\n");
        sb.Append("<script>").Append(mermaidJs).Append("</script>\n");
        sb.Append("<script>window.__READMD_MERMAID__ = ").Append(mermaidThemes).Append(";</script>\n");
        sb.Append("<script>\n").Append(ExportBootstrapJs).Append("\n</script>\n");
        sb.Append("</body>\n</html>\n");
        return sb.ToString();
    }

    /// <summary>Replaces <c>url(fonts/X.woff2)</c> in the KaTeX CSS with base64 data URIs.</summary>
    private static string InlineFonts(string css)
    {
        return FontUrlRegex().Replace(css, m =>
        {
            var file = m.Groups["f"].Value;
            var bytes = WebAssets.TryReadBytes("vendor/fonts/" + file);
            if (bytes is null) return m.Value;
            var b64 = Convert.ToBase64String(bytes);
            return $"url(data:font/woff2;base64,{b64}) format(\"woff2\")";
        });
    }

    [GeneratedRegex("""url\(fonts/(?<f>[^)]+\.woff2)\)\s*format\("woff2"\)""")]
    private static partial Regex FontUrlRegex();

    private const string ExportOverrideCss = """
        /* Standalone export: drop the app chrome and center the content. */
        #readmd-layout, #readmd-sidebar, #readmd-toolbar, #readmd-status, #readmd-help-overlay { display: none !important; }
        body { margin: 0; background: var(--bg); color: var(--fg); }
        #readmd-content {
          display: block; max-width: var(--readmd-max-content, 900px);
          margin: 0 auto; padding: 2.5rem 1.5rem 4rem;
        }
        @media print {
          body { background: #fff; }
          #readmd-content { max-width: none; }
          .readmd-diagram, pre, table, figure { break-inside: avoid; }
        }
        """;

    private const string ExportBootstrapJs = """
        (function () {
          var theme = document.documentElement.getAttribute("data-theme") || "dark";
          function mermaidConfig() {
            var themes = window.__READMD_MERMAID__ || {};
            return {
              startOnLoad: false, theme: "base", securityLevel: "loose",
              themeVariables: themes[theme === "dark" ? "dark" : "light"] || {},
              flowchart: { curve: "basis", htmlLabels: true, padding: 12 },
              sequence: { useMaxWidth: true, mirrorActors: false },
            };
          }
          if (window.hljs) {
            document.querySelectorAll("pre code").forEach(function (b) {
              if (!b.closest(".readmd-diagram")) window.hljs.highlightElement(b);
            });
          }
          if (window.renderMathInElement) {
            window.renderMathInElement(document.body, {
              delimiters: [
                { left: "$$", right: "$$", display: true },
                { left: "$", right: "$", display: false },
                { left: "\\(", right: "\\)", display: false },
                { left: "\\[", right: "\\]", display: true },
              ], throwOnError: false,
            });
          }
          if (window.mermaid && window.mermaid.run) {
            window.mermaid.initialize(mermaidConfig());
            document.querySelectorAll("figure.readmd-diagram-mermaid pre.mermaid").forEach(async function (pre) {
              var id = "m" + Math.random().toString(36).slice(2);
              try {
                var out = await window.mermaid.render(id, pre.textContent);
                pre.closest("figure").innerHTML = out.svg;
              } catch (e) {
                pre.outerHTML = '<div class="readmd-diagram-error">Mermaid error: ' + String(e) + '</div>';
              }
            });
          }
        })();
        """;
}
