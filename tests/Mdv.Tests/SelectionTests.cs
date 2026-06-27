using Mdv.Terminal;

namespace Mdv.Tests;

public class SelectionTests
{
    private static readonly string[] Doc =
    {
        "The quick brown fox",
        "jumps over the lazy dog",
        "and keeps on running",
    };

    [Fact]
    public void Single_line_selection_returns_substring()
    {
        // Select "quick" on line 0 (cols 4..9).
        var text = TerminalViewer.SelectTextForTest(Doc, (0, 4), (0, 9));
        Assert.Equal("quick", text);
    }

    [Fact]
    public void Selection_is_direction_independent()
    {
        var forward = TerminalViewer.SelectTextForTest(Doc, (0, 4), (0, 9));
        var backward = TerminalViewer.SelectTextForTest(Doc, (0, 9), (0, 4));
        Assert.Equal(forward, backward);
    }

    [Fact]
    public void Multi_line_selection_joins_with_newlines()
    {
        // From line 0 col 10 ("brown fox") through line 1 col 5 ("jumps").
        var text = TerminalViewer.SelectTextForTest(Doc, (0, 10), (1, 5));
        Assert.Equal("brown fox\njumps", text);
    }

    [Fact]
    public void Full_middle_line_is_included()
    {
        var text = TerminalViewer.SelectTextForTest(Doc, (0, 16), (2, 3));
        Assert.Equal("fox\njumps over the lazy dog\nand", text);
    }

    [Fact]
    public void Empty_selection_returns_empty()
    {
        var text = TerminalViewer.SelectTextForTest(Doc, (1, 6), (1, 6));
        Assert.Equal("", text);
    }

    [Fact]
    public void Selection_past_line_end_clamps()
    {
        // End column beyond the line length should clamp to the text end.
        var text = TerminalViewer.SelectTextForTest(Doc, (2, 0), (2, 999));
        Assert.Equal("and keeps on running", text);
    }
}
