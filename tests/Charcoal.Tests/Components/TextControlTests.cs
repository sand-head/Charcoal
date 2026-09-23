using Charcoal.Components;
using Charcoal.Input;
using Charcoal.Layout;
using Charcoal.Rendering;
using Charcoal.Styling;

namespace Charcoal.Tests.Components;

public class TextControlTests
{
    private static HostElement Element(string name, params (string Name, object? Value)[] attributes)
    {
        var element = new HostElement(name);
        foreach (var (attribute, value) in attributes) element.SetAttribute(attribute, value, 0);
        return element;
    }

    /// <summary>A body of the given size with the element in it, laid out.</summary>
    private static HostElement Laid(HostElement element, int width, int height)
    {
        var body = new HostElement("body");
        body.SetAttribute("style", $"width: {width}; height: {height}", 0);
        body.InsertChild(0, element);
        FlexLayout.Layout(body.Node, new Size(width, height));
        return body;
    }

    private static CellBuffer Painted(HostElement body)
    {
        var buffer = new CellBuffer(body.Node.Layout.Width, body.Node.Layout.Height);
        Painter.Paint(body.Node, buffer);
        return buffer;
    }

    private static KeyEvent Typed(string text) => new((Key)char.ToUpperInvariant(text[0]), KeyModifiers.None, text);
    private static KeyEvent Special(Key key, KeyModifiers modifiers = KeyModifiers.None) => new(key, modifiers, "");
    private static KeyEvent Ctrl(char letter) => new((Key)char.ToUpperInvariant(letter), KeyModifiers.Ctrl, "");

    private static void Type(TextControlLayoutNode control, string text)
    {
        foreach (var (cluster, _) in TextWidth.Clusters(text)) control.HandleKey(Typed(cluster), out _);
    }

    [Fact]
    public void A_control_is_a_leaf_sized_by_size_or_rows_and_cols_that_hugs_its_content()
    {
        var input = Element("input");
        Assert.IsType<TextControlLayoutNode>(input.Node);
        Assert.True(input.Node.IsLeaf);
        Assert.Equal(new Size(20, 1), FlexLayout.Measure(input.Node, 80, null));
        Assert.Equal(new Size(8, 1), FlexLayout.Measure(Element("input", ("size", 8)).Node, 80, null));
        Assert.Equal(new Size(30, 4), FlexLayout.Measure(Element("textarea", ("rows", 4), ("cols", 30)).Node, 80, null));
        Assert.Equal(new Size(20, 2), FlexLayout.Measure(Element("textarea").Node, 80, null));

        // The user-agent sheet makes it a block that hugs: not the body's width.
        var body = Laid(Element("input"), 60, 3);
        Assert.Equal(new Rect(0, 0, 20, 1), body.Node.Children[0].Layout);
        Assert.Equal(1, FlexLayout.MinContentWidth(Element("input").Node));
    }

    [Fact]
    public void A_control_is_focusable_and_in_the_tab_order_by_itself_unless_disabled_or_told_otherwise()
    {
        Assert.True(Element("input").Focusable);
        Assert.True(Element("input").Tabbable);
        Assert.True(Element("textarea").Tabbable);
        Assert.False(Element("input", ("disabled", true)).Focusable);
        Assert.True(Element("input", ("disabled", false)).Focusable);
        var clickOnly = Element("input", ("tabindex", -1));
        Assert.True(clickOnly.Focusable);
        Assert.False(clickOnly.Tabbable);
        Assert.False(Element("div").Focusable);
        Assert.True(Element("input", ("autofocus", true)).Autofocus);
    }

    [Fact]
    public void The_value_attribute_sets_the_text_with_the_caret_at_the_end_and_its_removal_clears_it()
    {
        var input = Element("input", ("value", "hello"));
        var control = input.Control!;
        Assert.Equal("hello", control.Value);
        Assert.Equal(5, control.Editor.Caret);

        control.HandleKey(Special(Key.Home), out _);
        input.SetAttribute("value", "hello", 0);   // the same value moves nothing
        Assert.Equal(0, control.Editor.Caret);
        input.SetAttribute("value", "bye", 0);
        Assert.Equal(("bye", 3), (control.Value, control.Editor.Caret));
        input.RemoveAttribute("value");
        Assert.Equal("", control.Value);

        // A restyle — focus moved, a sheet changed — leaves typed text alone.
        Type(control, "typed");
        input.Restyle();
        Assert.Equal("typed", control.Value);
    }

    [Fact]
    public void A_textarea_takes_its_child_text_as_the_default_value_until_the_user_edits()
    {
        var area = Element("textarea");
        var text = new HostTextNode { Text = "draft" };
        area.InsertChild(0, text);
        Assert.Equal("draft", area.Control!.Value);
        text.Text = "draft two";
        Assert.Equal("draft two", area.Control.Value);

        Type(area.Control, "!");
        text.Text = "replaced";
        Assert.Equal("draft two!", area.Control.Value);
    }

