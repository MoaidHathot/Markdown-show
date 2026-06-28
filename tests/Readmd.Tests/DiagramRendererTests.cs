using Readmd.Core;
using Readmd.Diagrams;

namespace Readmd.Tests;

public class DiagramRendererTests
{
    private static DiagramRenderer NewRenderer() => new(new DiagramRendererOptions
    {
        BestEffort = true, // never launch Chromium in tests
        D2Path = "readmd-no-such-d2",
        GraphvizPath = "readmd-no-such-dot",
        PlantUmlPath = "readmd-no-such-plantuml",
        MermaidCliPath = "readmd-no-such-mmdc",
        CacheDirectory = Path.Combine(Path.GetTempPath(), "readmd-test-cache-" + Guid.NewGuid().ToString("N")),
    });

    [Theory]
    [InlineData(DiagramKind.D2, "digraph")]
    [InlineData(DiagramKind.Graphviz, "digraph { a -> b }")]
    [InlineData(DiagramKind.PlantUml, "@startuml\nA->B\n@enduml")]
    public async Task Missing_tool_yields_a_failed_result_with_message(DiagramKind kind, string source)
    {
        await using var renderer = NewRenderer();
        var req = DiagramRequest.Create(kind, source);
        var result = await renderer.RenderAsync(req, DiagramTheme.Dark);
        Assert.Equal(DiagramStatus.Failed, result.Status);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Fact]
    public async Task Mermaid_in_best_effort_without_mmdc_fails_with_hint()
    {
        await using var renderer = NewRenderer();
        var req = DiagramRequest.Create(DiagramKind.Mermaid, "graph TD; A-->B;");
        var result = await renderer.RenderAsync(req, DiagramTheme.Dark);
        Assert.Equal(DiagramStatus.Failed, result.Status);
        Assert.Contains("mermaid", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DiagramRequest_key_is_stable_for_same_content()
    {
        var a = DiagramRequest.Create(DiagramKind.D2, "a -> b\n");
        var b = DiagramRequest.Create(DiagramKind.D2, "a -> b");
        Assert.Equal(a.Key, b.Key); // trailing whitespace normalized
    }
}
