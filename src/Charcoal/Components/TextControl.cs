using System.Globalization;
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
    private const int CheckableWidth = 3;

    private int _scrollX;
    private int _scrollY;
    private int _lastWidth = DefaultSize;
    private bool _hasValue;
    private bool _hasCheckedAttribute;
    private bool _checkedCommitted;
    private bool _hasRangeValue;
    private double _rangeCommitted;
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

    /// <summary>Whether this is a checkbox or a radio button, which toggle instead of editing text.</summary>
    public bool IsCheckable => Type is "checkbox" or "radio";

    /// <summary>Whether this is a number field: text editing restricted to a number, with a spinner.</summary>
    public bool IsNumeric => Type == "number";

    /// <summary>Whether this input activates a click handler rather than editing a value.</summary>
    public bool IsActivation => Type is "button" or "submit" or "reset";

    /// <summary>Whether this is a range: a slider with no text caret.</summary>
    public bool IsRange => Type == "range";

    /// <summary>Whether a user interaction commits a value immediately, as checkable controls and sliders do.</summary>
    public bool CommitsOnEdit => IsCheckable || IsRange;

    public bool Disabled { get; private set; }

    public bool ReadOnly { get; private set; }

    public bool Autofocus { get; private set; }

    public string Placeholder { get; private set; } = "";

    /// <summary>Whether a checkbox or radio button is checked; unrelated to <c>value</c>.</summary>
    public bool Checked { get; private set; }

    /// <summary>A checkbox's tri-state flag: neither checked nor unchecked until the user or the app settles it.</summary>
    /// <remarks>
    /// There is no HTML content attribute for this on the web either; Charcoal
    /// reads an <c>indeterminate</c> attribute as a pragmatic stand-in for the
    /// DOM property. Toggling always clears it, as a browser does.
    /// </remarks>
    public bool Indeterminate { get; private set; }

    /// <summary>The <c>min</c> attribute for a number or a range, or null if unset.</summary>
    public double? Min { get; private set; }

    /// <summary>The <c>max</c> attribute for a number or a range, or null if unset.</summary>
    public double? Max { get; private set; }

    /// <summary>The <c>step</c> attribute for a number or a range; a non-positive or missing value means 1.</summary>
    public double Step { get; private set; } = 1;

    /// <summary>A range's current position between <see cref="Min"/> and <see cref="Max"/>.</summary>
    public double RangeValue { get; private set; }

    public string Value
    {
        get
        {
            if (IsCheckable) return Checked ? "true" : "false";
            if (IsRange) return FormatNumber(RangeValue);
            return Editor.Text;
        }
    }

    /// <summary>The value as an event field carries it: bool for a checkable input, text otherwise.</summary>
    public object FieldValue => IsCheckable ? Checked : Value;

    public bool PlaceholderShown => !IsCheckable && !IsRange && Placeholder.Length > 0 && Editor.IsEmpty;

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
        Min = DoubleOf(attributes.GetValueOrDefault("min"));
        Max = DoubleOf(attributes.GetValueOrDefault("max"));
        Step = DoubleOf(attributes.GetValueOrDefault("step")) is { } step && step > 0 ? step : 1;
        SetIntrinsicSize((
            AtLeastOne(attributes.GetValueOrDefault("size"), DefaultSize),
            AtLeastOne(attributes.GetValueOrDefault("rows"), DefaultRows),
            AtLeastOne(attributes.GetValueOrDefault("cols"), DefaultCols)));

        // Restyles call this too, and must not put the value or checked
        // attribute back over what the user typed, toggled or dragged.
        var first = !_resolved;
        _resolved = true;
        if (!IsCheckable && !IsRange && (first || name == "value"))
        {
            ApplyValueAttribute(name == "value");
        }
        if (IsCheckable)
        {
            // Gated the same way as checked: a restyle must not put the
            // attribute back over a toggle that already cleared it.
            if (first || name is "indeterminate" or "type")
            {
                Indeterminate = IsSet(attributes.GetValueOrDefault("indeterminate"));
            }
            if (first || name is "checked" or "type")
            {
                ApplyCheckedAttribute(name is "checked" or "type");
            }
        }
        if (IsRange)
        {
            if (first || name is "value" or "type")
            {
                ApplyRangeValueAttribute(first || name == "type", name == "value");
            }
            else if (name is "min" or "max" or "step")
            {
                if (_hasRangeValue)
                {
                    // The bounds themselves changed under an interactive value:
                    // re-clamp it, rather than reverting to the value attribute.
                    ApplyRangeValue(ClampToStep(RangeValue));
                }
                else
                {
                    ApplyRangeValue(ClampToStep(DefaultRangeValue()));
                }
            }
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

    /// <summary>Applies the <c>checked</c> attribute, mirroring <see cref="ApplyValueAttribute"/>.</summary>
    private void ApplyCheckedAttribute(bool checkedChanged)
    {
        if (Element.Attributes.TryGetValue("checked", out var value))
        {
            _hasCheckedAttribute = true;
            ApplyChecked(IsSet(value));
        }
        else if (_hasCheckedAttribute || checkedChanged)
        {
            // A removed checked attribute unchecks the control, as a browser does.
            _hasCheckedAttribute = false;
            ApplyChecked(false);
        }
    }

    /// <summary>Sets the checked state from the markup and commits it.</summary>
    private void ApplyChecked(bool value)
    {
        Checked = value;
        _checkedCommitted = value;
    }

    /// <summary>Applies a range's <c>value</c> attribute, mirroring <see cref="ApplyValueAttribute"/>.</summary>
    /// <param name="first">Whether this is the control's first resolve, which must settle a default even without a <c>value</c> attribute.</param>
    private void ApplyRangeValueAttribute(bool first, bool valueChanged)
    {
        if (Element.Attributes.TryGetValue("value", out var value) && DoubleOf(value) is { } number)
        {
            _hasRangeValue = true;
            ApplyRangeValue(ClampToStep(number));
        }
        else if (first || _hasRangeValue || valueChanged)
        {
            // A removed value attribute falls back to the midpoint default, as a browser does.
            _hasRangeValue = false;
            ApplyRangeValue(ClampToStep(DefaultRangeValue()));
        }
    }

    /// <summary>Sets the range value from the markup and commits it.</summary>
    private void ApplyRangeValue(double value)
    {
        RangeValue = value;
        _rangeCommitted = value;
    }

    private double DefaultRangeValue()
    {
        var min = Min ?? 0;
        var max = Max ?? 100;
        return (min + max) / 2;
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

    private static double? DoubleOf(object? value) => value switch
    {
        null => null,
        double number => number,
        int number => number,
        _ => double.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : null,
    };

    /// <summary>Rounds to the nearest <paramref name="step"/> from <paramref name="basis"/>, then clamps to <c>[min, max]</c>.</summary>
    private static double SnapAndClamp(double value, double basis, double min, double max, double step)
    {
        if (max < min) (min, max) = (max, min);
        var stepped = basis + Math.Round((value - basis) / step) * step;
        return Math.Clamp(stepped, min, max);
    }

    /// <summary>Clamps a number's value; unlike a range, an unset bound is unbounded rather than 0 or 100.</summary>
    private double ClampNumber(double value) =>
        SnapAndClamp(value, Min ?? 0, Min ?? double.NegativeInfinity, Max ?? double.PositiveInfinity, Step);

    /// <summary>Clamps a range's value to its bounds, which default to 0 and 100 as HTML's do.</summary>
    private double ClampToStep(double value) =>
        SnapAndClamp(value, Min ?? 0, Min ?? 0, Max ?? 100, Step);

    /// <summary>Formats a stepped value without float noise: whole numbers plain, others to four places.</summary>
    private static string FormatNumber(double value)
    {
        if (Math.Abs(value) < 1e15 && value == Math.Truncate(value))
        {
            return ((long)value).ToString(CultureInfo.InvariantCulture);
        }
        return value.ToString("0.####", CultureInfo.InvariantCulture);
    }

    public override Size MeasureContent(int? availableWidth, int? availableHeight)
    {
        if (IsCheckable) return new Size(CheckableWidth, 1);
        if (IsActivation) return new Size(Editor.Width(Editor.Text) + 4, 1);
        return Multiline ? new Size(_intrinsic.Cols, _intrinsic.Rows) : new Size(_intrinsic.Size, 1);
    }

    /// <summary>Fields can shrink to a single column; a toggle or activation input keeps its glyph.</summary>
    public override int MinContentWidth()
    {
        if (IsCheckable) return CheckableWidth;
        if (IsActivation) return 4;
        return 1;
    }

    /// <summary>Handles a key no handler took.</summary>
    /// <param name="edited">Whether the text changed, which raises <c>oninput</c>.</param>
    /// <returns>Whether the field used the key.</returns>
    public bool HandleKey(KeyEvent key, out bool edited)
    {
        edited = false;
        if (Disabled || IsActivation) return false;

        if (IsCheckable)
        {
            // Space is the only key HTML toggles a checkbox or radio button
            // with; Enter is left to a component's own submit handling.
            if (key.Ctrl || key.Alt || key.Key != Key.Space) return false;
            edited = Toggle();
            return true;
        }

        if (IsRange)
        {
            if (key.Ctrl || key.Alt) return false;
            double? target = key.Key switch
            {
                Key.Left or Key.Down => RangeValue - Step,
                Key.Right or Key.Up => RangeValue + Step,
                Key.Home => Min ?? 0,
                Key.End => Max ?? 100,
                _ => null,
            };
            if (target is not { } proposed) return false;
            edited = SetRangeValue(proposed);
            return true;
        }

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
            Key.Up when IsNumeric && plain => Edit(() => StepNumber(1)),
            Key.Down when IsNumeric && plain => Edit(() => StepNumber(-1)),
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
            _ when key.IsText => Edit(() => IsNumeric ? InsertNumberText(key.Text) : editor.Insert(key.Text)),
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
        if (Disabled || ReadOnly) return false;
        var inserted = IsNumeric ? InsertNumberText(text) : Editor.Insert(text);
        if (!inserted) return false;
        Edited(placeholderWasShown);
        return true;
    }

    /// <summary>Moves the caret to the clicked cell.</summary>
    /// <returns>Whether the cell is inside the field.</returns>
    public bool Click(int x, int y)
    {
        if (Disabled || IsActivation) return false;
        var content = Layout.Deflate(Style.Inset);
        if (!Layout.Contains(x, y)) return false;
        if (IsCheckable) return Toggle();
        if (IsRange) return SetRangeValue(RangeValueAt(x, content));
        var column = Math.Max(0, x - content.X) + _scrollX;
        var row = Math.Max(0, y - content.Y) + _scrollY;
        Editor.MoveTo(Editor.IndexAt(Math.Max(1, _lastWidth), Wraps, column, row));
        return true;
    }

    /// <summary>Steps a number up or down by <see cref="Step"/>, clamped to <see cref="Min"/>/<see cref="Max"/>.</summary>
    /// <returns>Whether the text changed.</returns>
    private bool StepNumber(int direction)
    {
        var current = DoubleOf(Editor.Text) ?? Min ?? 0;
        var stepped = ClampNumber(current + direction * Step);
        return Editor.SetText(FormatNumber(stepped));
    }

    /// <summary>Inserts text into a number field only if the result still looks like a number.</summary>
    private bool InsertNumberText(string text)
    {
        var caret = Editor.Caret;
        var candidate = Editor.Text[..caret] + text + Editor.Text[caret..];
        return LooksLikeNumber(candidate) && Editor.Insert(text);
    }

    /// <summary>Whether text is a valid number or a state on the way to one: <c>""</c>, <c>"-"</c>, <c>"3."</c>.</summary>
    private static bool LooksLikeNumber(string text)
    {
        var index = 0;
        if (index < text.Length && text[index] == '-') index++;
        var sawDot = false;
        for (; index < text.Length; index++)
        {
            var c = text[index];
            if (char.IsAsciiDigit(c)) continue;
            if (c == '.' && !sawDot)
            {
                sawDot = true;
                continue;
            }
            return false;
        }
        return true;
    }

    /// <summary>Sets a range's value, clamped and stepped.</summary>
    /// <returns>Whether the value changed.</returns>
    private bool SetRangeValue(double target)
    {
        var clamped = ClampToStep(target);
        if (clamped == RangeValue) return false;
        RangeValue = clamped;
        return true;
    }

    /// <summary>The value a click at column <paramref name="x"/> maps to, proportional across the track.</summary>
    private double RangeValueAt(int x, Rect content)
    {
        var width = Math.Max(1, content.Width);
        var column = Math.Clamp(x - content.X, 0, width - 1);
        var fraction = width > 1 ? (double)column / (width - 1) : 0;
        var min = Min ?? 0;
        var max = Max ?? 100;
        return min + fraction * (max - min);
    }

    /// <summary>The default action a click or a Space key runs on a checkbox or radio button.</summary>
    /// <remarks>
    /// Inverts a checkbox, or selects a radio button and clears every other
    /// one sharing its <c>name</c>, as a browser's pre-click activation steps
    /// do. A checked radio button ignores the key or click. Toggling always
    /// clears <see cref="Indeterminate"/>.
    /// </remarks>
    /// <returns>Whether the checked state changed.</returns>
    private bool Toggle()
    {
        if (Type == "radio")
        {
            if (Checked) return false;
            Checked = true;
            Indeterminate = false;
            UncheckOtherRadiosInGroup();
        }
        else
        {
            Checked = !Checked;
            Indeterminate = false;
        }
        Element.Restyle();
        return true;
    }

    /// <summary>Unchecks every other radio button sharing this one's <c>name</c>.</summary>
    /// <remarks>There is no <c>&lt;form&gt;</c> scoping, so the search covers the whole tree.</remarks>
    private void UncheckOtherRadiosInGroup()
    {
        var name = Element.Attributes.GetValueOrDefault("name")?.ToString();
        if (string.IsNullOrEmpty(name)) return;

        var root = Element.AncestorElements().LastOrDefault() ?? Element;
        foreach (var sibling in root.Descendants().OfType<HostElement>())
        {
            if (ReferenceEquals(sibling, Element)) continue;
            if (sibling.Control is not { Type: "radio", Checked: true } other) continue;
            if (!string.Equals(sibling.Attributes.GetValueOrDefault("name")?.ToString(), name, StringComparison.Ordinal)) continue;

            other.Checked = false;
            other.Indeterminate = false;
            sibling.Restyle();
        }
    }

    /// <summary>Commits the value if it changed since the last commit, for <c>onchange</c>.</summary>
    public bool TakeChange(out string value)
    {
        value = Value;
        if (IsCheckable)
        {
            if (Checked == _checkedCommitted) return false;
            _checkedCommitted = Checked;
            return true;
        }
        if (IsRange)
        {
            if (RangeValue == _rangeCommitted) return false;
            _rangeCommitted = RangeValue;
            return true;
        }
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
        if (IsCheckable)
        {
            PaintCheckable(buffer, rect);
            return;
        }
        if (IsActivation)
        {
            PaintActivation(buffer, rect);
            return;
        }
        if (IsRange)
        {
            PaintRange(buffer, rect);
            return;
        }

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

    /// <summary>Paints a checkbox as <c>[ ]</c>/<c>[x]</c>/<c>[-]</c> or a radio button as <c>( )</c>/<c>(•)</c>.</summary>
    private void PaintCheckable(CellBuffer buffer, Rect rect)
    {
        CaretCell = null;
        var glyph = Type switch
        {
            "radio" => Checked ? "(•)" : "( )",
            _ when Indeterminate => "[-]",
            _ => Checked ? "[x]" : "[ ]",
        };
        buffer.PutText(rect.X, rect.Y, glyph, Style.Color, Style.Background, Style.TextStyle);
    }

    /// <summary>Paints an activation input as a compact terminal button.</summary>
    private void PaintActivation(CellBuffer buffer, Rect rect)
    {
        CaretCell = null;
        buffer.PutText(rect.X, rect.Y, $"[ {Editor.Text} ]", Style.Color, Style.Background, Style.TextStyle);
    }

    /// <summary>Paints a range as a dashed track with a thumb at the current value's position.</summary>
    private void PaintRange(CellBuffer buffer, Rect rect)
    {
        CaretCell = null;
        if (rect.Width <= 0 || rect.Height <= 0) return;

        var min = Min ?? 0;
        var max = Max ?? 100;
        var fraction = max > min ? (RangeValue - min) / (max - min) : 0;
        var thumb = Math.Clamp((int)Math.Round(fraction * (rect.Width - 1)), 0, rect.Width - 1);

        var style = Style;
        for (var column = 0; column < rect.Width; column++)
        {
            var glyph = column == thumb ? "●" : "─";
            buffer.PutText(rect.X + column, rect.Y, glyph, style.Color, style.Background, style.TextStyle);
        }
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
