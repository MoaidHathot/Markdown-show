using System.Runtime.InteropServices;

namespace Readmd.Terminal;

/// <summary>
/// Reliable cross-platform input reader. On Windows it uses the Win32 console input API
/// (<c>ReadConsoleInputW</c>) directly — the dependable way to read individual key presses and
/// mouse wheel events on Windows Terminal. On other platforms it falls back to
/// <see cref="Console.ReadKey(bool)"/> (keys only).
/// </summary>
internal static class KeyReader
{
    /// <summary>
    /// The terminal's text-area pixel size, captured from a CSI 14t report if one arrives. Null
    /// until reported. Dividing by the current rows/cols yields the per-cell pixel size.
    /// </summary>
    public static (int WidthPx, int HeightPx)? WindowPixelSize { get; private set; }

    /// <summary>Parses a swallowed CSI report; currently extracts the CSI 14t pixel-size reply.</summary>
    private static void TryParseReport(string seq)
    {
        // seq is like "\x1b[4;<height>;<width>t"
        var m = System.Text.RegularExpressions.Regex.Match(seq, @"\[4;(\d+);(\d+)t");
        if (m.Success &&
            int.TryParse(m.Groups[1].Value, out int h) &&
            int.TryParse(m.Groups[2].Value, out int w) &&
            w > 0 && h > 0)
        {
            WindowPixelSize = (w, h);
        }
    }

