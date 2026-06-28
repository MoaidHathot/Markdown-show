using System.Text.RegularExpressions;
using Readmd.Core;

namespace Readmd.Web;

/// <summary>
/// Post-processes the Core-rendered HTML for the browser: local <c>.md</c> links get a
/// <c>data-readmd-local</c> attribute (so the SPA intercepts them), and local image <c>src</c>
/// values are rewritten to the sandboxed file endpoint.
/// </summary>
internal static partial class HtmlLinkRewriter
{
    public static string Rewrite(string html, string currentFileAbsolutePath, LinkResolver resolver)
    {
        html = HrefRegex().Replace(html, m =>
        {
            var href = m.Groups["url"].Value;
            var link = resolver.Resolve(href, currentFileAbsolutePath);
            return link.Kind switch
            {
                LinkKind.LocalFile => $"href=\"{m.Groups["url"].Value}\" data-readmd-local=\"{HtmlAttr(link.AbsolutePath!)}\"",
                LinkKind.External => $"href=\"{m.Groups["url"].Value}\" target=\"_blank\" rel=\"noopener\"",
                _ => m.Value,
            };
        });

        html = ImgRegex().Replace(html, m =>
        {
            var src = m.Groups["url"].Value;
            if (LinkResolver.IsExternal(src) || src.StartsWith("data:")) return m.Value;
            var link = resolver.Resolve(src, currentFileAbsolutePath);
            if (link.Kind == LinkKind.Image && link.AbsolutePath is not null)
                return $"src=\"/_readmd/file?path={Uri.EscapeDataString(link.AbsolutePath)}\"";
            return m.Value;
        });

        return html;
    }

    private static string HtmlAttr(string value) => value.Replace("\"", "&quot;");

    /// <summary>
    /// Export variant: local images are inlined as base64 data URIs (so the file is
    /// self-contained) and external links open in a new tab. Local <c>.md</c> links are left as
    /// plain hrefs (there is no SPA to intercept them in a static file).
    /// </summary>
    public static string RewriteForExport(string html, string currentFileAbsolutePath, LinkResolver resolver)
    {
        html = HrefRegex().Replace(html, m =>
        {
            var href = m.Groups["url"].Value;
            var link = resolver.Resolve(href, currentFileAbsolutePath);
            return link.Kind == LinkKind.External
                ? $"href=\"{href}\" target=\"_blank\" rel=\"noopener\""
                : m.Value;
        });

        html = ImgRegex().Replace(html, m =>
        {
            var src = m.Groups["url"].Value;
            if (LinkResolver.IsExternal(src) || src.StartsWith("data:")) return m.Value;
            var link = resolver.Resolve(src, currentFileAbsolutePath);
            if (link.Kind == LinkKind.Image && link.AbsolutePath is not null && File.Exists(link.AbsolutePath))
            {
                try
                {
                    var bytes = File.ReadAllBytes(link.AbsolutePath);
                    var mime = WebAssets.ContentType(link.AbsolutePath).Split(';')[0];
                    var b64 = Convert.ToBase64String(bytes);
                    return $"src=\"data:{mime};base64,{b64}\"";
                }
                catch { return m.Value; }
            }
            return m.Value;
        });

        return html;
    }

    [GeneratedRegex("""href="(?<url>[^"]*)"|href='(?<url>[^']*)'""", RegexOptions.IgnoreCase)]
    private static partial Regex HrefRegex();

    [GeneratedRegex("""src="(?<url>[^"]*)"|src='(?<url>[^']*)'""", RegexOptions.IgnoreCase)]
    private static partial Regex ImgRegex();
}
