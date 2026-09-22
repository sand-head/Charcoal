using SlopTui.Layout;
using SlopTui.Rendering;

namespace SlopTui.Tests.Layout;

public class StyleParserTests
{
    private static Style Apply(string name, object? value) => StyleParser.Apply(Style.Default, name, value);
    private static Style Css(string css) => StyleParser.ApplyInline(Style.Default, css);

    [Fact]
    public void Keywords_read_css_spellings_and_typed_values()
    {
        Assert.Equal(JustifyContent.SpaceBetween, Apply("justify-content", "space-between").JustifyContent);
        Assert.Equal(JustifyContent.SpaceBetween, Apply("justify-content", JustifyContent.SpaceBetween).JustifyContent);
        Assert.Equal(JustifyContent.FlexStart, Apply("justify-content", "start").JustifyContent);
        Assert.Equal(FlexDirection.ColumnReverse, Apply("flex-direction", "column-reverse").FlexDirection);
        Assert.Equal(AlignItems.FlexEnd, Apply("align-items", "flex-end").AlignItems);
        Assert.Equal(AlignItems.FlexEnd, Apply("align-items", "end").AlignItems);
        Assert.Equal(AlignSelf.Center, Apply("align-self", "center").AlignSelf);
        Assert.Equal(Display.None, Apply("display", "none").Display);
        Assert.Equal(Display.Flex, Apply("display", "inline-flex").Display);
        Assert.Equal(Display.Block, Apply("display", "block").Display);
        Assert.Equal(Display.Inline, Style.Default.Display);
        Assert.Equal(Position.Absolute, Apply("position", "absolute").Position);
        Assert.Equal(Overflow.Visible, Apply("overflow", "visible").Overflow);
        Assert.Equal(Overflow.Scroll, Apply("overflow", "auto").Overflow);
        Assert.Equal(Overflow.Hidden, Apply("overflow-y", "clip").Overflow);
        Assert.Equal(Visibility.Hidden, Apply("visibility", "hidden").Visibility);
        var flow = Apply("flex-flow", "column wrap");
        Assert.Equal((FlexDirection.Column, FlexWrap.Wrap), (flow.FlexDirection, flow.FlexWrap));
    }

