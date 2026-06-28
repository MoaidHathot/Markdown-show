using System.Text;

namespace Readmd.Terminal;

/// <summary>
/// Best-effort LaTeX → Unicode renderer for terminal math. It converts the common subset of LaTeX
/// (Greek letters, sub/superscripts, fractions, roots, common operators) into readable Unicode.
/// This is not a full typesetting engine — complex expressions degrade gracefully to a cleaned-up
/// approximation rather than raw LaTeX source.
/// </summary>
public static class MathRenderer
{
    public static string ToUnicode(string latex)
    {
        if (string.IsNullOrWhiteSpace(latex)) return latex;
        var s = latex.Trim();

        s = ReplaceCommands(s);
        s = ReplaceFractions(s);
        s = ReplaceSqrt(s);
        s = ReplaceScripts(s);
        s = Cleanup(s);
        return s;
    }

    /// <summary>
    /// Converts display math to one or more output lines, laying out LaTeX environments
    /// (<c>matrix</c>/<c>pmatrix</c>/<c>bmatrix</c>/<c>aligned</c>/<c>align</c>/<c>cases</c>/<c>array</c>)
    /// as a simple aligned grid: rows split on <c>\\</c>, columns split on <c>&amp;</c>. Without an
    /// environment it returns the single converted line.
    /// </summary>
    public static List<string> ToUnicodeBlock(string latex)
    {
        if (string.IsNullOrWhiteSpace(latex)) return [latex];
        var s = latex.Trim();

        var env = System.Text.RegularExpressions.Regex.Match(s,
            @"\\begin\{(?<n>pmatrix|bmatrix|Bmatrix|vmatrix|Vmatrix|matrix|aligned|align\*?|cases|array)\}(?<body>[\s\S]*?)\\end\{\k<n>\}");
        if (!env.Success)
            return ToUnicode(s).Split('\n').ToList();

        string name = env.Groups["n"].Value;
        string body = env.Groups["body"].Value;

        // array/aligned may carry a column spec like {cc} right after \begin{array}; drop it.
        body = System.Text.RegularExpressions.Regex.Replace(body, @"^\s*\{[lcr|]*\}", "");

        // Split into rows (on \\) then columns (on unescaped &), convert each cell.
        var rows = System.Text.RegularExpressions.Regex.Split(body, @"\\\\")
            .Select(r => r.Trim())
            .Where(r => r.Length > 0)
            .Select(r => System.Text.RegularExpressions.Regex.Split(r, @"(?<!\\)&")
                .Select(c => ToUnicode(c.Trim())).ToArray())
            .ToList();
        if (rows.Count == 0) return [ToUnicode(s)];

        int cols = rows.Max(r => r.Length);
        var widths = new int[cols];
        foreach (var r in rows)
            for (int c = 0; c < r.Length; c++)
                widths[c] = Math.Max(widths[c], r[c].Length);

        (string l, string r) brackets = name switch
        {
            "pmatrix" => ("(", ")"),
            "bmatrix" or "array" => ("[", "]"),
            "Bmatrix" => ("{", "}"),
            "vmatrix" => ("|", "|"),
            "Vmatrix" => ("‖", "‖"),
            "cases" => ("{", " "),
            _ => ("", ""),
        };

        var lines = new List<string>();
        for (int i = 0; i < rows.Count; i++)
        {
            var cells = rows[i];
            var padded = new List<string>();
            for (int c = 0; c < cols; c++)
                padded.Add((c < cells.Length ? cells[c] : "").PadRight(widths[c]));
            string joined = string.Join("  ", padded).TrimEnd();
            // Vertical bracket pieces: ⎡ ⎢ ⎣ etc. would be ideal, but plain brackets on the middle
            // row read clearly in a terminal; use them on the first/only and last rows.
            string left = brackets.l.Length == 0 ? "" : (rows.Count == 1 ? brackets.l : (i == 0 ? brackets.l : (i == rows.Count - 1 ? brackets.l : " ")));
            string right = brackets.r.Length == 0 ? "" : (rows.Count == 1 ? brackets.r : (i == 0 ? brackets.r : (i == rows.Count - 1 ? brackets.r : " ")));
            lines.Add($"{left} {joined} {right}".Trim());
        }
        return lines;
    }

