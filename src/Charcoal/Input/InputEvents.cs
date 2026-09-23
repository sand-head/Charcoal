using System.Text;

namespace Charcoal.Input;

/// <summary>Modifier keys, valued as in xterm's <c>1 + shift + alt + ctrl</c> parameter.</summary>
[Flags]
public enum KeyModifiers
{
    None = 0,
    Shift = 1,
    Alt = 2,
    Ctrl = 4,
}

/// <summary>
/// A printable key is its codepoint, with letters in uppercase. Special keys
/// are numbered above U+10FFFF so they never collide with a codepoint.
/// </summary>
public enum Key : uint
{
    None = 0,

    Backspace = 0x08,
    Tab = 0x09,
    Enter = 0x0D,
    Escape = 0x1B,
    Space = 0x20,

    Up = 0x110001,
    Down,
    Left,
    Right,
    Home,
    End,
    PageUp,
    PageDown,
    Insert,
    Delete,
    F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,
    F13, F14, F15, F16, F17, F18, F19, F20,
}

/// <summary>A parsed item of terminal input.</summary>
public abstract record InputEvent;

/// <summary>A key press.</summary>
/// <param name="Text">What the key typed, or empty for a special key or a Ctrl/Alt chord.</param>
public sealed record KeyEvent(Key Key, KeyModifiers Modifiers, string Text) : InputEvent
{
    public bool Shift => (Modifiers & KeyModifiers.Shift) != 0;
    public bool Alt => (Modifiers & KeyModifiers.Alt) != 0;
    public bool Ctrl => (Modifiers & KeyModifiers.Ctrl) != 0;

    /// <summary>Whether this key types text: printable, with no Ctrl or Alt.</summary>
    public bool IsText => Text.Length > 0 && !Ctrl && !Alt;

    public bool IsCtrl(char letter) => Ctrl && Key == (Key)char.ToUpperInvariant(letter);

    public override string ToString()
    {
        var modifiers = new StringBuilder();
        if (Ctrl) modifiers.Append("Ctrl+");
        if (Alt) modifiers.Append("Alt+");
        if (Shift && Text.Length == 0) modifiers.Append("Shift+");
        return modifiers + KeyName();
    }

    private string KeyName()
    {
        var isSpecial = Key > (Key)0x10FFFF || Key < Key.Space;
        if (isSpecial) return Key.ToString();
        if (Text.Length > 0) return Text;
        return ((char)Key).ToString();
    }
}

/// <summary>A bracketed paste, delivered whole.</summary>
public sealed record PasteEvent(string Text) : InputEvent;

/// <summary>The terminal window gained or lost focus.</summary>
public sealed record FocusEvent(bool Gained) : InputEvent;

public enum MouseButton { Left, Middle, Right, None }

public enum MouseAction { Pressed, Released, Moved, WheelUp, WheelDown }

/// <summary>A mouse action, in cells from the top left of the terminal.</summary>
public sealed record MouseEvent(MouseAction Action, MouseButton Button, int X, int Y, KeyModifiers Modifiers) : InputEvent;

/// <summary>A terminal's reply to a query, such as DA1 or DSR, kept apart from keystrokes.</summary>
public sealed record ReplyEvent(string Sequence) : InputEvent;
