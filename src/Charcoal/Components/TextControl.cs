using Charcoal.Input;
using Charcoal.Layout;
using Charcoal.Rendering;

namespace Charcoal.Components;

/// <summary>
/// The node of an <c>input</c> or <c>textarea</c>. It holds a
/// <see cref="TextEditor"/>, keeps the caret in view as it paints, and handles
/// the keys, pastes and clicks that no handler took.
/// </summary>
/// <remarks>
/// It reads HTML's attributes: <c>value</c>, <c>placeholder</c>,
/// <c>type="password"</c>, <c>disabled</c>, <c>readonly</c>, <c>maxlength</c>,
/// <c>size</c>, <c>rows</c>, <c>cols</c>, <c>autofocus</c> and <c>wrap="off"</c>.
/// A textarea's text content is its default value until the user edits it.
/// Edits raise <c>oninput</c>. Enter in an input and losing focus raise
/// <c>onchange</c> when the value changed, which is what <c>@bind</c> uses.
/// </remarks>
public sealed class TextControlLayoutNode : ElementLayoutNode, ICustomPaint
{
    private const int DefaultSize = 20;
    private const int DefaultRows = 2;
    private const int DefaultCols = 20;

    private int _scrollX;
    private int _scrollY;
    private int _lastWidth = DefaultSize;
    private bool _hasValue;
    private bool _edited;
    private bool _resolved;
    private string _committed = "";
    private (int Size, int Rows, int Cols) _intrinsic = (DefaultSize, DefaultRows, DefaultCols);

    public TextControlLayoutNode(HostElement element) : base(element)
    {
        Editor.Multiline = Multiline;
    }

    public override bool IsLeaf => true;

    public TextEditor Editor { get; } = new();

    /// <summary>Whether this is a <c>textarea</c>.</summary>
    public bool Multiline => Element.Name == "textarea";

    /// <summary>The lower-cased <c>type</c> attribute, <c>text</c> by default.</summary>
    public string Type { get; private set; } = "text";

    public bool Disabled { get; private set; }

    public bool ReadOnly { get; private set; }

    public bool Autofocus { get; private set; }

    public string Placeholder { get; private set; } = "";

    public string Value => Editor.Text;

    public bool PlaceholderShown => Placeholder.Length > 0 && Editor.IsEmpty;

    /// <summary>Whether lines soft-wrap, which only a textarea without <c>wrap="off"</c> does.</summary>
    public bool Wraps => Multiline && Style.Wrap == TextWrap.Wrap && !WrapIsOff;

    private bool WrapIsOff =>
        string.Equals(Element.Attributes.GetValueOrDefault("wrap")?.ToString(), "off", StringComparison.OrdinalIgnoreCase);

    /// <summary>The caret relative to the content box at the last paint, or null when it is scrolled out of view.</summary>
    public (int Column, int Row)? CaretCell { get; private set; }

    /// <summary>Reads the attributes after <paramref name="name"/> changed, or all of them when null.</summary>
    internal void AttributesChanged(string? name)
    {
        var attributes = Element.Attributes;
        Disabled = IsSet(attributes.GetValueOrDefault("disabled"));
        ReadOnly = IsSet(attributes.GetValueOrDefault("readonly"));
        Autofocus = IsSet(attributes.GetValueOrDefault("autofocus"));
        Type = attributes.GetValueOrDefault("type")?.ToString()?.ToLowerInvariant() ?? "text";
        Placeholder = attributes.GetValueOrDefault("placeholder")?.ToString() ?? "";
        Editor.Masked = Type == "password";
        Editor.MaxLength = IntegerOf(attributes.GetValueOrDefault("maxlength")) is >= 0 and var max ? max : null;
        SetIntrinsicSize((
            AtLeastOne(attributes.GetValueOrDefault("size"), DefaultSize),
            AtLeastOne(attributes.GetValueOrDefault("rows"), DefaultRows),
            AtLeastOne(attributes.GetValueOrDefault("cols"), DefaultCols)));

        // Restyles call this too, and must not put the value attribute back
        // over typed text.
        var first = !_resolved;
        _resolved = true;
        if (first || name == "value")
        {
            ApplyValueAttribute(name == "value");
        }
    }

    private void SetIntrinsicSize((int Size, int Rows, int Cols) intrinsic)
    {
        if (intrinsic == _intrinsic) return;
        _intrinsic = intrinsic;
        InvalidateLayout();
    }

    private void ApplyValueAttribute(bool valueChanged)
    {
        if (Element.Attributes.TryGetValue("value", out var value))
        {
            _hasValue = true;
            Apply(value?.ToString() ?? "");
        }
        else if (_hasValue || valueChanged)
        {
            // A removed value attribute empties the field, as in a browser.
            _hasValue = false;
            Apply("");
        }
        else if (Multiline)
        {
            ContentChanged();
        }
    }

