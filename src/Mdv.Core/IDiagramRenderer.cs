namespace Mdv.Core;

public enum DiagramStatus
{
    Pending,
    Ready,
    Failed,
}

/// <summary>The rendered output of a diagram, in whichever formats were produced.</summary>
public sealed record DiagramResult(
    string Key,
    DiagramStatus Status,
    byte[]? Png,
    string? Svg,
    int PixelWidth,
    int PixelHeight,
    string? Error)
{
    public static DiagramResult Pending(string key) =>
        new(key, DiagramStatus.Pending, null, null, 0, 0, null);

    public static DiagramResult Fail(string key, string error) =>
        new(key, DiagramStatus.Failed, null, null, 0, 0, error);
}

/// <summary>
/// Renders diagrams to images/SVG. Implementations cache by <see cref="DiagramRequest.Key"/>
/// so unchanged diagrams are produced once and reused across live-reloads and both front-ends.
/// </summary>
public interface IDiagramRenderer : IAsyncDisposable
{
    /// <summary>Returns a cached result immediately if present, otherwise null.</summary>
    DiagramResult? TryGet(string key);

    /// <summary>
    /// Renders (or returns cached) output for the request. <paramref name="theme"/> selects a
    /// light/dark palette. Safe to call repeatedly with the same key.
    /// </summary>
    Task<DiagramResult> RenderAsync(DiagramRequest request, DiagramTheme theme, CancellationToken ct = default);
}

public enum DiagramTheme
{
    Light,
    Dark,
}
