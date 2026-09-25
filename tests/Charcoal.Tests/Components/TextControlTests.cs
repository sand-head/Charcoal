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
        Assert.True(Element("button").Focusable);
        Assert.True(Element("button").Tabbable);
        Assert.False(Element("button", ("disabled", true)).Focusable);
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
    public void An_activation_input_paints_its_value_and_does_not_edit_text()
    {
        var button = Element("input", ("type", "button"), ("value", "save"));
        var control = button.Control!;
        Assert.Equal(new Size(8, 1), FlexLayout.Measure(button.Node, 80, null));
        Assert.Equal("[ save ]", Painted(Laid(button, 10, 1)).RowText(0).TrimEnd());
        Assert.False(control.HandleKey(Typed("x"), out _));
        Assert.Equal("save", control.Value);
    }

    [Fact]
    public void A_checkbox_is_a_small_leaf_toggled_by_space_or_a_click_and_shows_x_when_checked()
    {
        var box = Element("input", ("type", "checkbox"));
        var control = box.Control!;
        Assert.Equal(new Size(3, 1), FlexLayout.Measure(box.Node, 80, null));
        Assert.Equal(3, FlexLayout.MinContentWidth(box.Node));
        Assert.False(control.Checked);

        Assert.False(control.HandleKey(Special(Key.Enter), out _));   // Enter is nobody's toggle
        Assert.True(control.HandleKey(Typed(" "), out var edited));
        Assert.True(edited);
        Assert.True(control.Checked);
        Assert.Equal("true", control.Value);

        var body = Laid(box, 5, 1);
        Assert.Equal("[x]", Painted(body).RowText(0).TrimEnd());

        Assert.True(control.Click(0, 0));
        Assert.False(control.Checked);
        Assert.Equal("[ ]", Painted(body).RowText(0).TrimEnd());
    }

    [Fact]
    public void The_checked_attribute_drives_a_checkbox_without_resetting_a_toggle_on_restyle()
    {
        var box = Element("input", ("type", "checkbox"), ("checked", true));
        var control = box.Control!;
        Assert.True(control.Checked);

        box.SetAttribute("checked", false, 0);
        Assert.False(control.Checked);
        box.RemoveAttribute("checked");
        Assert.False(control.Checked);
        box.SetAttribute("checked", true, 0);
        Assert.True(control.Checked);

        // A restyle — focus moved, a sheet changed — leaves an interactive toggle alone.
        control.HandleKey(Typed(" "), out _);
        Assert.False(control.Checked);
        box.Restyle();
        Assert.False(control.Checked);
    }

    [Fact]
    public void An_indeterminate_checkbox_shows_a_dash_and_any_toggle_clears_it()
    {
        var box = Element("input", ("type", "checkbox"), ("indeterminate", true));
        var control = box.Control!;
        Assert.True(control.Indeterminate);
        Assert.Equal("[-]", Painted(Laid(box, 5, 1)).RowText(0).TrimEnd());

        control.HandleKey(Typed(" "), out _);
        Assert.False(control.Indeterminate);
        Assert.True(control.Checked);
    }

    [Fact]
    public void A_radio_group_keeps_one_checked_member_by_name_and_a_checked_radio_ignores_another_toggle()
    {
        var a = Element("input", ("type", "radio"), ("name", "size"), ("checked", true));
        var b = Element("input", ("type", "radio"), ("name", "size"));
        var other = Element("input", ("type", "radio"), ("name", "flavor"));
        var body = new HostElement("body");
        body.InsertChild(0, a);
        body.InsertChild(1, b);
        body.InsertChild(2, other);

        Assert.True(a.Control!.Checked);
        Assert.True(b.Control!.HandleKey(Typed(" "), out var edited));
        Assert.True(edited);
        Assert.True(b.Control.Checked);
        Assert.False(a.Control.Checked);       // the same-named radio was cleared
        Assert.False(other.Control!.Checked);  // a different name is untouched

        // A checked radio button ignores another click or Space: nothing changes.
        Assert.True(b.Control.HandleKey(Typed(" "), out edited));
        Assert.False(edited);
        Assert.True(b.Control.Checked);
    }

    [Fact]
    public void A_checkbox_or_radios_onchange_commits_once_per_settled_toggle()
    {
        var box = Element("input", ("type", "checkbox"));
        var control = box.Control!;
        Assert.False(control.TakeChange(out _));
        control.HandleKey(Typed(" "), out _);
        Assert.True(control.TakeChange(out var value));
        Assert.Equal("true", value);
        Assert.False(control.TakeChange(out _));   // already committed
        control.HandleKey(Typed(" "), out _);      // unchecked...
        control.HandleKey(Typed(" "), out _);      // ...and checked again: back where it was committed
        Assert.False(control.TakeChange(out _));
    }

    [Fact]
    public void Checked_and_indeterminate_pseudo_classes_match_the_live_state_not_the_checked_attribute()
    {
        var checkedSelector = Selector.Parse(":checked");
        var indeterminateSelector = Selector.Parse("input:indeterminate");
        var box = Element("input", ("type", "checkbox"), ("checked", true));
        Assert.True(checkedSelector.Matches(box, null));

        box.Control!.HandleKey(Typed(" "), out _);
        Assert.False(checkedSelector.Matches(box, null));   // the live state, not the [checked] attribute

        box.SetAttribute("indeterminate", true, 0);
        Assert.True(indeterminateSelector.Matches(box, null));
    }

    [Fact]
    public void A_number_field_types_digits_a_leading_minus_and_one_dot_but_rejects_the_rest()
    {
        var control = Element("input", ("type", "number")).Control!;
        Type(control, "-12.5x");
        Assert.Equal("-12.5", control.Value);   // the letter is refused
        Type(control, ".");
        Assert.Equal("-12.5", control.Value);   // a second dot is refused
        Assert.False(control.Paste("6a"));      // a paste that would not be a number is refused whole
        Assert.Equal("-12.5", control.Value);
        Assert.True(control.Paste("6"));
        Assert.Equal("-12.56", control.Value);
    }

    [Fact]
    public void ArrowUp_and_ArrowDown_step_a_number_by_step_and_clamp_to_min_and_max()
    {
        var control = Element("input", ("type", "number"), ("min", "0"), ("max", "10"), ("step", "5"), ("value", "8")).Control!;
        Assert.Equal("8", control.Value);

        Assert.True(control.HandleKey(Special(Key.Up), out var edited));
        Assert.True(edited);
        Assert.Equal("10", control.Value);   // 8 steps up to 13, which clamps to the max

        Assert.True(control.HandleKey(Special(Key.Up), out edited));
        Assert.False(edited);                // already at the max
        Assert.Equal("10", control.Value);

        Assert.True(control.HandleKey(Special(Key.Down), out edited));
        Assert.True(edited);
        Assert.Equal("5", control.Value);

        control.HandleKey(Special(Key.Down), out _);
        Assert.Equal("0", control.Value);
        Assert.True(control.HandleKey(Special(Key.Down), out edited));
        Assert.False(edited);                // already at the min
    }

    [Fact]
    public void A_number_fields_left_right_home_and_end_still_move_the_caret_not_the_value()
    {
        var control = Element("input", ("type", "number"), ("value", "123")).Control!;
        Assert.True(control.HandleKey(Special(Key.Home), out var edited));
        Assert.False(edited);
        Assert.Equal(0, control.Editor.Caret);
        Assert.True(control.HandleKey(Special(Key.End), out edited));
        Assert.False(edited);
        Assert.Equal(3, control.Editor.Caret);
        Assert.Equal("123", control.Value);
    }

    [Fact]
    public void A_range_defaults_to_the_midpoint_and_paints_a_track_with_a_thumb_at_its_value()
    {
        var box = Element("input", ("type", "range"), ("size", "11"));
        var control = box.Control!;
        Assert.Equal(50, control.RangeValue);
        Assert.Equal(new Size(11, 1), FlexLayout.Measure(box.Node, 80, null));

        var body = Laid(box, 11, 1);
        Assert.Equal("─────●─────", Painted(body).RowText(0));   // 50% of 11 cells: the thumb at column 5
    }

    [Fact]
    public void Arrow_keys_and_home_end_step_a_ranges_value_and_a_click_positions_it_proportionally()
    {
        var box = Element("input", ("type", "range"), ("min", "0"), ("max", "10"), ("step", "2"), ("value", "4"), ("size", "11"));
        var control = box.Control!;
        Assert.Equal(4, control.RangeValue);

        Assert.True(control.HandleKey(Special(Key.Right), out var edited));
        Assert.True(edited);
        Assert.Equal(6, control.RangeValue);

        control.HandleKey(Special(Key.Up), out _);
        Assert.Equal(8, control.RangeValue);   // Up steps the same way as Right

        Assert.True(control.HandleKey(Special(Key.Left), out edited));
        Assert.True(edited);
        Assert.Equal(6, control.RangeValue);

        control.HandleKey(Special(Key.Home), out _);
        Assert.Equal(0, control.RangeValue);
        control.HandleKey(Special(Key.End), out _);
        Assert.Equal(10, control.RangeValue);
        Assert.True(control.HandleKey(Special(Key.End), out edited));
        Assert.False(edited);                  // already at the max

        Laid(box, 11, 1);
        Assert.True(control.Click(0, 0));
        Assert.Equal(0, control.RangeValue);   // a click at the far left is the minimum
    }

    [Fact]
    public void A_ranges_value_attribute_and_a_changed_bound_drive_it_without_resetting_a_drag_on_restyle()
    {
        var box = Element("input", ("type", "range"), ("min", "0"), ("max", "10"), ("value", "7"));
        var control = box.Control!;
        Assert.Equal(7, control.RangeValue);

        box.SetAttribute("max", "5", 0);       // narrowing the bound re-clamps the current value
        Assert.Equal(5, control.RangeValue);

        control.HandleKey(Special(Key.Left), out _);
        Assert.Equal(4, control.RangeValue);

        // A restyle leaves an interactive drag alone, as it does for typed text.
        box.Restyle();
        Assert.Equal(4, control.RangeValue);
    }

    [Fact]
    public void A_number_and_a_ranges_onchange_commit_once_per_settled_value()
    {
        var number = Element("input", ("type", "number"), ("value", "1")).Control!;
        Assert.False(number.TakeChange(out _));
        number.HandleKey(Special(Key.Up), out _);
        Assert.True(number.TakeChange(out var value));
        Assert.Equal("2", value);
        Assert.False(number.TakeChange(out _));

        var range = Element("input", ("type", "range"), ("value", "5")).Control!;
        Assert.False(range.TakeChange(out _));
        range.HandleKey(Special(Key.Right), out _);
        Assert.True(range.TakeChange(out value));
        Assert.Equal("6", value);
        Assert.False(range.TakeChange(out _));
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