    public static IEnumerable<KeyEvent> Read(Func<bool> isRunning)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return ReadWindows(isRunning);
        return ReadPortable(isRunning);
    }

    // ---------------- portable (non-Windows) ----------------
    private static IEnumerable<KeyEvent> ReadPortable(Func<bool> isRunning)
    {
        while (isRunning())
        {
            ConsoleKeyInfo info;
            try { info = Console.ReadKey(intercept: true); }
            catch (InvalidOperationException) { yield break; }
            var ev = FromConsoleKeyInfo(info);
            if (ev is not null) yield return ev;
        }
    }

    private static KeyEvent? FromConsoleKeyInfo(ConsoleKeyInfo info)
    {
        bool ctrl = info.Modifiers.HasFlag(ConsoleModifiers.Control);
        switch (info.Key)
        {
            case ConsoleKey.UpArrow: return new KeyEvent(KeyKind.Up);
            case ConsoleKey.DownArrow: return new KeyEvent(KeyKind.Down);
            case ConsoleKey.LeftArrow: return new KeyEvent(KeyKind.Left);
            case ConsoleKey.RightArrow: return new KeyEvent(KeyKind.Right);
            case ConsoleKey.Home: return new KeyEvent(KeyKind.Home);
            case ConsoleKey.End: return new KeyEvent(KeyKind.End);
            case ConsoleKey.PageUp: return new KeyEvent(KeyKind.PageUp);
            case ConsoleKey.PageDown: return new KeyEvent(KeyKind.PageDown);
            case ConsoleKey.Enter: return new KeyEvent(KeyKind.Enter);
            case ConsoleKey.Escape: return new KeyEvent(KeyKind.Escape);
            case ConsoleKey.Backspace: return new KeyEvent(KeyKind.Backspace);
            case ConsoleKey.Tab: return new KeyEvent(KeyKind.Tab);
        }
        if (info.KeyChar != '\0')
            return new KeyEvent(KeyKind.Char, info.KeyChar, ctrl);
        return null;
    }

    // ---------------- Windows (ReadConsoleInputW) ----------------
    private static IEnumerable<KeyEvent> ReadWindows(Func<bool> isRunning)
    {
        var handle = GetStdHandle(STD_INPUT_HANDLE);
        _inputHandle = handle;

        // Set a complete, explicit input mode: mouse + window events on, line/echo off,
        // quick-edit off (so the mouse isn't swallowed), processed-input on (so Ctrl+C still
        // works), and VT input OFF (so keys arrive as key records, not escape bytes).
        uint savedMode = 0;
        bool modeChanged = false;
        if (GetConsoleMode(handle, out savedMode))
        {
            _savedMode = savedMode;
            modeChanged = SetConsoleMode(handle, MouseCaptureMode);
        }

        var records = new INPUT_RECORD[16];
        bool inEscape = false;          // swallowing a terminal report like CSI ... <final>
        var escBuf = new System.Text.StringBuilder();
        try
        {
            while (isRunning())
            {
                // Wait briefly so we can re-check isRunning() and exit promptly.
                uint wait = WaitForSingleObject(handle, 100);
                if (wait != 0) continue;

                if (!ReadConsoleInputW(handle, records, (uint)records.Length, out uint read) || read == 0)
                    continue;

                for (uint i = 0; i < read; i++)
                {
                    var rec = records[i];
                    if (rec.EventType == KEY_EVENT)
                    {
                        if (rec.KeyEvent.bKeyDown == 0) continue;
                        char ch = (char)rec.KeyEvent.UnicodeChar;

                        // Swallow terminal report sequences (e.g. responses to CSI 14t / cursor
                        // position) so they never leak in as keystrokes like 't' opening the TOC.
                        if (inEscape)
                        {
                            if (ch != '\0')
                            {
                                escBuf.Append(ch);
                                // A CSI/SS3 sequence ends at the first letter or '~'.
                                if ((ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z') || ch == '~')
                                {
                                    TryParseReport(escBuf.ToString());
                                    inEscape = false;
                                    escBuf.Clear();
                                }
                                else if (escBuf.Length > 32) { inEscape = false; escBuf.Clear(); }
                            }
                            continue;
                        }
                        if (ch == '\x1b')
                        {
                            // Could be a real Escape key or the start of a report sequence. If the
                            // very next char in this batch is '[' or 'O', treat as a report.
                            bool nextIsCsi = i + 1 < read && records[i + 1].EventType == KEY_EVENT &&
                                             records[i + 1].KeyEvent.bKeyDown != 0 &&
                                             (records[i + 1].KeyEvent.UnicodeChar == '[' || records[i + 1].KeyEvent.UnicodeChar == 'O');
                            if (nextIsCsi) { inEscape = true; escBuf.Clear(); escBuf.Append(ch); continue; }
                            // else fall through and emit Escape via FromKeyEventRecord.
                        }

                        var ev = FromKeyEventRecord(rec.KeyEvent);
                        if (ev is not null) yield return ev;
                    }
                    else if (rec.EventType == MOUSE_EVENT)
                    {
                        var ev = FromMouseEventRecord(rec.MouseEvent);
                        if (ev is not null) yield return ev;
                    }
                    // WINDOW_BUFFER_SIZE_EVENT / FOCUS_EVENT / MENU_EVENT: ignored.
                }
            }
        }
        finally
        {
            if (modeChanged) SetConsoleMode(handle, savedMode);
        }
    }

    private static uint _prevButtonState;

    private static KeyEvent? FromMouseEventRecord(in MOUSE_EVENT_RECORD me)
    {
        if (me.dwEventFlags == MOUSE_WHEELED)
        {
            // High word of dwButtonState is the wheel delta (signed). Positive = wheel up.
            short delta = (short)((me.dwButtonState >> 16) & 0xFFFF);
            bool ctrl = (me.dwControlKeyState & (LEFT_CTRL_PRESSED | RIGHT_CTRL_PRESSED)) != 0;
            return delta > 0
                ? new KeyEvent(KeyKind.MouseScrollUp, Ctrl: ctrl)
                : new KeyEvent(KeyKind.MouseScrollDown, Ctrl: ctrl);
        }

        bool leftNow = (me.dwButtonState & FROM_LEFT_1ST_BUTTON) != 0;
        bool leftBefore = (_prevButtonState & FROM_LEFT_1ST_BUTTON) != 0;
        bool rightNow = (me.dwButtonState & RIGHTMOST_BUTTON) != 0;
        bool rightBefore = (_prevButtonState & RIGHTMOST_BUTTON) != 0;
        int row = me.dwMousePosition.Y, colPos = me.dwMousePosition.X;
        _prevButtonState = me.dwButtonState;

        // Drag: mouse moved while the left button is held.
        if (me.dwEventFlags == MOUSE_MOVED)
        {
            if (leftNow) return new KeyEvent(KeyKind.MouseDrag, MouseRow: row, MouseCol: colPos);
            return null;
        }

        // Button press/release edges (dwEventFlags == 0) and double-clicks.
        if (me.dwEventFlags == 0 || me.dwEventFlags == DOUBLE_CLICK)
        {
            // Right-button press edge → right-click (used to copy a selection).
            if (rightNow && !rightBefore)
                return new KeyEvent(KeyKind.MouseRightClick, MouseRow: row, MouseCol: colPos);

            // Left-button down edge → a click (follows links in normal mode; starts a drag
            // selection in mark mode — the viewer decides based on its current mode).
            if (leftNow && !leftBefore)
                return new KeyEvent(KeyKind.MouseClick, MouseRow: row, MouseCol: colPos);

            // Left-button up edge → end of a drag.
            if (!leftNow && leftBefore)
                return new KeyEvent(KeyKind.MouseDragEnd, MouseRow: row, MouseCol: colPos);
        }
        return null;
    }

    private static KeyEvent? FromKeyEventRecord(in KEY_EVENT_RECORD ke)
    {
        bool ctrl = (ke.dwControlKeyState & (LEFT_CTRL_PRESSED | RIGHT_CTRL_PRESSED)) != 0;

        switch (ke.wVirtualKeyCode)
        {
            case VK_UP: return new KeyEvent(KeyKind.Up);
            case VK_DOWN: return new KeyEvent(KeyKind.Down);
            case VK_LEFT: return new KeyEvent(KeyKind.Left);
            case VK_RIGHT: return new KeyEvent(KeyKind.Right);
            case VK_HOME: return new KeyEvent(KeyKind.Home);
            case VK_END: return new KeyEvent(KeyKind.End);
            case VK_PRIOR: return new KeyEvent(KeyKind.PageUp);
            case VK_NEXT: return new KeyEvent(KeyKind.PageDown);
            case VK_RETURN: return new KeyEvent(KeyKind.Enter);
            case VK_ESCAPE: return new KeyEvent(KeyKind.Escape);
            case VK_BACK: return new KeyEvent(KeyKind.Backspace);
            case VK_TAB: return new KeyEvent(KeyKind.Tab);
            case VK_SHIFT or VK_CONTROL or VK_MENU or VK_CAPITAL
                or VK_LWIN or VK_RWIN or VK_NUMLOCK or VK_SCROLL: return null; // modifier-only
        }

        char ch = (char)ke.UnicodeChar;
        if (ctrl && ch is >= (char)1 and <= (char)26)
        {
            // Ctrl+A..Z arrive as 1..26; normalize to the letter.
            return new KeyEvent(KeyKind.Char, (char)('a' + (ch - 1)), Ctrl: true);
        }
        if (ch != '\0' && !char.IsControl(ch))
            return new KeyEvent(KeyKind.Char, ch, ctrl);
        return null;
    }

    // ---------------- Win32 interop ----------------
    private const int STD_INPUT_HANDLE = -10;
    private const ushort KEY_EVENT = 0x0001;
    private const ushort MOUSE_EVENT = 0x0002;
    private const uint MOUSE_WHEELED = 0x0004;
    private const uint MOUSE_MOVED = 0x0001;
    private const uint DOUBLE_CLICK = 0x0002;
    private const uint FROM_LEFT_1ST_BUTTON = 0x0001;
    private const uint RIGHTMOST_BUTTON = 0x0002;
    private const int LEFT_CTRL_PRESSED = 0x0008;
    private const int RIGHT_CTRL_PRESSED = 0x0004;

    private const uint ENABLE_MOUSE_INPUT = 0x0010;
    private const uint ENABLE_WINDOW_INPUT = 0x0008;
    private const uint ENABLE_EXTENDED_FLAGS = 0x0080;
    private const uint ENABLE_PROCESSED_INPUT = 0x0001;
    private const uint ENABLE_QUICK_EDIT_MODE = 0x0040;

    // Mode while we capture the mouse (for wheel scrolling): mouse on, quick-edit off.
    private const uint MouseCaptureMode = ENABLE_PROCESSED_INPUT | ENABLE_MOUSE_INPUT | ENABLE_WINDOW_INPUT | ENABLE_EXTENDED_FLAGS;
    // Mode for text selection: mouse reporting off + quick-edit on so the terminal's native
    // click-drag selection works.
    private const uint SelectionMode = ENABLE_PROCESSED_INPUT | ENABLE_WINDOW_INPUT | ENABLE_EXTENDED_FLAGS | ENABLE_QUICK_EDIT_MODE;

    private static nint _inputHandle;
    private static uint _savedMode;

    /// <summary>
    /// Switches between mouse-capture mode (wheel scrolling) and selection mode (terminal-native
    /// click-drag text selection). Only affects Windows; no-op elsewhere.
    /// </summary>
    public static void SetMouseCapture(bool capture)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
        if (_inputHandle == 0) return;
        SetConsoleMode(_inputHandle, capture ? MouseCaptureMode : SelectionMode);
    }

    private const ushort VK_BACK = 0x08, VK_TAB = 0x09, VK_RETURN = 0x0D, VK_SHIFT = 0x10,
        VK_CONTROL = 0x11, VK_MENU = 0x12, VK_CAPITAL = 0x14, VK_ESCAPE = 0x1B,
        VK_PRIOR = 0x21, VK_NEXT = 0x22, VK_END = 0x23, VK_HOME = 0x24,
        VK_LEFT = 0x25, VK_UP = 0x26, VK_RIGHT = 0x27, VK_DOWN = 0x28,
        VK_LWIN = 0x5B, VK_RWIN = 0x5C, VK_NUMLOCK = 0x90, VK_SCROLL = 0x91;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetConsoleMode(nint hConsoleHandle, out uint lpMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleMode(nint hConsoleHandle, uint dwMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(nint hHandle, uint dwMilliseconds);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool ReadConsoleInputW(nint hConsoleInput, [Out] INPUT_RECORD[] lpBuffer,
        uint nLength, out uint lpNumberOfEventsRead);

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUT_RECORD
    {
        [FieldOffset(0)] public ushort EventType;
        [FieldOffset(4)] public KEY_EVENT_RECORD KeyEvent;
        [FieldOffset(4)] public MOUSE_EVENT_RECORD MouseEvent;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEY_EVENT_RECORD
    {
        public int bKeyDown;          // Win32 BOOL (4 bytes); keep as int so the struct stays blittable
        public ushort wRepeatCount;
        public ushort wVirtualKeyCode;
        public ushort wVirtualScanCode;
        public ushort UnicodeChar;    // WCHAR
        public uint dwControlKeyState;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSE_EVENT_RECORD
    {
        public COORD dwMousePosition;
        public uint dwButtonState;
        public uint dwControlKeyState;
        public uint dwEventFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct COORD
    {
        public short X;
        public short Y;
    }
}
