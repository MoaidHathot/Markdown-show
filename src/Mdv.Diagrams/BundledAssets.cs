using System.Reflection;

namespace Mdv.Diagrams;

/// <summary>Loads bundled JS assets (e.g. mermaid) embedded in this assembly.</summary>
internal static class BundledAssets
{
    private static string? _mermaidJs;

    public static string MermaidJs => _mermaidJs ??= ReadResource("mermaid.min.js");

    private static string ReadResource(string fileName)
    {
        var asm = Assembly.GetExecutingAssembly();
        var name = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(fileName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Embedded asset '{fileName}' not found.");
        using var stream = asm.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
