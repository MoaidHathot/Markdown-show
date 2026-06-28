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

public class ExecutableResolverTests
{
    [Fact]
    public void Nonexistent_command_resolves_to_null()
    {
        Assert.Null(ExecutableResolver.Resolve("readmd-definitely-not-a-real-tool-xyz", ["--version"]));
        Assert.Null(ExecutableResolver.Find("readmd-definitely-not-a-real-tool-xyz"));
    }

    [Fact]
    public void Resolves_a_well_known_tool_on_path()
    {
        // 'dotnet' is guaranteed present in the test/build environment. On Windows it may be an
        // .exe (or a shim); on Unix it's a plain executable — either way Find must locate it.
        var found = ExecutableResolver.Find("dotnet");
        Assert.NotNull(found);
        Assert.True(File.Exists(found!) || Path.IsPathRooted(found));
    }

    [Fact]
    public void Resolve_builds_a_runnable_start_info_with_arguments()
    {
        var psi = ExecutableResolver.Resolve("dotnet", ["--version"]);
        Assert.NotNull(psi);
        Assert.False(psi!.UseShellExecute);
        // The final argument list ends with our requested args (a launcher prefix may precede them).
        Assert.Contains("--version", psi.ArgumentList);
    }
}

public class MermaidConfigTests
{
    [Fact]
    public void Default_config_uses_html_labels()
    {
        var json = MermaidTheme.ConfigJson(dark: true);
        Assert.Contains("\"htmlLabels\": true", json);
    }

    [Fact]
    public void Config_can_request_native_text_labels()
    {
        var json = MermaidTheme.ConfigJson(dark: true, htmlLabels: false);
        Assert.Contains("\"htmlLabels\": false", json);
        Assert.DoesNotContain("\"htmlLabels\": true", json);
    }

    [Fact]
    public void Config_includes_theme_variables_for_both_modes()
    {
        Assert.Contains("darkMode", MermaidTheme.ConfigJson(dark: true));
        Assert.Contains("darkMode", MermaidTheme.ConfigJson(dark: false));
    }
}
