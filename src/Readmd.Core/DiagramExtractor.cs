using Markdig.Syntax;

namespace Readmd.Core;

/// <summary>
/// Walks the parsed AST and collects fenced code blocks whose info string names a supported
/// diagram language (<c>mermaid</c>, <c>d2</c>, <c>graphviz</c>/<c>dot</c>,
/// <c>plantuml</c>/<c>puml</c>), turning them into cacheable <see cref="DiagramRequest"/>s.
/// </summary>
public static class DiagramExtractor
{
    public static IReadOnlyList<DiagramRequest> Extract(MarkdownObject document)
    {
        var result = new List<DiagramRequest>();
        foreach (var node in Descendants(document))
        {
            if (node is FencedCodeBlock fenced)
            {
                var kind = MapKind(fenced.Info);
                if (kind is null) continue;
                var source = fenced.Lines.ToString();
                if (string.IsNullOrWhiteSpace(source)) continue;
                result.Add(DiagramRequest.Create(kind.Value, source));
            }
        }
        return result;
    }

    public static DiagramKind? MapKind(string? info)
    {
        if (string.IsNullOrWhiteSpace(info)) return null;
        return info.Trim().ToLowerInvariant() switch
        {
            "mermaid" => DiagramKind.Mermaid,
            "d2" => DiagramKind.D2,
            "graphviz" or "dot" => DiagramKind.Graphviz,
            "plantuml" or "puml" or "uml" => DiagramKind.PlantUml,
            _ => null,
        };
    }

    private static IEnumerable<MarkdownObject> Descendants(MarkdownObject root)
    {
        if (root is ContainerBlock container)
        {
            foreach (var child in container)
            {
                yield return child;
                foreach (var sub in Descendants(child)) yield return sub;
            }
        }
    }
}
