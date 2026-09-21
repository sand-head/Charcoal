using SlopTui.Layout;

namespace SlopTui.Terminal;

/// <summary>What to switch on when the terminal is started.</summary>
public sealed record TerminalOptions
{
    /// <summary>Use the alternate screen buffer, so the app leaves no scrollback behind.</summary>
    public bool AlternateScreen { get; init; } = true;

    /// <summary>Report mouse presses, releases, drags and wheel (SGR 1006 encoding).</summary>
    public bool Mouse { get; init; } = true;

    /// <summary>Wrap pastes in the bracketed-paste markers so they arrive as one event.</summary>
    public bool BracketedPaste { get; init; } = true;

    /// <summary>Ask for the kitty keyboard protocol's disambiguation flags; harmless where unsupported.</summary>
    public bool KittyKeyboard { get; init; } = true;

    /// <summary>Report the terminal window gaining and losing focus.</summary>
    public bool FocusEvents { get; init; } = true;

    /// <summary>Hide the terminal's cursor while running.</summary>
    public bool HideCursor { get; init; } = true;

    public static readonly TerminalOptions Default = new();
}

/// <summary>
/// Raw input, stamped when the read returned. The stamp tells a lone ESC key
/// from the start of an escape sequence, even when the app loop is busy.
/// </summary>
public readonly record struct InputChunk(string Text, long TimestampTicks);

/// <summary>The terminal as the app sees it: a size, raw input, and a place to write frames.</summary>
public interface ITerminal : IDisposable
{
    /// <summary>The current size in cells.</summary>
    Size Size { get; }

    /// <summary>Whether this is an interactive terminal (a TTY); false when redirected.</summary>
    bool IsInteractive { get; }

    /// <summary>Raised on the input thread.</summary>
    event Action<InputChunk>? InputReceived;

    /// <summary>Raised on any thread.</summary>
    event Action<Size>? Resized;

    /// <summary>Enter raw mode, switch on the options, and start the input thread.</summary>
    void Start(TerminalOptions options);

    /// <summary>Undoes <see cref="Start"/>. Safe to call more than once.</summary>
    void Stop();

    /// <summary>Writes a whole frame in a single call.</summary>
    void Write(ReadOnlySpan<char> text);
}