    private static readonly Dictionary<string, string> Symbols = new()
    {
        // Greek (lower)
        ["\\alpha"] = "α", ["\\beta"] = "β", ["\\gamma"] = "γ", ["\\delta"] = "δ",
        ["\\epsilon"] = "ε", ["\\varepsilon"] = "ε", ["\\zeta"] = "ζ", ["\\eta"] = "η",
        ["\\theta"] = "θ", ["\\vartheta"] = "ϑ", ["\\iota"] = "ι", ["\\kappa"] = "κ",
        ["\\lambda"] = "λ", ["\\mu"] = "μ", ["\\nu"] = "ν", ["\\xi"] = "ξ", ["\\pi"] = "π",
        ["\\varpi"] = "ϖ", ["\\rho"] = "ρ", ["\\sigma"] = "σ", ["\\tau"] = "τ",
        ["\\upsilon"] = "υ", ["\\phi"] = "φ", ["\\varphi"] = "φ", ["\\chi"] = "χ",
        ["\\psi"] = "ψ", ["\\omega"] = "ω",
        // Greek (upper)
        ["\\Gamma"] = "Γ", ["\\Delta"] = "Δ", ["\\Theta"] = "Θ", ["\\Lambda"] = "Λ",
        ["\\Xi"] = "Ξ", ["\\Pi"] = "Π", ["\\Sigma"] = "Σ", ["\\Upsilon"] = "Υ",
        ["\\Phi"] = "Φ", ["\\Psi"] = "Ψ", ["\\Omega"] = "Ω",
        // Operators / relations
        ["\\times"] = "×", ["\\div"] = "÷", ["\\pm"] = "±", ["\\mp"] = "∓",
        ["\\cdot"] = "·", ["\\ast"] = "∗", ["\\star"] = "⋆",
        ["\\leq"] = "≤", ["\\le"] = "≤", ["\\geq"] = "≥", ["\\ge"] = "≥",
        ["\\neq"] = "≠", ["\\ne"] = "≠", ["\\approx"] = "≈", ["\\equiv"] = "≡",
        ["\\sim"] = "∼", ["\\simeq"] = "≃", ["\\cong"] = "≅", ["\\propto"] = "∝",
        ["\\ll"] = "≪", ["\\gg"] = "≫",
        ["\\in"] = "∈", ["\\notin"] = "∉", ["\\ni"] = "∋", ["\\subset"] = "⊂",
        ["\\subseteq"] = "⊆", ["\\supset"] = "⊃", ["\\supseteq"] = "⊇",
        ["\\cup"] = "∪", ["\\cap"] = "∩", ["\\emptyset"] = "∅", ["\\varnothing"] = "∅",
        ["\\forall"] = "∀", ["\\exists"] = "∃", ["\\nexists"] = "∄",
        ["\\neg"] = "¬", ["\\land"] = "∧", ["\\lor"] = "∨",
        ["\\Rightarrow"] = "⇒", ["\\Leftarrow"] = "⇐", ["\\Leftrightarrow"] = "⇔",
        ["\\rightarrow"] = "→", ["\\to"] = "→", ["\\leftarrow"] = "←", ["\\leftrightarrow"] = "↔",
        ["\\mapsto"] = "↦", ["\\implies"] = "⟹", ["\\iff"] = "⟺",
        ["\\infty"] = "∞", ["\\partial"] = "∂", ["\\nabla"] = "∇",
        ["\\sum"] = "∑", ["\\prod"] = "∏", ["\\coprod"] = "∐", ["\\int"] = "∫",
        ["\\oint"] = "∮", ["\\iint"] = "∬", ["\\iiint"] = "∭",
        ["\\sqrt"] = "√", ["\\angle"] = "∠", ["\\perp"] = "⊥", ["\\parallel"] = "∥",
        ["\\degree"] = "°", ["\\circ"] = "∘", ["\\bullet"] = "•",
        ["\\dots"] = "…", ["\\ldots"] = "…", ["\\cdots"] = "⋯", ["\\vdots"] = "⋮", ["\\ddots"] = "⋱",
        ["\\aleph"] = "ℵ", ["\\hbar"] = "ℏ", ["\\ell"] = "ℓ", ["\\Re"] = "ℜ", ["\\Im"] = "ℑ",
        ["\\wp"] = "℘", ["\\otimes"] = "⊗", ["\\oplus"] = "⊕", ["\\odot"] = "⊙",
        ["\\langle"] = "⟨", ["\\rangle"] = "⟩", ["\\lceil"] = "⌈", ["\\rceil"] = "⌉",
        ["\\lfloor"] = "⌊", ["\\rfloor"] = "⌋", ["\\mid"] = "∣", ["\\setminus"] = "∖",
        ["\\quad"] = "  ", ["\\qquad"] = "    ", ["\\,"] = " ", ["\\;"] = " ", ["\\:"] = " ", ["\\!"] = "",
        ["\\left"] = "", ["\\right"] = "", ["\\displaystyle"] = "", ["\\textstyle"] = "",
        ["\\mathbb{R}"] = "ℝ", ["\\mathbb{N}"] = "ℕ", ["\\mathbb{Z}"] = "ℤ",
        ["\\mathbb{Q}"] = "ℚ", ["\\mathbb{C}"] = "ℂ", ["\\mathbb{P}"] = "ℙ",
    };

