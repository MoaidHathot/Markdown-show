using System.Net;
using Readmd.Core;
using Readmd.Web;

namespace Readmd.Tests;

public class WebServerTests
{
    private sealed class StubDiagrams : IDiagramRenderer
    {
        public DiagramResult? TryGet(string key) => null;
        public Task<DiagramResult> RenderAsync(DiagramRequest request, DiagramTheme theme, CancellationToken ct = default)
            => Task.FromResult(new DiagramResult(request.Key, DiagramStatus.Ready, null, "<svg/>", 4, 4, null));
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static string WriteDoc(string dir, string name, string content)
    {
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public async Task Root_serves_the_shell_with_rendered_body()
    {
        var dir = Path.Combine(Path.GetTempPath(), "readmd-web-" + Guid.NewGuid().ToString("N"));
        var doc = WriteDoc(dir, "doc.md", "# Hello Web\n\nBody.\n");
        await using var server = new WebViewerServer(new WebViewerOptions { FilePath = doc, Port = 0 }, new StubDiagrams());
        await server.StartAsync();
        try
        {
            using var http = new HttpClient();
            var html = await http.GetStringAsync(server.Url);
            Assert.Contains("Hello Web", html);
            Assert.Contains("readmd-content", html);
            Assert.Contains("__READMD_MERMAID__", html); // mermaid palette injected
        }
        finally { TryDelete(dir); }
    }

    [Fact]
    public async Task Doc_endpoint_returns_json_with_title_and_html()
    {
        var dir = Path.Combine(Path.GetTempPath(), "readmd-web-" + Guid.NewGuid().ToString("N"));
        var doc = WriteDoc(dir, "doc.md", "# JSON Title\n\nText.\n");
        await using var server = new WebViewerServer(new WebViewerOptions { FilePath = doc, Port = 0 }, new StubDiagrams());
        await server.StartAsync();
        try
        {
            using var http = new HttpClient();
            var json = await http.GetStringAsync(server.Url + "/_readmd/doc");
            Assert.Contains("\"title\"", json);
            Assert.Contains("JSON Title", json);
            Assert.Contains("\"html\"", json);
        }
        finally { TryDelete(dir); }
    }

    [Fact]
    public async Task File_endpoint_rejects_paths_outside_the_sandbox()
    {
        var dir = Path.Combine(Path.GetTempPath(), "readmd-web-" + Guid.NewGuid().ToString("N"));
        var doc = WriteDoc(dir, "doc.md", "# Hi\n");
        // A sibling file outside the document's directory must not be served.
        var outsideDir = Path.Combine(Path.GetTempPath(), "readmd-web-outside-" + Guid.NewGuid().ToString("N"));
        var secret = WriteDoc(outsideDir, "secret.txt", "top secret");

        await using var server = new WebViewerServer(new WebViewerOptions { FilePath = doc, Port = 0 }, new StubDiagrams());
        await server.StartAsync();
        try
        {
            using var http = new HttpClient();
            var resp = await http.GetAsync($"{server.Url}/_readmd/file?path={Uri.EscapeDataString(secret)}");
            Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        }
        finally { TryDelete(dir); TryDelete(outsideDir); }
    }

    [Fact]
    public async Task Unknown_asset_returns_404()
    {
        var dir = Path.Combine(Path.GetTempPath(), "readmd-web-" + Guid.NewGuid().ToString("N"));
        var doc = WriteDoc(dir, "doc.md", "# Hi\n");
        await using var server = new WebViewerServer(new WebViewerOptions { FilePath = doc, Port = 0 }, new StubDiagrams());
        await server.StartAsync();
        try
        {
            using var http = new HttpClient();
            var resp = await http.GetAsync(server.Url + "/_readmd/does-not-exist.js");
            Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        }
        finally { TryDelete(dir); }
    }

    [Fact]
    public async Task Export_html_returns_self_contained_attachment()
    {
        var dir = Path.Combine(Path.GetTempPath(), "readmd-web-" + Guid.NewGuid().ToString("N"));
        var doc = WriteDoc(dir, "report.md", "# Title\n\nBody and `code`.\n");
        await using var server = new WebViewerServer(new WebViewerOptions { FilePath = doc, Port = 0 }, new StubDiagrams());
        await server.StartAsync();
        try
        {
            using var http = new HttpClient();
            var resp = await http.GetAsync(server.Url + "/_readmd/export?format=html");
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            Assert.Contains("attachment", resp.Content.Headers.ContentDisposition?.ToString() ?? "");
            Assert.Contains("report", resp.Content.Headers.ContentDisposition?.ToString() ?? "");
            var html = await resp.Content.ReadAsStringAsync();
            Assert.Contains("<style>", html);
            Assert.DoesNotContain("/_readmd/", html); // self-contained
        }
        finally { TryDelete(dir); }
    }

    [Fact]
    public async Task Export_rejects_path_outside_the_sandbox()
    {
        var dir = Path.Combine(Path.GetTempPath(), "readmd-web-" + Guid.NewGuid().ToString("N"));
        var doc = WriteDoc(dir, "doc.md", "# Hi\n");
        var outsideDir = Path.Combine(Path.GetTempPath(), "readmd-web-out-" + Guid.NewGuid().ToString("N"));
        var secret = WriteDoc(outsideDir, "secret.md", "# Secret\n");
        await using var server = new WebViewerServer(new WebViewerOptions { FilePath = doc, Port = 0 }, new StubDiagrams());
        await server.StartAsync();
        try
        {
            using var http = new HttpClient();
            var resp = await http.GetAsync($"{server.Url}/_readmd/export?format=html&path={Uri.EscapeDataString(secret)}");
            Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        }
        finally { TryDelete(dir); TryDelete(outsideDir); }
    }

    [Fact]
    public async Task Search_finds_matches_in_other_files()
    {
        var dir = Path.Combine(Path.GetTempPath(), "readmd-web-" + Guid.NewGuid().ToString("N"));
        WriteDoc(dir, "doc.md", "# Home\n");
        WriteDoc(dir, "other.md", "# Other\n\nThe quick brown zqxfox jumps.\n");
        await using var server = new WebViewerServer(new WebViewerOptions { FilePath = Path.Combine(dir, "doc.md"), Port = 0 }, new StubDiagrams());
        await server.StartAsync();
        try
        {
            using var http = new HttpClient();
            var json = await http.GetStringAsync(server.Url + "/_readmd/search?q=zqxfox");
            Assert.Contains("other.md", json);
            Assert.Contains("zqxfox", json);
        }
        finally { TryDelete(dir); }
    }

    [Fact]
    public async Task Tree_lists_markdown_files_in_the_sandbox()
    {
        var dir = Path.Combine(Path.GetTempPath(), "readmd-web-" + Guid.NewGuid().ToString("N"));
        WriteDoc(dir, "a.md", "# A\n");
        WriteDoc(Path.Combine(dir, "sub"), "b.md", "# B\n");
        WriteDoc(dir, "ignore.txt", "not markdown");
        await using var server = new WebViewerServer(new WebViewerOptions { FilePath = Path.Combine(dir, "a.md"), Port = 0 }, new StubDiagrams());
        await server.StartAsync();
        try
        {
            using var http = new HttpClient();
            var json = await http.GetStringAsync(server.Url + "/_readmd/tree");
            Assert.Contains("a.md", json);
            Assert.Contains("b.md", json);
            Assert.DoesNotContain("ignore.txt", json);
        }
        finally { TryDelete(dir); }
    }

    private static void TryDelete(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { /* ignore */ }
    }
}
