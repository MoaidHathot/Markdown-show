using Markdig;

namespace Readmd.Terminal;

/// <summary>
/// The single, process-wide Markdig pipeline used to parse documents for terminal rendering.
/// Built once (constructing the advanced-extensions pipeline is not free) and shared by the
/// interactive viewer and the non-interactive text renderer. It deliberately omits the browser's
/// <c>UseTocMarker</c>/<c>UseAutoIdentifiers</c>: the terminal detects <c>[[_TOC_]]</c> as a plain
/// paragraph and assigns its own heading slugs, so adding those extensions would change output.
/// </summary>
internal static class TerminalPipeline
{
    public static readonly MarkdownPipeline Instance =
        new MarkdownPipelineBuilder()
            .UseYamlFrontMatter()
            .UseAdvancedExtensions()
            .UseEmojiAndSmiley()
            .UseMathematics()
            .UseGenericAttributes()
            .Build();
}
