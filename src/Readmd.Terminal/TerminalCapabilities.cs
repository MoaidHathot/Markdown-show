using System.Runtime.InteropServices;

namespace Readmd.Terminal;

/// <summary>How inline images/diagrams are drawn in the terminal.</summary>
public enum GraphicsMode
{
    /// <summary>Sixel graphics (best quality) — for terminals that support it.</summary>
    Sixel,
    /// <summary>Unicode half-block (▀) cells with truecolor — works on any truecolor terminal.</summary>
    HalfBlock,
    /// <summary>No inline images; diagrams/images show only their caption line.</summary>
    None,
}

/// <summary>
/// Decides how to draw inline images/diagrams. Sixel support can't be detected with certainty
/// without querying the terminal, so this uses well-known environment signals plus an explicit
/// override (<c>READMD_GRAPHICS=sixel|half-block|none|auto</c> or config <c>graphics</c>). The
/// half-block fallback works on essentially any truecolor terminal, widening where diagrams render.
/// </summary>
public static class TerminalCapabilities
{
    /// <summary>
    /// Resolves the graphics mode. <paramref name="configured"/> is the config value (if any);
    /// the <c>READMD_GRAPHICS</c> environment variable takes precedence over it; "auto"/null fall
    /// back to detection.
    /// </summary>
    public static GraphicsMode Resolve(string? configured)
    {
        var env = Environment.GetEnvironmentVariable("READMD_GRAPHICS");
        var choice = !string.IsNullOrWhiteSpace(env) ? env : configured;
        switch (choice?.Trim().ToLowerInvariant())
        {
            case "sixel": return GraphicsMode.Sixel;
            case "half-block" or "halfblock" or "blocks" or "block": return GraphicsMode.HalfBlock;
            case "none" or "off" or "text": return GraphicsMode.None;
            case "auto" or "" or null: break;
            default: break;
        }
        return Detect();
    }

    /// <summary>Best-effort detection from environment markers; defaults to half-block when unsure.</summary>
    public static GraphicsMode Detect()
    {
        var term = Environment.GetEnvironmentVariable("TERM") ?? "";
        var termProgram = Environment.GetEnvironmentVariable("TERM_PROGRAM") ?? "";

        // Known Sixel-capable terminals (by env marker).
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WT_SESSION"))) return GraphicsMode.Sixel; // Windows Terminal ≥ 1.22
        if (term.Contains("sixel", StringComparison.OrdinalIgnoreCase)) return GraphicsMode.Sixel;
        if (term.Contains("foot", StringComparison.OrdinalIgnoreCase)) return GraphicsMode.Sixel;
        if (term.Contains("contour", StringComparison.OrdinalIgnoreCase)) return GraphicsMode.Sixel;
        if (termProgram.Contains("WezTerm", StringComparison.OrdinalIgnoreCase)) return GraphicsMode.Sixel;
        if (termProgram.Contains("mintty", StringComparison.OrdinalIgnoreCase)) return GraphicsMode.Sixel;
        if (term is "xterm-256color" && OperatingSystem.IsWindows()) return GraphicsMode.Sixel;

        // kitty/iTerm2 use their own graphics protocols (not Sixel) — fall back to half-block, which
        // both render fine. Anything else also gets the universally-supported half-block path.
        return GraphicsMode.HalfBlock;
    }
}
