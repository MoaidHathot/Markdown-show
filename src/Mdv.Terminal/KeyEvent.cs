namespace Mdv.Terminal;

/// <summary>The semantic kind of a parsed input event from the console.</summary>
public enum KeyKind
{
    Char,       // a printable/letter key in <see cref="KeyEvent.Char"/>
    Enter,
    Escape,
    Backspace,
    Tab,
    Up,
    Down,
    Left,
    Right,
    Home,
    End,
    PageUp,
    PageDown,
    MouseScrollUp,
    MouseScrollDown,
    MouseClick,     // left-button press at (MouseRow, MouseCol)
}

/// <summary>
/// An input event parsed from the console. For <see cref="KeyKind.Char"/>, <see cref="Char"/>
/// holds the character and <see cref="Ctrl"/> indicates a Ctrl modifier (Ctrl+A..Z). Mouse wheel
/// events use <see cref="KeyKind.MouseScrollUp"/>/<see cref="KeyKind.MouseScrollDown"/>; a left
/// click is <see cref="KeyKind.MouseClick"/> with <see cref="MouseRow"/>/<see cref="MouseCol"/>.
/// </summary>
public sealed record KeyEvent(KeyKind Kind, char Char = '\0', bool Ctrl = false, int MouseRow = 0, int MouseCol = 0);
