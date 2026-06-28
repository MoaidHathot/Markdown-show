using Readmd.Core;

namespace Readmd.Web;

/// <summary>
/// Enumerates and searches the Markdown files under the document's sandbox root, for the browser's
/// project-wide search and quick-open palette. Bounded by file-count and file-size caps so a huge
/// folder can't make the server slow or memory-hungry.
/// </summary>
internal static class WikiIndex
{
    private const int MaxFiles = 2000;
    private const long MaxFileBytes = 2 * 1024 * 1024; // skip files larger than 2 MB
    private const int MaxResults = 100;
    private const int MaxPerFile = 5;

    /// <summary>A markdown file in the sandbox, with a path relative to the root for display.</summary>
    public sealed record TreeEntry(string Path, string Relative, string Title);

    /// <summary>One search hit: the file, the matching line number, and a trimmed snippet.</summary>
    public sealed record SearchHit(string Path, string Relative, string Title, int Line, string Snippet);

    /// <summary>Lists markdown files under <paramref name="root"/> (sorted, capped), skipping common noise dirs.</summary>
    public static IReadOnlyList<TreeEntry> ListFiles(string root)
    {
        var results = new List<TreeEntry>();
        foreach (var path in EnumerateMarkdown(root))
        {
            var rel = System.IO.Path.GetRelativePath(root, path).Replace('\\', '/');
            results.Add(new TreeEntry(path, rel, TitleFor(path, rel)));
            if (results.Count >= MaxFiles) break;
        }
        results.Sort((a, b) => string.Compare(a.Relative, b.Relative, StringComparison.OrdinalIgnoreCase));
        return results;
    }

    /// <summary>Searches all markdown files for <paramref name="query"/> (case-insensitive substring).</summary>
    public static IReadOnlyList<SearchHit> Search(string root, string query)
    {
        var hits = new List<SearchHit>();
        if (string.IsNullOrWhiteSpace(query) || query.Length < 2) return hits;

        foreach (var path in EnumerateMarkdown(root))
        {
            if (hits.Count >= MaxResults) break;
            try
            {
                var info = new FileInfo(path);
                if (info.Length > MaxFileBytes) continue;

                var rel = System.IO.Path.GetRelativePath(root, path).Replace('\\', '/');
                string title = TitleFor(path, rel);
                int perFile = 0;
                var lines = File.ReadLines(path);
                int lineNo = 0;
                foreach (var line in lines)
                {
                    lineNo++;
                    var idx = line.IndexOf(query, StringComparison.OrdinalIgnoreCase);
                    if (idx < 0) continue;
                    hits.Add(new SearchHit(path, rel, title, lineNo, Snippet(line, idx, query.Length)));
                    if (++perFile >= MaxPerFile) break;
                    if (hits.Count >= MaxResults) break;
                }
            }
            catch { /* unreadable file — skip */ }
        }
        return hits;
    }

    private static IEnumerable<string> EnumerateMarkdown(string root)
    {
        if (!Directory.Exists(root)) yield break;
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories);
        }
        catch { yield break; }

        foreach (var path in files)
        {
            if (!LinkResolver.IsMarkdownFile(path)) continue;
            // Skip common noise directories.
            var norm = path.Replace('\\', '/');
            if (norm.Contains("/node_modules/") || norm.Contains("/.git/") || norm.Contains("/bin/") || norm.Contains("/obj/"))
                continue;
            yield return path;
        }
    }

    // A display title: the first ATX/Setext H1 if present, else the file name.
    private static string TitleFor(string path, string relative)
    {
        try
        {
            foreach (var raw in File.ReadLines(path).Take(40))
            {
                var line = raw.Trim();
                if (line.StartsWith("# ")) return line[2..].Trim();
            }
        }
        catch { /* ignore */ }
        return System.IO.Path.GetFileName(relative);
    }

    private static string Snippet(string line, int matchStart, int matchLen)
    {
        const int pad = 40;
        var start = Math.Max(0, matchStart - pad);
        var end = Math.Min(line.Length, matchStart + matchLen + pad);
        var s = line[start..end].Trim();
        if (start > 0) s = "…" + s;
        if (end < line.Length) s += "…";
        return s;
    }
}
