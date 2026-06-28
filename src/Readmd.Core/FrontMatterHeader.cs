using System.Net;
using System.Text;

namespace Readmd.Core;

/// <summary>
/// Renders an optional, unobtrusive metadata header (title, author, date, tags) from a
/// document's YAML front matter and prepends it to the rendered HTML body. Nothing is emitted
/// when the front matter has none of the recognized keys, so plain documents are unaffected.
/// </summary>
internal static class FrontMatterHeader
{
    public static string Prepend(string html, FrontMatter fm)
    {
        var header = Build(fm);
        return header.Length == 0 ? html : header + html;
    }

    /// <summary>Builds the metadata header HTML, or an empty string if there's nothing to show.</summary>
    public static string Build(FrontMatter fm)
    {
        if (fm.IsEmpty) return "";

        var title = fm.Get("title");
        var subtitle = fm.Get("subtitle") ?? fm.Get("description");
        var author = fm.Get("author") ?? fm.Get("authors");
        var date = fm.Get("date");
        var tags = fm.GetList("tags") ?? fm.GetList("keywords");

        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(subtitle) &&
            string.IsNullOrWhiteSpace(author) && string.IsNullOrWhiteSpace(date) &&
            (tags is null || tags.Count == 0))
        {
            return "";
        }

        var sb = new StringBuilder();
        sb.Append("<header class=\"readmd-frontmatter\">");
        if (!string.IsNullOrWhiteSpace(title))
            sb.Append("<h1 class=\"readmd-fm-title\">").Append(Enc(title)).Append("</h1>");
        if (!string.IsNullOrWhiteSpace(subtitle))
            sb.Append("<p class=\"readmd-fm-subtitle\">").Append(Enc(subtitle)).Append("</p>");

        var meta = new List<string>();
        if (!string.IsNullOrWhiteSpace(author))
            meta.Add($"<span class=\"readmd-fm-author\">{Enc(author)}</span>");
        if (!string.IsNullOrWhiteSpace(date))
            meta.Add($"<span class=\"readmd-fm-date\">{Enc(date)}</span>");
        if (meta.Count > 0)
            sb.Append("<p class=\"readmd-fm-meta\">").Append(string.Join(" · ", meta)).Append("</p>");

        if (tags is { Count: > 0 })
        {
            sb.Append("<p class=\"readmd-fm-tags\">");
            foreach (var tag in tags)
                sb.Append("<span class=\"readmd-fm-tag\">").Append(Enc(tag)).Append("</span>");
            sb.Append("</p>");
        }

        sb.Append("</header>");
        return sb.ToString();
    }

    private static string Enc(string? s) => WebUtility.HtmlEncode(s ?? "");
}
