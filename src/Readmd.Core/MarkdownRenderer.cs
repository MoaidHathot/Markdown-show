using System.Text;
using Markdig;
using Markdig.Extensions.AutoIdentifiers;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Readmd.Core.Toc;

namespace Readmd.Core;

/// <summary>
/// Parses markdown into a <see cref="MarkdownDocument"/>: HTML body for the browser,
/// a table of contents, and the list of mermaid/D2 diagrams referenced in the document.
/// This is the single source of truth shared by both the terminal and browser front-ends.
/// </summary>
public sealed class MarkdownRenderer
{
    private readonly MarkdownPipeline _pipeline;

    public MarkdownRenderer()
    {
        _pipeline = new MarkdownPipelineBuilder()
            .UseYamlFrontMatter()          // strip leading ---\n...\n--- so it doesn't leak as content
            .UseAdvancedExtensions()       // tables, footnotes, task lists, alerts, etc.
            .UseAutoIdentifiers(AutoIdentifierOptions.GitHub) // stable heading ids for TOC + anchors
            .UseEmojiAndSmiley()
            .UseMathematics()              // $...$ / $$...$$ -> KaTeX in browser
            .UseGenericAttributes()
            .UseTocMarker()                // [[_TOC_]]
            .Build();
    }

    public MarkdownDocument Parse(string sourcePath, string markdown)
    {
        var document = Markdown.Parse(markdown, _pipeline);

        var frontMatter = ExtractFrontMatter(document);
        var toc = TocExtractor.Extract(document);
        var diagrams = DiagramExtractor.Extract(document);
        var title = DeriveTitle(frontMatter, toc, sourcePath);
        var html = RenderHtml(document);
        html = FrontMatterHeader.Prepend(html, frontMatter);

        return new MarkdownDocument(sourcePath, title, html, toc, diagrams, markdown)
        {
            FrontMatter = frontMatter,
        };
    }

    private static FrontMatter ExtractFrontMatter(Markdig.Syntax.MarkdownDocument document)
    {
        if (document.Count > 0 && document[0] is Markdig.Extensions.Yaml.YamlFrontMatterBlock yaml)
            return FrontMatter.FromBlock(yaml);
        return FrontMatter.Empty;
    }

    private string RenderHtml(MarkdownObject document)
    {
        var writer = new StringWriter();
        var renderer = new HtmlRenderer(writer);
        _pipeline.Setup(renderer);

        // Swap in a custom code-block renderer that emits diagram containers for mermaid/d2.
        var existing = renderer.ObjectRenderers.FindExact<CodeBlockRenderer>();
        if (existing is not null)
        {
            renderer.ObjectRenderers.Remove(existing);
        }
        renderer.ObjectRenderers.AddIfNotAlready(new DiagramAwareCodeBlockRenderer(existing));

        renderer.Render(document);
        writer.Flush();
        return writer.ToString();
    }

    private static string DeriveTitle(FrontMatter frontMatter, IReadOnlyList<TocEntry> toc, string sourcePath)
    {
        // Front-matter `title:` wins, then the first H1, then any heading, then the file name.
        if (frontMatter.TryGet("title", out var fmTitle) && !string.IsNullOrWhiteSpace(fmTitle))
            return fmTitle;
        var firstH1 = toc.FirstOrDefault(t => t.Level == 1);
        if (firstH1 is not null) return firstH1.Title;
        var firstAny = toc.FirstOrDefault();
        if (firstAny is not null) return firstAny.Title;
        return Path.GetFileName(sourcePath);
    }
}
