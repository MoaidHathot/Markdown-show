using System.Text.RegularExpressions;
using Mdv.Core;

namespace Mdv.Web;

/// <summary>
/// Post-processes the Core-rendered HTML for the browser: local <c>.md</c> links get a
/// <c>data-mdv-local</c> attribute (so the SPA intercepts them), and local image <c>src</c>
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
                LinkKind.LocalFile => $"href=\"{m.Groups["url"].Value}\" data-mdv-local=\"{HtmlAttr(link.AbsolutePath!)}\"",
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
                return $"src=\"/_mdv/file?path={Uri.EscapeDataString(link.AbsolutePath)}\"";
            return m.Value;
        });

        return html;
    }

    private static string HtmlAttr(string value) => value.Replace("\"", "&quot;");

    [GeneratedRegex("""href="(?<url>[^"]*)"|href='(?<url>[^']*)'""", RegexOptions.IgnoreCase)]
    private static partial Regex HrefRegex();

    [GeneratedRegex("""src="(?<url>[^"]*)"|src='(?<url>[^']*)'""", RegexOptions.IgnoreCase)]
    private static partial Regex ImgRegex();
}