    [Fact]
    public void Lengths_read_cells_units_percent_and_auto()
    {
        Assert.Equal(Length.Cells(12), Apply("width", "12").Width);
        Assert.Equal(Length.Cells(12), Apply("width", 12).Width);
        Assert.Equal(Length.Cells(12), Apply("width", "12ch").Width);
        Assert.Equal(Length.Cells(3), Apply("height", "3lh").Height);
        Assert.Equal(Length.Cells(2), Apply("height", "2em").Height);
        Assert.Equal(Length.Percent(50), Apply("height", "50%").Height);
        Assert.Equal(Length.Auto, Apply("min-width", "auto").MinWidth);
        Assert.Equal(Length.Auto, Apply("max-width", "none").MaxWidth);
        Assert.Equal(Length.Cells(3), Apply("max-height", Length.Cells(3)).MaxHeight);
        Assert.Equal(Length.Cells(0), Apply("flex-basis", "0").FlexBasis);
        var px = Assert.Throws<FormatException>(() => Apply("width", "12px"));
        Assert.Contains("pixels", px.Message);
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
    public void Padding_and_margin_take_one_to_four_values_physical_and_logical_sides()
    {
        Assert.Equal(new Edges(2), Apply("padding", "2").Padding);
        Assert.Equal(new Edges(1, 3), Apply("padding", "1 3").Padding);
        Assert.Equal(new Edges(1, 2, 3, 2), Apply("padding", "1 2 3").Padding);
        Assert.Equal(new Edges(1, 2, 3, 4), Apply("margin", "1 2 3 4").Margin);
        Assert.Equal(new Edges(2), Apply("padding", 2).Padding);
        Assert.Equal(new Edges(1, 2, 3, 4), Apply("margin", new Edges(1, 2, 3, 4)).Margin);

        Assert.Equal(new Edges(1, 1, 1, 5), Css("padding: 1; padding-left: 5").Padding);
        Assert.Equal(new Edges(0, 4, 0, 4), Apply("padding-inline", 4).Padding);
        Assert.Equal(new Edges(0, 3, 0, 2), Apply("padding-inline", "2 3").Padding);
        Assert.Equal(new Edges(2, 0, 2, 0), Apply("margin-block", "2").Margin);
        Assert.Equal(new Edges(0, 0, 0, 1), Apply("margin-inline-start", "1").Margin);
        Assert.Equal(new Edges(0, 0, 1, 0), Apply("padding-block-end", "1").Padding);
    }

    [Fact]
    public void Gap_sets_both_axes_or_row_then_column()
    {
        var both = Apply("gap", "3");
        Assert.Equal((3, 3), (both.RowGap, both.ColumnGap));
        var two = Apply("gap", "1 2");
        Assert.Equal((1, 2), (two.RowGap, two.ColumnGap));
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
        Assert.Equal(Color.Rgb(1, 2, 3), Apply("background-color", "rgb(1 2 3 / 50%)").Background);
        Assert.Equal(Color.Rgb(0xff, 0xd7, 0x00), Apply("color", "gold").Color);
        Assert.Equal(Color.Default, Apply("color", "currentcolor").Color);
        Assert.Equal(Color.Default, Apply("border-color", "transparent").BorderColor);
    }

    [Fact]
    public void Borders_read_the_shorthand_the_longhands_and_the_sides()
    {
        var solid = Apply("border", "solid");
        Assert.Equal(BorderStyle.Solid, solid.BorderStyle);
        Assert.Equal(BorderGlyphSet.Single, solid.BorderGlyphs);
        Assert.True(solid.BorderTop && solid.BorderLeft);

        var full = Apply("border", "2 double cyan");
        Assert.Equal((BorderStyle.Double, 2, Color.Cyan), (full.BorderStyle, full.BorderWidth, full.BorderColor));
        Assert.Equal(BorderGlyphSet.Double, full.BorderGlyphs);

        Assert.Equal(BorderGlyphSet.Round, Css("border: solid; border-radius: 1").BorderGlyphs);
        Assert.Equal(BorderGlyphSet.Bold, Css("border-style: solid; border-width: thick").BorderGlyphs);
        Assert.Equal(BorderGlyphSet.Classic, Apply("border-style", "dashed").BorderGlyphs);
        Assert.Equal(BorderGlyphSet.None, Apply("border", "none").BorderGlyphs);
        Assert.Equal(BorderGlyphSet.Single, Apply("border", "cyan").BorderGlyphs);   // a colour alone means a solid line

        var top = Apply("border-top", "solid");
        Assert.True(top.BorderTop);
        Assert.False(top.BorderBottom);
        Assert.Equal(new Edges(1, 0, 0, 0), top.BorderEdges);
        var noBottom = Css("border: solid; border-bottom-style: none");
        Assert.False(noBottom.BorderBottom);
        Assert.True(noBottom.BorderLeft);
        Assert.Equal(BorderStyle.Solid, Apply("border-left", BorderStyle.Solid).SideStyle(Side.Left));
    }

    [Fact]
    public void Text_properties_set_and_clear_flags()
    {
        Assert.Equal(TextStyle.Bold, Apply("font-weight", "bold").TextStyle);
        Assert.Equal(TextStyle.Bold, Apply("font-weight", "700").TextStyle);
        Assert.Equal(TextStyle.Dim, Apply("font-weight", "lighter").TextStyle);
        Assert.Equal(TextStyle.Italic, Apply("font-style", "italic").TextStyle);
        Assert.Equal(TextStyle.Underline, Apply("text-decoration", "underline").TextStyle);
        Assert.Equal(TextStyle.Underline | TextStyle.Strikethrough, Apply("text-decoration", "underline line-through red").TextStyle);
        Assert.Equal(TextStyle.Strikethrough, Apply("text-decoration-line", "line-through").TextStyle);
        Assert.Equal(TextStyle.Dim, Apply("opacity", "0.5").TextStyle);
        Assert.Equal(TextStyle.None, Apply("opacity", "1").TextStyle);
        Assert.Equal(TextStyle.Inverse, Apply("filter", "invert(100%)").TextStyle);
        Assert.Equal(TextStyle.Inverse, Apply("filter", "invert(1)").TextStyle);
        Assert.Equal(TextStyle.None, Apply("filter", "invert(0)").TextStyle);

        var cleared = Css("font-weight: bold; text-decoration: underline; font-weight: normal");
        Assert.Equal(TextStyle.Underline, cleared.TextStyle);
        Assert.Equal(TextStyle.Bold | TextStyle.Dim, cleared.TextStyleReset);   // so inheritance leaves them off
        Assert.Equal(TextStyle.None, Css("text-decoration: underline; text-decoration: none").TextStyle);
    }

    [Fact]
    public void White_space_text_overflow_and_text_align_read_their_keywords()
    {
        Assert.Equal(WhiteSpace.Pre, Apply("white-space", "pre").WhiteSpace);
        Assert.Equal(WhiteSpace.PreWrap, Apply("white-space", "pre-wrap").WhiteSpace);
        Assert.Equal(WhiteSpace.NoWrap, Apply("white-space", "nowrap").WhiteSpace);
        Assert.Equal(StyleSet.WhiteSpace, Apply("white-space", "normal").Set);
        Assert.Equal(TextOverflow.Ellipsis, Apply("text-overflow", "ellipsis").TextOverflow);
        Assert.Equal(TextOverflow.EllipsisStart, Apply("text-overflow", "ellipsis clip").TextOverflow);
        Assert.Equal(TextOverflow.EllipsisMiddle, Apply("text-overflow", "ellipsis ellipsis").TextOverflow);
        Assert.Equal(TextAlign.Center, Apply("text-align", "center").TextAlign);
        Assert.Equal(TextAlign.Right, Apply("text-align", "end").TextAlign);

        Assert.Equal(TextWrap.Wrap, Css("white-space: normal; text-overflow: ellipsis").Wrap);
        Assert.Equal(TextWrap.Truncate, Css("white-space: nowrap; text-overflow: ellipsis").Wrap);
        Assert.Equal(TextWrap.TruncateStart, Css("white-space: nowrap; text-overflow: ellipsis clip").Wrap);
        Assert.Equal(TextWrap.TruncateMiddle, Css("white-space: nowrap; text-overflow: ellipsis ellipsis").Wrap);
        Assert.Equal(TextWrap.Clip, Css("white-space: pre").Wrap);
    }

    [Fact]
    public void Properties_a_terminal_cannot_draw_are_accepted_and_unknown_names_are_not()
    {
        Assert.Equal(Style.Default, Apply("font-family", "monospace"));
        Assert.Equal(Style.Default, Apply("box-shadow", "0 0 1 black"));
        Assert.True(StyleParser.IsIgnored("line-height"));
        Assert.False(StyleParser.IsProperty("paint"));
        Assert.True(StyleParser.IsProperty("flex-direction"));
        Assert.True(StyleParser.IsProperty("Padding-Left"));
        Assert.Throws<FormatException>(() => Apply("focusable", true));
        Assert.Throws<FormatException>(() => Apply("bold", true));
        Assert.Throws<FormatException>(() => Apply("wrap", "clip"));
    }

    [Theory]
    [InlineData("width", "wide")]
    [InlineData("flex-direction", "sideways")]
    [InlineData("padding", "1 2 3 4 5")]
    [InlineData("color", "#12")]
    [InlineData("font-weight", "maybe")]
    [InlineData("flex-grow", "lots")]
    [InlineData("border", "round")]
    public void Bad_values_throw_naming_the_property_and_the_value(string name, string value)
    {
        var ex = Assert.Throws<FormatException>(() => Apply(name, value));
        Assert.Contains(name, ex.Message);
        Assert.Contains(value, ex.Message);
    }

    [Fact]
    public void Inline_css_applies_every_declaration_in_order()
    {
        var style = Css("display: flex; flex-direction: column; padding: 1 2; gap: 1; border: solid; border-radius: 1; color: green; font-weight: bold; width: 50%;");
        Assert.Equal(Display.Flex, style.Display);
        Assert.Equal(FlexDirection.Column, style.FlexDirection);
        Assert.Equal(new Edges(1, 2), style.Padding);
        Assert.Equal(1, style.RowGap);
        Assert.Equal(BorderGlyphSet.Round, style.BorderGlyphs);
        Assert.Equal(Color.Green, style.Color);
        Assert.Equal(TextStyle.Bold, style.TextStyle);
        Assert.Equal(Length.Percent(50), style.Width);
    }

    [Fact]
    public void Inline_css_rejects_unknown_properties_and_bare_words()
    {
        Assert.Throws<FormatException>(() => Css("colour: red"));
        Assert.Throws<FormatException>(() => Css("bold"));
    }

    [Fact]
    public void Later_declarations_win_and_important_is_ignored()
    {
        Assert.Equal(new Edges(0, 3, 3, 3), Css("padding: 3; padding-top: 0").Padding);
        Assert.Equal(new Edges(2), Css("padding: 2 !important").Padding);
    }

    [Fact]
    public void Custom_properties_substitute_through_var_with_fallbacks()
    {
        var style = Css("--accent: cyan; --pad: 2; color: var(--accent); padding: var(--pad) var(--gap, 4); border: solid var(--accent)");
        Assert.Equal(Color.Cyan, style.Color);
        Assert.Equal(new Edges(2, 4), style.Padding);
        Assert.Equal(Color.Cyan, style.BorderColor);

        var outer = new Dictionary<string, string> { ["--accent"] = "red" };
        Assert.Equal(Color.Red, StyleParser.ApplyInline(Style.Default, "color: var(--accent)", outer).Color);
        Assert.Equal(Color.Red, Css("color: red; color: var(--missing)").Color);
        Assert.Null(StyleParser.Substitute("var(--missing)", _ => null));
        Assert.Equal("rgb(1, 2, 3) solid", StyleParser.Substitute("var(--c, rgb(1, 2, 3)) var(--s)", k => k == "--s" ? "solid" : null));
    }

    [Fact]
    public void Container_properties_read_the_type_the_names_and_the_shorthand()
    {
        Assert.Equal(ContainerType.InlineSize, Apply("container-type", "inline-size").ContainerType);
        Assert.Equal(ContainerType.Size, Apply("container-type", "size").ContainerType);
        Assert.Equal(["a", "b"], Apply("container-name", "a b").ContainerNames);
        Assert.Empty(Apply("container-name", "none").ContainerNames);
        var shorthand = Apply("container", "card / inline-size");
        Assert.Equal((ContainerType.InlineSize, "card"), (shorthand.ContainerType, shorthand.ContainerNames[0]));
        Assert.Equal(ContainerType.Normal, Apply("container", "card").ContainerType);
        Assert.Throws<FormatException>(() => Apply("container-type", "block-size"));
        Assert.True(new Style { ContainerType = ContainerType.InlineSize }.Contains(true));
        Assert.False(new Style { ContainerType = ContainerType.InlineSize }.Contains(false));
    }
}