    /// <summary>Applies a textarea's text content as its default value, unless the user has edited it.</summary>
    internal void ContentChanged()
    {
        if (!Multiline || _hasValue || _edited) return;
        Apply(DefaultText(Element));
    }

    private static string DefaultText(HostNode node) =>
        string.Concat(node.Descendants().OfType<HostTextNode>().Select(text => text.Text));

    /// <summary>Sets the text from the markup and commits it.</summary>
    private void Apply(string text)
    {
        var placeholderWasShown = PlaceholderShown;
        Editor.SetText(text);
        _committed = Editor.Text;
        _edited = false;
        RestyleIfPlaceholderToggled(placeholderWasShown);
    }

    private static bool IsSet(object? value) => value is not (null or false);

    private static int AtLeastOne(object? value, int fallback) => Math.Max(1, IntegerOf(value) ?? fallback);

    private static int? IntegerOf(object? value) => value switch
    {
        null => null,
        int number => number,
        _ => int.TryParse(value.ToString(), out var number) ? number : null,
    };

    public override Size MeasureContent(int? availableWidth, int? availableHeight) =>
        Multiline ? new Size(_intrinsic.Cols, _intrinsic.Rows) : new Size(_intrinsic.Size, 1);

    /// <summary>Fields can shrink to a single column.</summary>
    public override int MinContentWidth() => 1;

    /// <summary>Handles a key no handler took.</summary>
    /// <param name="edited">Whether the text changed, which raises <c>oninput</c>.</param>
    /// <returns>Whether the field used the key.</returns>
    public bool HandleKey(KeyEvent key, out bool edited)
    {
        edited = false;
        if (Disabled) return false;

        var editor = Editor;
        var placeholderWasShown = PlaceholderShown;
        var byWord = key.Ctrl || key.Alt;
        var plain = !key.Ctrl && !key.Alt;
        var changed = false;

        // A field keeps its movement keys even when the caret cannot move,
        // and a read-only field keeps its editing keys without editing.
        bool Move(Func<bool> move)
        {
            move();
            return true;
        }

        bool Edit(Func<bool> edit)
        {
            if (!ReadOnly)
            {
                changed = edit();
            }
            return true;
        }

        var handled = key.Key switch
        {
            Key.Left => Move(() => editor.MoveLeft(byWord)),
            Key.Right => Move(() => editor.MoveRight(byWord)),
            Key.Home => Move(key.Ctrl && Multiline ? editor.MoveToStart : editor.MoveToLineStart),
            Key.End => Move(key.Ctrl && Multiline ? editor.MoveToEnd : editor.MoveToLineEnd),
            Key.Up when Multiline && plain => editor.MoveVertical(-1, _lastWidth, Wraps),
            Key.Down when Multiline && plain => editor.MoveVertical(1, _lastWidth, Wraps),
            Key.Backspace => Edit(byWord ? editor.DeleteWordBackward : editor.Backspace),
            Key.Delete => Edit(byWord ? editor.DeleteWordForward : editor.Delete),
            Key.Enter when Multiline && plain => Edit(() => editor.Insert("\n")),
            _ when key.IsCtrl('a') => Move(editor.MoveToLineStart),
            _ when key.IsCtrl('e') => Move(editor.MoveToLineEnd),
            _ when key.IsCtrl('b') => Move(() => editor.MoveLeft()),
            _ when key.IsCtrl('f') => Move(() => editor.MoveRight()),
            _ when key.IsCtrl('u') => Edit(editor.DeleteToLineStart),
            _ when key.IsCtrl('k') => Edit(editor.DeleteToLineEnd),
            _ when key.IsCtrl('w') => Edit(editor.DeleteWordBackward),
            _ when key.IsCtrl('d') => Edit(editor.Delete),
            _ when key.IsCtrl('h') => Edit(editor.Backspace),
            _ when IsAlt(key, 'b') => Move(() => editor.MoveLeft(true)),
            _ when IsAlt(key, 'f') => Move(() => editor.MoveRight(true)),
            _ when IsAlt(key, 'd') => Edit(editor.DeleteWordForward),
            _ when key.IsText => Edit(() => editor.Insert(key.Text)),
            _ => false,
        };

        if (changed)
        {
            Edited(placeholderWasShown);
        }
        edited = changed;
        return handled;
    }

    private static bool IsAlt(KeyEvent key, char letter) =>
        key.Alt && !key.Ctrl && key.Key == (Key)char.ToUpperInvariant(letter);

