namespace Mdv.Terminal;

/// <summary>Color palette for terminal rendering, in light and dark variants.</summary>
public sealed class TerminalTheme
{
    public required Rgb Text { get; init; }
    public required Rgb Muted { get; init; }
    public required Rgb Heading { get; init; }
    public required Rgb Link { get; init; }
    public required Rgb Code { get; init; }
    public required Rgb Quote { get; init; }
    public required Rgb Accent { get; init; }
    public required Rgb Rule { get; init; }
    public required Rgb SearchBg { get; init; }
    public required Rgb SearchActiveBg { get; init; }

    /// <summary>The document background. Used as a solid fill when <c>SolidBackground</c> is on.</summary>
    public required Rgb Background { get; init; }

    /// <summary>Slightly elevated background for panels/status/overlays.</summary>
    public required Rgb BackgroundElevated { get; init; }

    /// <summary>Background fill for fenced/indented code blocks.</summary>
    public required Rgb CodeBackground { get; init; }

    /// <summary>Left gutter bar color for code blocks.</summary>
    public required Rgb CodeBorder { get; init; }

    /// <summary>Distinct accent for H1 banners.</summary>
    public required Rgb H1 { get; init; }
    /// <summary>Distinct accent for H2 headings.</summary>
    public required Rgb H2 { get; init; }
    /// <summary>Distinct accent for H3 headings.</summary>
    public required Rgb H3 { get; init; }

    public bool IsDark { get; init; }

    public static TerminalTheme Dark { get; } = new()
    {
        IsDark = true,
        Text = Rgb.FromHex("#e6edf3"),
        Muted = Rgb.FromHex("#9da7b3"),
        Heading = Rgb.FromHex("#58a6ff"),
        Link = Rgb.FromHex("#6cb6ff"),
        Code = Rgb.FromHex("#ffa657"),
        Quote = Rgb.FromHex("#8b949e"),
        Accent = Rgb.FromHex("#a371f7"),
        Rule = Rgb.FromHex("#30363d"),
        SearchBg = Rgb.FromHex("#9e6a03"),
        SearchActiveBg = Rgb.FromHex("#bb8009"),
        Background = Rgb.FromHex("#0d1117"),
        BackgroundElevated = Rgb.FromHex("#161b22"),
        CodeBackground = Rgb.FromHex("#1b2230"),
        CodeBorder = Rgb.FromHex("#3d4757"),
        H1 = Rgb.FromHex("#d2a8ff"),
        H2 = Rgb.FromHex("#79c0ff"),
        H3 = Rgb.FromHex("#56d364"),
    };

    public static TerminalTheme Light { get; } = new()
    {
        IsDark = false,
        Text = Rgb.FromHex("#1f2328"),
        Muted = Rgb.FromHex("#636c76"),
        Heading = Rgb.FromHex("#0969da"),
        Link = Rgb.FromHex("#0969da"),
        Code = Rgb.FromHex("#953800"),
        Quote = Rgb.FromHex("#636c76"),
        Accent = Rgb.FromHex("#8250df"),
        Rule = Rgb.FromHex("#d0d7de"),
        SearchBg = Rgb.FromHex("#fae17d"),
        SearchActiveBg = Rgb.FromHex("#fbca04"),
        Background = Rgb.FromHex("#ffffff"),
        BackgroundElevated = Rgb.FromHex("#f6f8fa"),
        CodeBackground = Rgb.FromHex("#f0f2f5"),
        CodeBorder = Rgb.FromHex("#d0d7de"),
        H1 = Rgb.FromHex("#8250df"),
        H2 = Rgb.FromHex("#0550ae"),
        H3 = Rgb.FromHex("#1a7f37"),
    };

    public static TerminalTheme For(bool dark) => dark ? Dark : Light;
}
