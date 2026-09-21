using SlopTui.Layout;
using SlopTui.Rendering;

namespace SlopTui.Tests.Layout;

public class StyleParserTests
{
    private static Style Apply(string name, object? value) => StyleParser.Apply(Style.Default, name, value);

    [Fact]
    public void Enums_accept_kebab_case_pascal_case_and_typed_values()
    {
        Assert.Equal(JustifyContent.SpaceBetween, Apply("justify-content", "space-between").JustifyContent);
        Assert.Equal(JustifyContent.SpaceBetween, Apply("justify-content", "SpaceBetween").JustifyContent);
        Assert.Equal(JustifyContent.SpaceBetween, Apply("justify-content", JustifyContent.SpaceBetween).JustifyContent);
        Assert.Equal(FlexDirection.ColumnReverse, Apply("flex-direction", "column-reverse").FlexDirection);
        Assert.Equal(AlignItems.FlexEnd, Apply("align-items", "flex-end").AlignItems);
        Assert.Equal(AlignSelf.Center, Apply("align-self", "center").AlignSelf);
        Assert.Equal(Display.None, Apply("display", "none").Display);
        Assert.Equal(Position.Absolute, Apply("position", "absolute").Position);
        Assert.Equal(Overflow.Visible, Apply("overflow", "visible").Overflow);
        Assert.Equal(TextWrap.TruncateMiddle, Apply("wrap", "truncate-middle").Wrap);
    }

    [Fact]
    public void Lengths_read_cells_percent_and_auto()
    {
        Assert.Equal(Length.Cells(12), Apply("width", "12").Width);
        Assert.Equal(Length.Cells(12), Apply("width", 12).Width);
        Assert.Equal(Length.Percent(50), Apply("height", "50%").Height);
        Assert.Equal(Length.Auto, Apply("min-width", "auto").MinWidth);
        Assert.Equal(Length.Cells(3), Apply("max-height", Length.Cells(3)).MaxHeight);
        Assert.Equal(Length.Cells(0), Apply("flex-basis", "0").FlexBasis);
    }

    [Fact]
    public void Numbers_read_typed_and_spelled()
    {
        Assert.Equal(2, Apply("flex-grow", "2").FlexGrow);
        Assert.Equal(1.5, Apply("flex-grow", 1.5).FlexGrow);
        Assert.Equal(0, Apply("flex-shrink", 0).FlexShrink);
    }

    [Fact]
    public void Flex_shorthand_sets_grow_shrink_and_basis()
    {
        var one = Apply("flex", "1");
        Assert.Equal((1.0, 1.0, Length.Cells(0)), (one.FlexGrow, one.FlexShrink, one.FlexBasis));
        var full = Apply("flex", "2 0 10");
        Assert.Equal((2.0, 0.0, Length.Cells(10)), (full.FlexGrow, full.FlexShrink, full.FlexBasis));
        var none = Apply("flex", "none");
        Assert.Equal((0.0, 0.0, Length.Auto), (none.FlexGrow, none.FlexShrink, none.FlexBasis));
        Assert.Equal(3, Apply("flex", 3).FlexGrow);
    }

    [Fact]
    public void Padding_and_margin_take_one_two_or_four_values_and_per_side_names()
    {
        Assert.Equal(new Edges(2), Apply("padding", "2").Padding);
        Assert.Equal(new Edges(1, 3), Apply("padding", "1 3").Padding);
        Assert.Equal(new Edges(1, 2, 3, 4), Apply("margin", "1 2 3 4").Margin);
        Assert.Equal(new Edges(2), Apply("padding", 2).Padding);
        Assert.Equal(new Edges(1, 2, 3, 4), Apply("margin", new Edges(1, 2, 3, 4)).Margin);

        var side = StyleParser.Apply(Apply("padding", "1"), "padding-left", "5");
        Assert.Equal(new Edges(1, 1, 1, 5), side.Padding);
        var x = Apply("padding-x", 4);
        Assert.Equal(new Edges(0, 4, 0, 4), x.Padding);
        var y = Apply("margin-y", "2");
        Assert.Equal(new Edges(2, 0, 2, 0), y.Margin);
    }

    [Fact]
    public void Gap_sets_both_axes_and_the_axis_names_set_one()
    {
        var both = Apply("gap", "3");
        Assert.Equal((3, 3), (both.RowGap, both.ColumnGap));
        Assert.Equal(2, Apply("row-gap", 2).RowGap);
        Assert.Equal(4, Apply("column-gap", "4").ColumnGap);
    }

