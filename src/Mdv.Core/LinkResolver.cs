namespace Mdv.Core;

public enum LinkKind
{
    External,   // http(s):, mailto:, etc. -> open in system browser
    Anchor,     // #heading -> jump within current doc
    LocalFile,  // ./other.md -> navigate (wiki) or open
    Image,      // ./pic.png -> render inline
    Unknown,
}

public sealed record ResolvedLink(LinkKind Kind, string Raw, string? AbsolutePath, string? Anchor);

/// <summary>
/// Resolves markdown link targets relative to the current document, sandboxed so a link can
/// never escape the configured root directory (defends the browser file endpoint).
/// </summary>
public sealed class LinkResolver(string rootDirectory)
{
    private static readonly string[] ImageExtensions =
        [".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".svg"];
    private static readonly string[] MarkdownExtensions =
        [".md", ".markdown", ".mdown", ".mkd"];

    public string Root { get; } = Path.GetFullPath(rootDirectory);

    public ResolvedLink Resolve(string target, string currentFileAbsolutePath)
    {
        if (string.IsNullOrWhiteSpace(target))
            return new ResolvedLink(LinkKind.Unknown, target, null, null);

        if (target.StartsWith('#'))
            return new ResolvedLink(LinkKind.Anchor, target, null, target.TrimStart('#'));

        if (IsExternal(target))
            return new ResolvedLink(LinkKind.External, target, null, null);

        // Split off any anchor on a local link.
        string path = target;
        string? anchor = null;
        var hash = target.IndexOf('#');
        if (hash >= 0)
        {
            path = target[..hash];
            anchor = target[(hash + 1)..];
        }

        if (string.IsNullOrEmpty(path))
            return new ResolvedLink(LinkKind.Anchor, target, null, anchor);

        var baseDir = Path.GetDirectoryName(currentFileAbsolutePath) ?? Root;
        string absolute;
        try
        {
            absolute = Path.GetFullPath(Uri.UnescapeDataString(path), baseDir);
        }
        catch
        {
            return new ResolvedLink(LinkKind.Unknown, target, null, anchor);
        }

        if (!IsInsideRoot(absolute))
            return new ResolvedLink(LinkKind.Unknown, target, null, anchor);

        var ext = Path.GetExtension(absolute).ToLowerInvariant();
        if (MarkdownExtensions.Contains(ext))
            return new ResolvedLink(LinkKind.LocalFile, target, absolute, anchor);
        if (ImageExtensions.Contains(ext))
            return new ResolvedLink(LinkKind.Image, target, absolute, anchor);

        return new ResolvedLink(LinkKind.Unknown, target, absolute, anchor);
    }

    public bool IsInsideRoot(string absolutePath)
    {
        var full = Path.GetFullPath(absolutePath);
        return full.StartsWith(Root, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsExternal(string target) =>
        Uri.TryCreate(target, UriKind.Absolute, out var uri) &&
        uri.Scheme is "http" or "https" or "mailto" or "ftp" or "tel";
}
