using Readmd.Terminal;

namespace Readmd.Tests;

public class InputParsingTests
{
    private static List<KeyEvent> Parse(string ansi) =>
        KeyReader.ParsePortableForTest(System.Text.Encoding.ASCII.GetBytes(ansi));

    [Fact]
    public void Arrow_keys_are_decoded()
    {
        var evs = Parse("\e[A\e[B\e[C\e[D");
        Assert.Equal(
            new[] { KeyKind.Up, KeyKind.Down, KeyKind.Right, KeyKind.Left },
            evs.Select(e => e.Kind));
    }

    [Fact]
    public void Home_end_pageup_pagedown_are_decoded()
    {
        var evs = Parse("\e[H\e[F\e[5~\e[6~");
        Assert.Equal(
            new[] { KeyKind.Home, KeyKind.End, KeyKind.PageUp, KeyKind.PageDown },
            evs.Select(e => e.Kind));
    }

    [Fact]
    public void Plain_letters_become_char_events()
    {
        var evs = Parse("jkq");
        Assert.Equal(new[] { 'j', 'k', 'q' }, evs.Select(e => e.Char));
        Assert.All(evs, e => Assert.Equal(KeyKind.Char, e.Kind));
    }

    [Fact]
    public void Ctrl_letter_is_decoded_with_modifier()
    {
        // Ctrl+D = 0x04
        var evs = Parse("\u0004");
        Assert.Single(evs);
        Assert.Equal(KeyKind.Char, evs[0].Kind);
        Assert.Equal('d', evs[0].Char);
        Assert.True(evs[0].Ctrl);
    }

    [Fact]
    public void Sgr_mouse_wheel_up_and_down()
    {
        // CSI < 64 ; col ; row M  -> wheel up;  65 -> wheel down
        var up = Parse("\e[<64;10;5M");
        var down = Parse("\e[<65;10;5M");
        Assert.Equal(KeyKind.MouseScrollUp, Assert.Single(up).Kind);
        Assert.Equal(KeyKind.MouseScrollDown, Assert.Single(down).Kind);
    }

    [Fact]
    public void Sgr_mouse_left_click_carries_zero_based_position()
    {
        // CSI < 0 ; 12 ; 7 M  -> left press at col 12,row 7 (1-based) => 11,6 (0-based)
        var evs = Parse("\e[<0;12;7M");
        var e = Assert.Single(evs);
        Assert.Equal(KeyKind.MouseClick, e.Kind);
        Assert.Equal(11, e.MouseCol);
        Assert.Equal(6, e.MouseRow);
    }

    [Fact]
    public void Sgr_mouse_right_click_is_decoded()
    {
        var evs = Parse("\e[<2;3;4M");
        Assert.Equal(KeyKind.MouseRightClick, Assert.Single(evs).Kind);
    }

    [Fact]
    public void Sgr_mouse_left_drag_and_release()
    {
        // 32 = motion + left button held (drag); 'm' final on button 0 = release.
        var drag = Parse("\e[<32;5;5M");
        var release = Parse("\e[<0;5;5m");
        Assert.Equal(KeyKind.MouseDrag, Assert.Single(drag).Kind);
        Assert.Equal(KeyKind.MouseDragEnd, Assert.Single(release).Kind);
    }

    [Fact]
    public void Bare_escape_is_an_escape_key()
    {
        // ESC followed by a normal char: ESC is its own key, the char follows.
        var evs = Parse("\eq");
        Assert.Equal(2, evs.Count);
        Assert.Equal(KeyKind.Escape, evs[0].Kind);
        Assert.Equal('q', evs[1].Char);
    }
}
