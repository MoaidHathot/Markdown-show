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

    [Fact]
    public void Front_matter_title_overrides_first_heading()
    {
        var md = "---\ntitle: From Front Matter\n---\n\n# A Different Heading\n";
        var doc = new MarkdownRenderer().Parse("doc.md", md);
        Assert.Equal("From Front Matter", doc.Title);
    }

    [Fact]
    public void Front_matter_is_parsed_into_scalars_and_lists()
    {
        var md = "---\ntitle: Report\nauthor: Jane Smith\ntags: [a, b, c]\n---\n\n# Body\n";
        var doc = new MarkdownRenderer().Parse("doc.md", md);
        Assert.Equal("Report", doc.FrontMatter.Get("title"));
        Assert.Equal("Jane Smith", doc.FrontMatter.Get("author"));
        Assert.Equal(new[] { "a", "b", "c" }, doc.FrontMatter.GetList("tags"));
    }

    [Fact]
    public void Front_matter_metadata_header_is_emitted_in_html()
    {
        var md = "---\ntitle: Report\nauthor: Jane\n---\n\n# Body\n";
        var doc = new MarkdownRenderer().Parse("doc.md", md);
        Assert.Contains("readmd-frontmatter", doc.Html);
        Assert.Contains("Jane", doc.Html);
    }

    [Fact]
    public void Document_without_front_matter_has_empty_front_matter_and_no_header()
    {
        var md = "# Title\n\nBody.\n";
        var doc = new MarkdownRenderer().Parse("doc.md", md);
        Assert.True(doc.FrontMatter.IsEmpty);
        Assert.DoesNotContain("readmd-frontmatter", doc.Html);
    }

    [Theory]
    [InlineData("key: \"quoted value\"", "key", "quoted value")]
    [InlineData("key: 'single quoted'", "key", "single quoted")]
    [InlineData("url: https://example.com:8080/x", "url", "https://example.com:8080/x")]
    public void Front_matter_parses_scalars(string line, string key, string expected)
    {
        var fm = FrontMatter.Parse(line);
        Assert.Equal(expected, fm.Get(key));
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