    /// <summary>Inserts pasted text at the caret.</summary>
    /// <returns>Whether the text changed.</returns>
    public bool Paste(string text)
    {
        var placeholderWasShown = PlaceholderShown;
        if (Disabled || ReadOnly || !Editor.Insert(text)) return false;
        Edited(placeholderWasShown);
        return true;
    }

    /// <summary>Moves the caret to the clicked cell.</summary>
    /// <returns>Whether the cell is inside the field.</returns>
    public bool Click(int x, int y)
    {
        if (Disabled) return false;
        var content = Layout.Deflate(Style.Inset);
        if (!Layout.Contains(x, y)) return false;
        var column = Math.Max(0, x - content.X) + _scrollX;
        var row = Math.Max(0, y - content.Y) + _scrollY;
        Editor.MoveTo(Editor.IndexAt(Math.Max(1, _lastWidth), Wraps, column, row));
        return true;
    }

    /// <summary>Commits the value if it changed since the last commit, for <c>onchange</c>.</summary>
    public bool TakeChange(out string value)
    {
        value = Editor.Text;
        if (value == _committed) return false;
        _committed = value;
        return true;
    }

    private void Edited(bool placeholderWasShown)
    {
        _edited = true;
        RestyleIfPlaceholderToggled(placeholderWasShown);
    }

    // The placeholder-shown state can be matched by selectors.
    private void RestyleIfPlaceholderToggled(bool placeholderWasShown)
    {
        if (placeholderWasShown != PlaceholderShown)
        {
            Element.Restyle();
        }
    }

    public void Paint(CellBuffer buffer, Rect rect)
    {
        _lastWidth = rect.Width;
        var wrap = Wraps;
        var lines = Editor.Lines(rect.Width, wrap);
        ScrollToCaret(rect.Width, rect.Height, lines);

        if (PlaceholderShown)
        {
            PaintPlaceholder(buffer, rect, wrap);
        }
        else
        {
            PaintLines(buffer, rect, lines);
        }

        var (column, row) = VisualCaret(rect.Width, wrap);
        column -= _scrollX;
        row -= _scrollY;
        var visible = column >= 0 && column < rect.Width && row >= 0 && row < rect.Height;
        CaretCell = visible ? (column, row) : null;
    }

    private void PaintPlaceholder(CellBuffer buffer, Rect rect, bool wrap)
    {
        var style = Style;
        var lines = TextLayout.Wrap([new TextRun(Placeholder)], rect.Width, wrap ? TextWrap.Wrap : TextWrap.Clip);
        for (var row = 0; row < lines.Count && row < rect.Height; row++)
        {
            var x = rect.X;
            foreach (var run in lines[row])
            {
                x += buffer.PutText(x, rect.Y + row, run.Text, style.Color, style.Background, style.TextStyle | TextStyle.Dim);
            }
        }
    }

    private void PaintLines(CellBuffer buffer, Rect rect, IReadOnlyList<TextLine> lines)
    {
        var style = Style;
        for (var row = 0; row < rect.Height && row + _scrollY < lines.Count; row++)
        {
            var line = lines[row + _scrollY];
            var shown = Editor.Display(Editor.Text[line.Start..line.End]);
            buffer.PutText(rect.X - _scrollX, rect.Y + row, shown, style.Color, style.Background, style.TextStyle);
        }
    }

    /// <summary>The caret's column and row, where a caret after a full line starts the next row when wrapping.</summary>
    private (int Column, int Row) VisualCaret(int width, bool wrap)
    {
        var (column, row) = Editor.CaretPosition(width, wrap);
        if (wrap && width > 0 && column >= width) return (0, row + 1);
        return (column, row);
    }

    /// <summary>Scrolls as little as keeps the caret in view, without scrolling past the text.</summary>
    private void ScrollToCaret(int width, int height, IReadOnlyList<TextLine> lines)
    {
        if (width <= 0 || height <= 0) return;
        var wrap = Wraps;
        var (column, row) = VisualCaret(width, wrap);

        var rows = Math.Max(lines.Count, row + 1);
        _scrollY = ScrollToShow(_scrollY, row, height, rows);
        if (wrap)
        {
            _scrollX = 0;
            return;
        }

        var widest = lines.Max(line => Editor.Width(Editor.Text[line.Start..line.End]));
        // The caret may stand one column past the widest line.
        _scrollX = ScrollToShow(_scrollX, column, width, widest + 1);
    }

    private static int ScrollToShow(int scroll, int position, int size, int extent)
    {
        if (position < scroll)
        {
            scroll = position;
        }
        else if (position >= scroll + size)
        {
            scroll = position - size + 1;
        }
        return Math.Clamp(scroll, 0, Math.Max(0, extent - size));
    }
}
