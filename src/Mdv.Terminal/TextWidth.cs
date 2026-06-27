using System.Globalization;

namespace Mdv.Terminal;

/// <summary>
/// Computes the terminal display width of text, accounting for East-Asian wide/fullwidth glyphs
/// (counted as 2 columns), zero-width combining marks/variation selectors (0), and grapheme
/// clusters such as emoji ZWJ sequences and flags (counted once at the base character's width).
/// Shared by the body word-wrapper and the table layout so widths stay consistent.
/// </summary>
internal static class TextWidth
{
    /// <summary>Display width (in terminal cells) of a whole string.</summary>
    public static int Of(string s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        int width = 0;
        var e = StringInfo.GetTextElementEnumerator(s);
        while (e.MoveNext())
            width += ElementWidth((string)e.Current);
        return width;
    }

    /// <summary>Width of a single grapheme cluster (text element).</summary>
    public static int ElementWidth(string element)
    {
        if (element.Length == 0) return 0;
        // A cluster's width is governed by its first scalar; combining marks/selectors add 0.
        int cp = char.ConvertToUtf32(element, 0);
        if (cp == 0) return 0;
        if (IsZeroWidth(cp)) return 0;
        // Emoji presentation (incl. ZWJ sequences, flags, skin tones) render as 2 cells.
        if (IsEmoji(cp)) return 2;
        return IsWide(cp) ? 2 : 1;
    }

    /// <summary>Enumerates grapheme clusters of a string (so callers can wrap without splitting emoji).</summary>
    public static IEnumerable<string> Graphemes(string s)
    {
        var e = StringInfo.GetTextElementEnumerator(s);
        while (e.MoveNext()) yield return (string)e.Current;
    }

    private static bool IsZeroWidth(int cp) =>
        cp == 0x200B ||                              // zero-width space
        cp == 0x200D ||                              // zero-width joiner
        (cp >= 0x0300 && cp <= 0x036F) ||            // combining diacritical marks
        (cp >= 0x1AB0 && cp <= 0x1AFF) ||
        (cp >= 0x1DC0 && cp <= 0x1DFF) ||
        (cp >= 0x20D0 && cp <= 0x20FF) ||            // combining marks for symbols
        (cp >= 0xFE00 && cp <= 0xFE0F) ||            // variation selectors
        (cp >= 0xFE20 && cp <= 0xFE2F);              // combining half marks

    private static bool IsEmoji(int cp) =>
        (cp >= 0x1F300 && cp <= 0x1FAFF) ||          // misc symbols & pictographs … symbols-extended-A
        (cp >= 0x1F000 && cp <= 0x1F0FF) ||          // mahjong/dominoes/cards
        (cp >= 0x2600 && cp <= 0x27BF) ||            // misc symbols + dingbats
        (cp >= 0x1F1E6 && cp <= 0x1F1FF) ||          // regional indicators (flags)
        cp == 0x2B50 || cp == 0x2B55 ||
        cp == 0x231A || cp == 0x231B ||              // ⌚ ⌛ (emoji-presented technical symbols)
        cp == 0x23E9 || cp == 0x23EA || cp == 0x23F0 || cp == 0x23F3;

    private static bool IsWide(int cp) =>
        (cp >= 0x1100 && cp <= 0x115F) ||            // Hangul Jamo
        cp == 0x2329 || cp == 0x232A ||
        (cp >= 0x2E80 && cp <= 0x303E) ||            // CJK radicals … Kangxi
        (cp >= 0x3041 && cp <= 0x33FF) ||            // Hiragana … CJK compatibility
        (cp >= 0x3400 && cp <= 0x4DBF) ||            // CJK Ext A
        (cp >= 0x4E00 && cp <= 0x9FFF) ||            // CJK Unified
        (cp >= 0xA000 && cp <= 0xA4CF) ||            // Yi
        (cp >= 0xAC00 && cp <= 0xD7A3) ||            // Hangul Syllables
        (cp >= 0xF900 && cp <= 0xFAFF) ||            // CJK Compatibility Ideographs
        (cp >= 0xFE30 && cp <= 0xFE4F) ||            // CJK Compatibility Forms
        (cp >= 0xFF00 && cp <= 0xFF60) ||            // Fullwidth Forms
        (cp >= 0xFFE0 && cp <= 0xFFE6) ||
        (cp >= 0x20000 && cp <= 0x3FFFD);            // CJK Ext B+ (supplementary ideographic planes)
}