    private static string ReplaceCommands(string s)
    {
        // Replace longer commands first to avoid partial matches.
        foreach (var pair in Symbols.OrderByDescending(p => p.Key.Length))
            s = s.Replace(pair.Key, pair.Value);
        // \operatorname / \text / \mathrm{...} -> contents
        s = StripWrapper(s, "\\text");
        s = StripWrapper(s, "\\mathrm");
        s = StripWrapper(s, "\\mathbf");
        s = StripWrapper(s, "\\mathit");
        s = StripWrapper(s, "\\operatorname");
        return s;
    }

    private static string StripWrapper(string s, string cmd)
    {
        int idx;
        while ((idx = s.IndexOf(cmd + "{", StringComparison.Ordinal)) >= 0)
        {
            int open = idx + cmd.Length;
            int close = MatchBrace(s, open);
            if (close < 0) break;
            var inner = s.Substring(open + 1, close - open - 1);
            s = s[..idx] + inner + s[(close + 1)..];
        }
        return s;
    }

    private static string ReplaceFractions(string s)
    {
        int idx;
        while ((idx = s.IndexOf("\\frac{", StringComparison.Ordinal)) >= 0)
        {
            int n1 = idx + 5; // points at first '{'
            int c1 = MatchBrace(s, n1);
            if (c1 < 0 || c1 + 1 >= s.Length || s[c1 + 1] != '{') break;
            int n2 = c1 + 1;
            int c2 = MatchBrace(s, n2);
            if (c2 < 0) break;
            var num = s.Substring(n1 + 1, c1 - n1 - 1);
            var den = s.Substring(n2 + 1, c2 - n2 - 1);
            s = s[..idx] + "(" + num + ")/(" + den + ")" + s[(c2 + 1)..];
        }
        return s;
    }

    private static string ReplaceSqrt(string s)
    {
        int idx;
        while ((idx = s.IndexOf("√{", StringComparison.Ordinal)) >= 0)
        {
            int open = idx + 1;
            int close = MatchBrace(s, open);
            if (close < 0) break;
            var inner = s.Substring(open + 1, close - open - 1);
            s = s[..idx] + "√(" + inner + ")" + s[(close + 1)..];
        }
        return s;
    }

