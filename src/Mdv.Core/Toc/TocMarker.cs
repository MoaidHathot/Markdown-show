using Markdig;
using Markdig.Parsers;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;

namespace Mdv.Core.Toc;

/// <summary>
/// A block representing an Azure DevOps style <c>[[_TOC_]]</c> marker. It renders as a
/// container that the front-end fills with the document's table of contents.
/// </summary>
public sealed class TocMarkerBlock(BlockParser parser) : LeafBlock(parser)
{
}

/// <summary>
/// Parses a line that consists solely of <c>[[_TOC_]]</c> (case-insensitive, optional
/// surrounding whitespace) into a <see cref="TocMarkerBlock"/>.
/// </summary>
public sealed class TocMarkerParser : BlockParser
{
    public TocMarkerParser()
    {
        OpeningCharacters = ['['];
    }

    public override BlockState TryOpen(BlockProcessor processor)
    {
        if (processor.IsCodeIndent) return BlockState.None;

        var line = processor.Line;
        var text = line.ToString().Trim();
        if (!IsTocMarker(text)) return BlockState.None;

        processor.NewBlocks.Push(new TocMarkerBlock(this)
        {
            Column = processor.Column,
            Span = new SourceSpan(processor.Start, line.End),
            Line = processor.LineIndex,
        });

        return BlockState.BreakDiscard;
    }

    internal static bool IsTocMarker(string text) =>
        text.Equals("[[_TOC_]]", StringComparison.OrdinalIgnoreCase) ||
        text.Equals("[[_toc_]]", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Renders <see cref="TocMarkerBlock"/> as a nav placeholder for the browser view.</summary>
public sealed class TocMarkerRenderer : HtmlObjectRenderer<TocMarkerBlock>
{
    protected override void Write(HtmlRenderer renderer, TocMarkerBlock obj)
    {
        renderer.WriteLine("<nav class=\"mdv-toc-inline\" data-mdv-toc-inline=\"true\"></nav>");
    }
}

/// <summary>Markdig extension wiring up the <c>[[_TOC_]]</c> marker parser and renderer.</summary>
public sealed class TocMarkerExtension : IMarkdownExtension
{
    public void Setup(MarkdownPipelineBuilder pipeline)
    {
        if (!pipeline.BlockParsers.Contains<TocMarkerParser>())
        {
            // Insert before the paragraph parser so the marker isn't swallowed as text.
            pipeline.BlockParsers.Insert(0, new TocMarkerParser());
        }
    }

    public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
    {
        if (renderer is HtmlRenderer html && !html.ObjectRenderers.Contains<TocMarkerRenderer>())
        {
            html.ObjectRenderers.Insert(0, new TocMarkerRenderer());
        }
    }
}

public static class TocMarkerPipelineExtensions
{
    public static MarkdownPipelineBuilder UseTocMarker(this MarkdownPipelineBuilder pipeline)
    {
        pipeline.Extensions.AddIfNotAlready<TocMarkerExtension>();
        return pipeline;
    }
}
