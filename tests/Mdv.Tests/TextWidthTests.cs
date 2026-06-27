using Mdv.Terminal;

namespace Mdv.Tests;

public class TextWidthTests
{
    [Theory]
    [InlineData("A", 1)]
    [InlineData("abc", 3)]
    [InlineData("", 0)]
    [InlineData("✓", 1)]    // CHECK MARK is a narrow text symbol (the table-alignment bug)
    [InlineData("✗", 1)]    // BALLOT X — narrow
    [InlineData("→", 1)]    // arrows — narrow
    [InlineData("①", 1)]
    public void Narrow_text_symbols_count_as_one(string s, int expected)
    {
        Assert.Equal(expected, TextWidth.Of(s));
    }

    [Theory]
    [InlineData("日", 2)]      // CJK ideograph
    [InlineData("日本語", 6)]  // 3 CJK = 6 cells
    [InlineData("ﾊ", 1)]       // halfwidth katakana = 1
    [InlineData("Ａ", 2)]      // fullwidth Latin = 2
    [InlineData("가", 2)]      // Hangul syllable = 2
    public void East_asian_width_is_respected(string s, int expected)
    {
        Assert.Equal(expected, TextWidth.Of(s));
    }

    [Theory]
    [InlineData("🎉", 2)]     // party popper (SMP emoji) = 2
    [InlineData("🚀", 2)]     // rocket = 2
    [InlineData("⭐", 2)]      // star (default emoji presentation) = 2
    [InlineData("✔\uFE0F", 2)] // narrow symbol + VS16 → emoji presentation, wide
    public void Default_emoji_count_as_two(string s, int expected)
    {
        Assert.Equal(expected, TextWidth.Of(s));
    }

    [Fact]
    public void Combining_marks_add_zero_width()
    {
        // 'e' + combining acute accent renders in one cell.
        Assert.Equal(1, TextWidth.Of("e\u0301"));
    }

    [Fact]
    public void Table_with_check_marks_keeps_columns_aligned()
    {
        // Regression: ✓ in cells previously over-counted and shifted the borders.
        var md = "| A | B | C |\n|---|---|---|\n| x | ✓ | ✓ |";
        var lines = Render.Lines(md, width: 40);
        // The border rows and the data row should all have their '│'/junctions at the same columns.
        var top = lines.First(l => l.Contains('┌'));
        var data = lines.First(l => l.Contains("✓"));
        // Collect vertical-bar column positions on the data row and the matching positions of the
        // top border's ┬ junctions; they must coincide.
        var barCols = Positions(data, '│');
        var junctionCols = Positions(top, '┬');
        foreach (var jc in junctionCols)
            Assert.Contains(jc, barCols);
    }

    private static List<int> Positions(string s, char c)
    {
        var list = new List<int>();
        for (int i = 0; i < s.Length; i++) if (s[i] == c) list.Add(i);
        return list;
    }
}