    private const string SuperDigits = "⁰¹²³⁴⁵⁶⁷⁸⁹";
    private const string SubDigits = "₀₁₂₃₄₅₆₇₈₉";
    private static readonly Dictionary<char, char> SuperMap = new()
    {
        ['+'] = '⁺', ['-'] = '⁻', ['='] = '⁼', ['('] = '⁽', [')'] = '⁾', ['n'] = 'ⁿ', ['i'] = 'ⁱ',
        ['a'] = 'ᵃ', ['b'] = 'ᵇ', ['c'] = 'ᶜ', ['d'] = 'ᵈ', ['e'] = 'ᵉ', ['x'] = 'ˣ', ['y'] = 'ʸ',
    };
    private static readonly Dictionary<char, char> SubMap = new()
    {
        ['+'] = '₊', ['-'] = '₋', ['='] = '₌', ['('] = '₍', [')'] = '₎',
        ['a'] = 'ₐ', ['e'] = 'ₑ', ['i'] = 'ᵢ', ['j'] = 'ⱼ', ['o'] = 'ₒ', ['x'] = 'ₓ', ['n'] = 'ₙ',
    };

    /// <summary>
    /// Converts a short run of text to Unicode super/subscript glyphs. Digits and the mapped letters
    /// convert directly; if any character has no super/subscript form, the whole run falls back to a
    /// readable bracketed notation (<c>^(...)</c> / <c>_(...)</c>) so nothing is lost.
    /// </summary>
    public static string ToScript(string text, bool superscript)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var map = superscript ? SuperMap : SubMap;
        var digits = superscript ? SuperDigits : SubDigits;
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (ch >= '0' && ch <= '9') sb.Append(digits[ch - '0']);
            else if (map.TryGetValue(char.ToLowerInvariant(ch), out var m)) sb.Append(m);
            else return (superscript ? "^(" : "_(") + text + ")";   // unmappable → bracketed fallback
        }
        return sb.ToString();
    }

    private static string ReplaceScripts(string s)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if ((c == '^' || c == '_') && i + 1 < s.Length)
            {
                bool sup = c == '^';
                string content;
                int next = i + 1;
                if (s[next] == '{')
                {
                    int close = MatchBrace(s, next);
                    if (close < 0) { sb.Append(c); continue; }
                    content = s.Substring(next + 1, close - next - 1);
                    i = close;
                }
                else
                {
                    content = s[next].ToString();
                    i = next;
                }
                var converted = ConvertScript(content, sup);
                if (converted is null)
                    sb.Append(sup ? "^" : "_").Append(content.Length > 1 ? "(" + content + ")" : content);
                else
                    sb.Append(converted);
            }
            else sb.Append(c);
        }
        return sb.ToString();
    }

    private static string? ConvertScript(string content, bool sup)
    {
        var sb = new StringBuilder();
        foreach (var ch in content)
        {
            if (char.IsDigit(ch)) sb.Append((sup ? SuperDigits : SubDigits)[ch - '0']);
            else if ((sup ? SuperMap : SubMap).TryGetValue(char.ToLowerInvariant(ch), out var m)) sb.Append(m);
            else return null; // can't fully convert -> caller falls back
        }
        return sb.ToString();
    }

    private static string Cleanup(string s)
    {
        s = s.Replace("\\{", "{").Replace("\\}", "}").Replace("\\%", "%").Replace("\\$", "$")
             .Replace("\\&", "&").Replace("\\#", "#").Replace("\\_", "_");
        // Collapse leftover braces from simple groups.
        s = s.Replace("{", "").Replace("}", "");
        // Squeeze excess spaces.
        while (s.Contains("  ")) s = s.Replace("  ", " ");
        return s.Trim();
    }

    private static int MatchBrace(string s, int openIndex)
    {
        if (openIndex >= s.Length || s[openIndex] != '{') return -1;
        int depth = 0;
        for (int i = openIndex; i < s.Length; i++)
        {
            if (s[i] == '{') depth++;
            else if (s[i] == '}') { depth--; if (depth == 0) return i; }
        }
        return -1;
    }
}
