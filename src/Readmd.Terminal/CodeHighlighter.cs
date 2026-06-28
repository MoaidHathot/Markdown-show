using TextMateSharp.Grammars;
using TextMateSharp.Registry;
using TextMateSharp.Themes;

namespace Readmd.Terminal;

/// <summary>
/// Syntax-highlights code blocks using TextMateSharp grammars. Falls back to a single code-color
/// run if the language is unknown or highlighting fails. Registry/grammars are cached per process.
/// </summary>
public static class CodeHighlighter
{
    private static readonly object Sync = new();
    private static RegistryOptions? _options;
    private static Registry? _registry;          // dark (DarkPlus) — also used for grammar lookup
    private static Registry? _lightRegistry;     // light (LightPlus) — for color resolution on light themes
    private static readonly Dictionary<string, IGrammar?> GrammarCache = new(StringComparer.OrdinalIgnoreCase);

    public static List<List<StyledSpan>> Highlight(string code, string language, TerminalTheme theme)
    {
        var lines = code.Replace("\r\n", "\n").Split('\n');
        try
        {
            var grammar = GetGrammar(language);
            if (grammar is null) return Fallback(lines, theme);

            var result = new List<List<StyledSpan>>(lines.Length);
            IStateStack? ruleStack = null;
            foreach (var line in lines)
            {
                var tokenizeResult = grammar.TokenizeLine(line, ruleStack, TimeSpan.FromMilliseconds(200));
                ruleStack = tokenizeResult.RuleStack;
                var spans = new List<StyledSpan>();
                foreach (var token in tokenizeResult.Tokens)
                {
                    int start = Math.Min(token.StartIndex, line.Length);
                    int end = Math.Min(token.EndIndex, line.Length);
                    if (end <= start) continue;
                    var text = line[start..end];
                    var color = ResolveColor(token.Scopes, theme) ?? theme.Text;
                    spans.Add(new StyledSpan(text, color));
                }
                if (spans.Count == 0) spans.Add(new StyledSpan(line.Length == 0 ? "" : line, theme.Text));
                result.Add(spans);
            }
            return result;
        }
        catch
        {
            return Fallback(lines, theme);
        }
    }

    private static List<List<StyledSpan>> Fallback(string[] lines, TerminalTheme theme) =>
        lines.Select(l => new List<StyledSpan> { new(l, theme.Code) }).ToList();

    private static Rgb? ResolveColor(List<string> scopes, TerminalTheme theme)
    {
        EnsureInit();
        // Use the theme that matches the terminal background so code stays legible: DarkPlus colors
        // are designed for dark backgrounds and wash out on light, and vice versa.
        var registry = theme.IsDark ? _registry : (_lightRegistry ?? _registry);
        if (registry is null) return null;

        var themeManager = registry.GetTheme();
        var rules = themeManager.Match(scopes);
        if (rules is null) return null;
        foreach (var rule in rules)
        {
            if (rule.foreground > 0)
            {
                var hex = themeManager.GetColor(rule.foreground);
                if (!string.IsNullOrEmpty(hex)) return Rgb.FromHex(hex);
            }
        }
        return null;
    }

    private static IGrammar? GetGrammar(string language)
    {
        EnsureInit();
        if (_options is null || _registry is null) return null;
        language = NormalizeLanguage(language);
        if (string.IsNullOrEmpty(language)) return null;

        lock (Sync)
        {
            if (GrammarCache.TryGetValue(language, out var cached)) return cached;
            IGrammar? grammar = null;
            try
            {
                var lang = _options.GetLanguageByExtension("." + language)
                           ?? _options.GetAvailableLanguages()
                               .FirstOrDefault(l => l.Id.Equals(language, StringComparison.OrdinalIgnoreCase)
                                   || (l.Aliases?.Contains(language, StringComparer.OrdinalIgnoreCase) ?? false));
                if (lang is not null)
                {
                    var scope = _options.GetScopeByLanguageId(lang.Id);
                    if (scope is not null) grammar = _registry.LoadGrammar(scope);
                }
            }
            catch { grammar = null; }
            GrammarCache[language] = grammar;
            return grammar;
        }
    }

    private static string NormalizeLanguage(string language)
    {
        language = language.Trim().ToLowerInvariant();
        return language switch
        {
            "c#" or "csharp" => "cs",
            "js" or "javascript" => "js",
            "ts" or "typescript" => "ts",
            "py" or "python" => "py",
            "sh" or "bash" or "shell" or "zsh" => "sh",
            "yml" or "yaml" => "yaml",
            "" => "",
            _ => language,
        };
    }

    private static void EnsureInit()
    {
        if (_registry is not null) return;
        lock (Sync)
        {
            if (_registry is not null) return;
            var options = new RegistryOptions(ThemeName.DarkPlus);
            _registry = new Registry(options);
            _options = options;
            // A second registry with the light theme for color resolution on light terminals.
            try { _lightRegistry = new Registry(new RegistryOptions(ThemeName.LightPlus)); }
            catch { _lightRegistry = null; }
        }
    }
}
