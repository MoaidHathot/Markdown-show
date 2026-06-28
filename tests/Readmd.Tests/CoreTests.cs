using Readmd.Core;

namespace Readmd.Tests;

public class CoreTests
{
    [Fact]
    public void Parse_extracts_table_of_contents()
    {
        var md = "# One\n\n## Two\n\n### Three\n";
        var doc = new MarkdownRenderer().Parse("doc.md", md);
        Assert.Equal(3, doc.Toc.Count);
        Assert.Equal("One", doc.Toc[0].Title);
        Assert.Equal(1, doc.Toc[0].Level);
        Assert.Equal("Three", doc.Toc[2].Title);
        Assert.Equal(3, doc.Toc[2].Level);
    }

    [Fact]
    public void Parse_html_does_not_contain_front_matter()
    {
        var md = "---\ntitle: Secret\n---\n\n# Hi\n";
        var doc = new MarkdownRenderer().Parse("doc.md", md);
        Assert.DoesNotContain("title: Secret", doc.Html);
        Assert.Contains("Hi", doc.Html);
    }

    [Fact]
    public void Mermaid_block_is_collected_as_a_diagram()
    {
        var md = "```mermaid\ngraph TD; A-->B;\n```\n";
        var doc = new MarkdownRenderer().Parse("doc.md", md);
        Assert.Contains(doc.Diagrams, d => d.Kind == DiagramKind.Mermaid);
    }
}

public class LinkResolverTests
{
    private static string Root => OperatingSystem.IsWindows() ? @"C:\docs" : "/docs";
    private static string Sibling => OperatingSystem.IsWindows() ? @"C:\docs-secret\x.md" : "/docs-secret/x.md";
    private static string Inside => OperatingSystem.IsWindows() ? @"C:\docs\sub\y.md" : "/docs/sub/y.md";

    [Fact]
    public void Sibling_directory_is_not_inside_root()
    {
        var resolver = new LinkResolver(Root);
        Assert.False(resolver.IsInsideRoot(Sibling), "a sibling dir sharing a name prefix must be rejected");
    }

    [Fact]
    public void Nested_path_is_inside_root()
    {
        var resolver = new LinkResolver(Root);
        Assert.True(resolver.IsInsideRoot(Inside));
    }

    [Fact]
    public void Root_itself_is_inside_root()
    {
        var resolver = new LinkResolver(Root);
        Assert.True(resolver.IsInsideRoot(Root));
    }

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("http://example.com/path")]
    public void External_urls_are_external(string url)
    {
        Assert.True(LinkResolver.IsExternal(url));
    }
}
