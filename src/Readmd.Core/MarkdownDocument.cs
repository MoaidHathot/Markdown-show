namespace Readmd.Core;

/// <summary>
/// The kind of diagram embedded in a fenced code block that readmd renders to an image.
/// </summary>
public enum DiagramKind
{
    Mermaid,
    D2,
}

/// <summary>
/// A diagram extracted from the document. <see cref="Key"/> is a stable content hash used
/// for caching rendered output so unchanged diagrams are never re-rendered.
/// </summary>
public sealed record DiagramRequest(DiagramKind Kind, string Source, string Key)
{
    public static DiagramRequest Create(DiagramKind kind, string source)
    {
        var normalized = source.Replace("\r\n", "\n").Trim();
        var key = kind.ToString().ToLowerInvariant() + "-" + Hashing.Sha256Hex(normalized)[..16];
        return new DiagramRequest(kind, normalized, key);
    }
}

/// <summary>One entry in the document's table of contents.</summary>
public sealed record TocEntry(int Level, string Title, string Id, int Line);

/// <summary>
/// The result of parsing a markdown file: rendered HTML body (for browser mode),
/// the table of contents, the set of diagrams referenced, and the source line count.
/// </summary>
public sealed record MarkdownDocument(
    string SourcePath,
    string Title,
    string Html,
    IReadOnlyList<TocEntry> Toc,
    IReadOnlyList<DiagramRequest> Diagrams,
    string RawMarkdown)
{
    /// <summary>Parsed YAML front matter (empty when the document has none).</summary>
    public FrontMatter FrontMatter { get; init; } = FrontMatter.Empty;
}
