using System.Reflection;

namespace Readmd.Web;

/// <summary>Serves the embedded front-end assets (shell, css, js, vendor libs, fonts).</summary>
internal static class WebAssets
{
    private static readonly Assembly Asm = typeof(WebAssets).Assembly;
    private const string Prefix = "Readmd.Web.assets.";

    public static string ReadText(string relativePath) =>
        new StreamReader(OpenRequired(relativePath)).ReadToEnd();

    public static byte[]? TryReadBytes(string relativePath)
    {
        using var stream = Open(relativePath);
        if (stream is null) return null;
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    private static Stream OpenRequired(string relativePath) =>
        Open(relativePath) ?? throw new FileNotFoundException($"Embedded asset not found: {relativePath}");

    private static Stream? Open(string relativePath)
    {
        // assets/app.js  ->  Readmd.Web.assets.app.js  (folders become dots)
        var resourceName = Prefix + relativePath.Replace('/', '.').Replace('\\', '.');
        return Asm.GetManifestResourceStream(resourceName);
    }

    public static string ContentType(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".js" => "text/javascript; charset=utf-8",
            ".mjs" => "text/javascript; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".html" => "text/html; charset=utf-8",
            ".woff2" => "font/woff2",
            ".woff" => "font/woff",
            ".ttf" => "font/ttf",
            ".svg" => "image/svg+xml",
            ".png" => "image/png",
            ".json" => "application/json; charset=utf-8",
            _ => "application/octet-stream",
        };
    }
}
