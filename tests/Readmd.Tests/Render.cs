using Markdig;
using Readmd.Core;
using Readmd.Terminal;

namespace Readmd.Tests;

/// <summary>Helpers to render Markdown to terminal display lines for assertions.</summary>
internal static class Render
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseYamlFrontMatter()
        .UseAdvancedExtensions()
        .UseEmojiAndSmiley()
        .UseMathematics()
        .UseGenericAttributes()
        .Build();

    /// <summary>Renders markdown and returns each display line's plain text.</summary>
    public static List<string> Lines(string markdown, int width = 100, bool dark = true)
    {
        var doc = Markdown.Parse(markdown, Pipeline);
        var renderer = new MarkdownTerminalRenderer(TerminalTheme.For(dark), width);
        var result = renderer.Render(doc, null);
        return result.Lines.Select(l => l.PlainText).ToList();
    }

    /// <summary>Renders markdown and returns the whole document as a single newline-joined string.</summary>
    public static string Text(string markdown, int width = 100, bool dark = true) =>
        string.Join("\n", Lines(markdown, width, dark));
}