    [Fact]
    public void Offsets_read_ints_and_auto_as_unset()
    {
        Assert.Equal(3, Apply("top", "3").Top);
        Assert.Equal(-1, Apply("left", -1).Left);
        Assert.Null(Apply("right", "auto").Right);
        Assert.Null(Apply("bottom", null).Bottom);
    }

    [Fact]
    public void Colours_read_names_hex_rgb_and_typed_values()
    {
        Assert.Equal(Color.Red, Apply("color", "red").Color);
        Assert.Equal(Color.Rgb(0x11, 0x22, 0x33), Apply("background", "#112233").Background);
        Assert.Equal(Color.BrightBlue, Apply("border-color", Color.BrightBlue).BorderColor);
        Assert.Equal(Color.Rgb(1, 2, 3), Apply("background-color", "rgb(1, 2, 3)").Background);
        Assert.Equal(Color.Default, Apply("color", "default").Color);
    }

    [Fact]
    public void Borders_read_styles_booleans_and_sides()
    {
        Assert.Equal(BorderStyle.Round, Apply("border", "round").Border);
        Assert.Equal(BorderStyle.Single, Apply("border", true).Border);
        Assert.Equal(BorderStyle.Single, Apply("border", null).Border);
        Assert.Equal(BorderStyle.None, Apply("border", "none").Border);
        Assert.Equal(BorderStyle.Double, Apply("border-style", BorderStyle.Double).Border);
        Assert.False(Apply("border-top", "false").BorderTop);
        Assert.False(Apply("border-left", false).BorderLeft);
        Assert.True(Apply("border-right", null).BorderRight);
    }

    [Fact]
    public void Text_flags_set_and_clear_bits()
    {
        var bold = Apply("bold", "true");
        Assert.Equal(TextStyle.Bold, bold.TextStyle);
        var both = StyleParser.Apply(bold, "underline", true);
        Assert.Equal(TextStyle.Bold | TextStyle.Underline, both.TextStyle);
        var cleared = StyleParser.Apply(both, "bold", "false");
        Assert.Equal(TextStyle.Underline, cleared.TextStyle);
        Assert.Equal(TextStyle.Dim, Apply("dim", null).TextStyle);
        Assert.Equal(TextStyle.Italic | TextStyle.Inverse, Apply("text-style", "italic inverse").TextStyle);
        Assert.Equal(TextStyle.Strikethrough, Apply("strikethrough", "yes").TextStyle);
    }

    [Fact]
    public void Unknown_names_leave_the_style_alone()
    {
        Assert.Equal(Style.Default, Apply("onclick", "x"));
        Assert.Equal(Style.Default, Apply("focusable", true));
        Assert.False(StyleParser.IsStyleAttribute("paint"));
        Assert.True(StyleParser.IsStyleAttribute("flex-direction"));
        Assert.True(StyleParser.IsStyleAttribute("Padding-Left"));
    }

    [Theory]
    [InlineData("width", "wide")]
    [InlineData("flex-direction", "sideways")]
    [InlineData("padding", "1 2 3")]
    [InlineData("color", "#12")]
    [InlineData("bold", "maybe")]
    [InlineData("flex-grow", "lots")]
    public void Bad_values_throw_naming_the_property_and_the_value(string name, string value)
    {
        var ex = Assert.Throws<FormatException>(() => Apply(name, value));
        Assert.Contains(name, ex.Message);
        Assert.Contains(value, ex.Message);
    }

    [Fact]
    public void Inline_css_applies_every_declaration_in_order()
    {
        var style = StyleParser.ApplyInline(Style.Default,
            "flex-direction: column; padding: 1 2; gap: 1; border: round; color: green; bold: true; width: 50%;");
        Assert.Equal(FlexDirection.Column, style.FlexDirection);
        Assert.Equal(new Edges(1, 2), style.Padding);
        Assert.Equal(1, style.RowGap);
        Assert.Equal(BorderStyle.Round, style.Border);
        Assert.Equal(Color.Green, style.Color);
        Assert.Equal(TextStyle.Bold, style.TextStyle);
        Assert.Equal(Length.Percent(50), style.Width);
    }

    [Fact]
    public void Inline_css_rejects_unknown_properties_and_bare_words()
    {
        Assert.Throws<FormatException>(() => StyleParser.ApplyInline(Style.Default, "colour: red"));
        Assert.Throws<FormatException>(() => StyleParser.ApplyInline(Style.Default, "bold"));
    }

    [Fact]
    public void Later_declarations_win()
    {
        var style = StyleParser.ApplyInline(Style.Default, "padding: 3; padding-top: 0");
        Assert.Equal(new Edges(0, 3, 3, 3), style.Padding);
    }
}
