namespace SlopTui.Rendering;

/// <summary>The SGR text attributes a cell can carry, as flags.</summary>
[Flags]
public enum TextStyle : byte
{
    None = 0,
    Bold = 1,
    Dim = 2,
    Italic = 4,
    Underline = 8,
    Inverse = 16,
    Strikethrough = 32,
}

/// <summary>A run of text with one appearance.</summary>
public readonly record struct TextRun(string Text, Color Foreground, Color Background, TextStyle Style)
{
    public TextRun(string text) : this(text, Color.Default, Color.Default, TextStyle.None) { }
}

/// <summary>A layout leaf whose content is styled text.</summary>
public interface ITextContent
{
    /// <summary>The runs in order; a <c>"\n"</c> inside a run breaks the line.</summary>
    IReadOnlyList<TextRun> Runs { get; }
}

/// <summary>A layout leaf that paints itself into a buffer clipped to its rect.</summary>
public interface ICustomPaint
{
    void Paint(CellBuffer buffer, Layout.Rect rect);
}
