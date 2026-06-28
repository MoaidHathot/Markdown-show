using Readmd.Core;
using Readmd.Terminal;
using Readmd.Web;

namespace Readmd.Tests;

public class ExportTests
{
    // A diagram renderer stub that returns a trivial SVG, so export tests don't need Playwright/d2.
    private sealed class StubDiagrams : IDiagramRenderer
    {
        public DiagramResult? TryGet(string key) => null;
        public Task<DiagramResult> RenderAsync(DiagramRequest request, DiagramTheme theme, CancellationToken ct = default)
            => Task.FromResult(new DiagramResult(request.Key, DiagramStatus.Ready, null, "<svg data-stub=\"1\"></svg>", 10, 10, null));
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static string TempMd(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"readmd-test-{Guid.NewGuid():N}.md");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public async Task Html_export_is_self_contained()
    {
        var path = TempMd("# Title\n\nSome **text** and `code`.\n");
        try
        {
            var html = await HtmlExporter.ExportAsync(path, new StubDiagrams(), DiagramTheme.Dark);
            Assert.Contains("<style>", html);            // CSS inlined
            Assert.Contains("Title", html);              // body content present
            Assert.DoesNotContain("/_readmd/", html);    // no server-relative asset refs
            Assert.Contains("<!doctype html>", html);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Html_export_inlines_d2_as_svg()
    {
        var path = TempMd("```d2\na -> b\n```\n");
        try
        {
            var html = await HtmlExporter.ExportAsync(path, new StubDiagrams(), DiagramTheme.Dark);
            Assert.Contains("data-stub", html);                          // stub SVG was inlined
            Assert.DoesNotContain("class=\"readmd-d2-slot\"", html);     // placeholder slot div was replaced
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Html_export_uses_front_matter_title()
    {
        var path = TempMd("---\ntitle: Exported Doc\n---\n\n# Body\n");
        try
        {
            var html = await HtmlExporter.ExportAsync(path, new StubDiagrams(), DiagramTheme.Dark);
            Assert.Contains("<title>Exported Doc</title>", html);
        }
        finally { File.Delete(path); }
    }
}

public class DocumentTextRendererTests
{
    [Fact]
    public void Plain_text_has_no_ansi_escapes()
    {
        var text = DocumentTextRenderer.Render("# Heading\n\nBody text.\n", dark: true, width: 80, color: false);
        Assert.DoesNotContain("\e[", text);
        Assert.Contains("Heading", text);
        Assert.Contains("Body text.", text);
    }

    [Fact]
    public void Colored_output_contains_ansi_escapes()
    {
        var text = DocumentTextRenderer.Render("# Heading\n", dark: true, width: 80, color: true);
        Assert.Contains("\e[", text);
        Assert.Contains("Heading", text);
    }

    [Fact]
    public void Diagram_blocks_render_as_caption_lines()
    {
        var text = DocumentTextRenderer.Render("```mermaid\ngraph TD; A-->B;\n```\n", dark: true, width: 80, color: false);
        Assert.Contains("Mermaid diagram", text);
    }
}