    [Fact]
    public void Keys_edit_and_a_readonly_or_disabled_field_refuses()
    {
        var control = Element("input").Control!;
        Type(control, "ab c");
        Assert.True(control.HandleKey(Special(Key.Backspace), out var edited));
        Assert.True(edited);
        Assert.True(control.HandleKey(Ctrl('w'), out edited) && edited);
        Assert.Equal("", control.Value);
        Type(control, "one two");
        Assert.True(control.HandleKey(Special(Key.Left), out edited));
        Assert.False(edited);
        Assert.False(control.HandleKey(Special(Key.Tab), out _));
        Assert.False(control.HandleKey(Special(Key.Enter), out _));   // an input's Enter is nobody's edit
        Assert.False(control.HandleKey(Ctrl('c'), out _));

        var readOnly = Element("input", ("readonly", true), ("value", "fixed")).Control!;
        Assert.True(readOnly.HandleKey(Typed("x"), out edited));
        Assert.False(edited);
        Assert.Equal("fixed", readOnly.Value);
        Assert.False(readOnly.Paste("y"));

        var disabled = Element("input", ("disabled", true)).Control!;
        Assert.False(disabled.HandleKey(Typed("x"), out _));
    }

    [Fact]
    public void A_textarea_makes_lines_with_enter_and_moves_between_them()
    {
        var control = Element("textarea", ("cols", 10)).Control!;
        Type(control, "first");
        control.HandleKey(Special(Key.Enter), out var edited);
        Assert.True(edited);
        Type(control, "second");
        Assert.Equal("first\nsecond", control.Value);
        Assert.True(control.HandleKey(Special(Key.Up), out _));
        Assert.Equal(5, control.Editor.Caret);
        Assert.False(control.HandleKey(Special(Key.Up), out _));   // the first line: not the field's
    }

    [Fact]
    public void The_text_paints_scrolled_so_the_caret_stays_in_the_box()
    {
        var input = Element("input", ("size", 5), ("value", "abcdefgh"));
        var body = Laid(input, 10, 1);
        var frame = Painted(body);
        // The caret is after the h, one past the text: the window shows "efgh" and a cell for it.
        Assert.Equal("efgh      ", frame.RowText(0));
        Assert.Equal((4, 0), input.Caret);

        input.Control!.HandleKey(Special(Key.Home), out _);
        frame = Painted(body);
        Assert.Equal("abcde     ", frame.RowText(0));
        Assert.Equal((0, 0), input.Caret);

        for (var i = 0; i < 6; i++) input.Control.HandleKey(Special(Key.Right), out _);
        frame = Painted(body);
        Assert.Equal("cdefg     ", frame.RowText(0));   // scrolled the least that shows the caret
        Assert.Equal((4, 0), input.Caret);
    }

    [Fact]
    public void A_password_paints_bullets_and_a_placeholder_paints_dim_until_something_is_typed()
    {
        var secret = Element("input", ("type", "password"), ("value", "hunter2"));
        Assert.Equal("•••••••", Painted(Laid(secret, 10, 1)).RowText(0).TrimEnd());

        var input = Element("input", ("placeholder", "your name"));
        var body = Laid(input, 12, 1);
        var frame = Painted(body);
        Assert.Equal("your name", frame.RowText(0).TrimEnd());
        Assert.Equal(TextStyle.Dim, frame[0, 0].Style);
        Assert.Equal((0, 0), input.Caret);
        Assert.True(input.Control!.PlaceholderShown);

        Type(input.Control, "a");
        Assert.False(input.Control.PlaceholderShown);
        Assert.Equal("a", Painted(body).RowText(0).TrimEnd());
    }

    [Fact]
    public void A_textarea_wraps_its_lines_scrolls_to_the_caret_row_and_a_click_places_the_caret()
    {
        var area = Element("textarea", ("cols", 8), ("rows", 2), ("value", "one two three four"));
        var body = Laid(area, 8, 2);
        var frame = Painted(body);
        // Lines: "one two " / "three " / "four"; the caret is on the last, so the first has scrolled off.
        Assert.Equal(["three   ", "four    "], new[] { frame.RowText(0), frame.RowText(1) });
        Assert.Equal((4, 1), area.Caret);

        area.Control!.HandleKey(Special(Key.Home, KeyModifiers.Ctrl), out _);
        frame = Painted(body);
        Assert.Equal("one two ", frame.RowText(0));
        Assert.Equal((0, 0), area.Caret);

        Assert.True(area.Control.Click(2, 1));   // "th|ree"
        Assert.Equal(10, area.Control.Editor.Caret);
        Painted(body);
        Assert.Equal((2, 1), area.Caret);
    }

    [Fact]
    public void A_change_is_taken_once_per_value_the_user_settled_on()
    {
        var input = Element("input", ("value", "a"));
        var control = input.Control!;
        Assert.False(control.TakeChange(out _));
        Type(control, "b");
        Assert.True(control.TakeChange(out var value));
        Assert.Equal("ab", value);
        Assert.False(control.TakeChange(out _));
        input.SetAttribute("value", "reset", 0);
        Assert.False(control.TakeChange(out _));
    }

    [Fact]
    public void The_form_pseudo_classes_match_disabled_enabled_and_a_shown_placeholder()
    {
        var disabled = Selector.Parse("input:disabled");
        var enabled = Selector.Parse(":enabled");
        var shown = Selector.Parse("input:placeholder-shown");
        var off = Element("input", ("disabled", true));
        var on = Element("input", ("placeholder", "hint"));
        Assert.True(disabled.Matches(off, null));
        Assert.False(disabled.Matches(on, null));
        Assert.True(enabled.Matches(on, null));
        Assert.False(enabled.Matches(Element("div"), null));
        Assert.True(shown.Matches(on, null));
        Type(on.Control!, "x");
        Assert.False(shown.Matches(on, null));

        // The user-agent sheet dims a disabled field.
        Assert.Equal(TextStyle.Dim, off.Node.Style.TextStyle);
    }
}
