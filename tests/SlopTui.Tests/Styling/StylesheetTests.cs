using SlopTui.Styling;

namespace SlopTui.Tests.Styling;

public class StylesheetTests
{
    [Fact]
    public void A_rule_has_its_selectors_and_declarations_in_order()
    {
        var sheet = Stylesheet.Parse("box, text.title { padding: 1; color: red } text { bold: true }");

        Assert.Equal(2, sheet.Rules.Count);
        Assert.Equal(["box", "text.title"], sheet.Rules[0].Selectors.Select(s => s.Text));
        Assert.Equal([("padding", "1"), ("color", "red")], sheet.Rules[0].Declarations.Select(d => (d.Name, d.Value)));
        Assert.Equal(0, sheet.Rules[0].Order);
        Assert.Equal(1, sheet.Rules[1].Order);
        Assert.Empty(sheet.Warnings);
    }

    [Theory]
    [InlineData("box", "box", 0, 0)]
    [InlineData("*", "*", 0, 0)]
    [InlineData(".a", null, 1, 0)]
    [InlineData("#main", null, 0, 1)]
    [InlineData("text.a.b", "text", 2, 0)]
    [InlineData("box#main.a", "box", 1, 1)]
    [InlineData("box:focus", "box", 1, 0)]
    [InlineData(".a:focus-within", null, 2, 0)]
    public void A_compound_selector_reads_its_parts(string text, string? type, int classesAndPseudos, int ids)
    {
        var selector = Selector.Parse(text);
        var compound = Assert.Single(selector.Compounds);

        Assert.Equal(type, compound.Type);
        Assert.Equal(classesAndPseudos, compound.Classes.Count + (compound.Focus ? 1 : 0) + (compound.FocusWithin ? 1 : 0));
        Assert.Equal(ids, compound.Id is null ? 0 : 1);
        Assert.Equal(ids * 10_000 + classesAndPseudos * 100 + (type is not null && type != "*" ? 1 : 0), selector.Specificity);
    }

    [Fact]
    public void Combinators_are_descendant_for_a_space_and_child_for_an_angle()
    {
        var selector = Selector.Parse("box .a > text");

        Assert.Equal(3, selector.Compounds.Count);
        Assert.Equal(Combinator.None, selector.Compounds[0].Combinator);
        Assert.Equal(Combinator.Descendant, selector.Compounds[1].Combinator);
        Assert.Equal(Combinator.Child, selector.Compounds[2].Combinator);
        Assert.Equal("box .a > text", selector.Text);
    }

    [Fact]
    public void An_angle_without_spaces_is_still_a_child_combinator()
    {
        var selector = Selector.Parse("box>text");
        Assert.Equal(Combinator.Child, selector.Compounds[1].Combinator);
    }

    [Fact]
    public void Specificity_orders_ids_over_classes_over_types()
    {
        Assert.True(Selector.Parse("#a").Specificity > Selector.Parse(".a.b.c.d").Specificity);
        Assert.True(Selector.Parse(".a").Specificity > Selector.Parse("box text canvas").Specificity);
        Assert.Equal(Selector.Parse("box:focus").Specificity, Selector.Parse("box.a").Specificity);
        Assert.Equal(0, Selector.Parse("*").Specificity);
    }

    [Fact]
    public void Comments_are_ignored_and_lines_keep_counting_through_them()
    {
        var sheet = Stylesheet.Parse("/* the header */\nbox { /* inner */ padding: 1; }\n/* multi\nline */\ntext { color: red }");

        Assert.Equal(2, sheet.Rules.Count);
        Assert.Single(sheet.Rules[0].Declarations);
    }

    [Fact]
    public void A_bad_value_names_the_line_the_selector_and_the_property()
    {
        var ex = Assert.Throws<FormatException>(() => Stylesheet.Parse("box { padding: 1 }\n\ntext.title { color: notacolour }"));

        Assert.Contains("line 3", ex.Message);
        Assert.Contains("text.title", ex.Message);
        Assert.Contains("color: notacolour", ex.Message);
    }

    [Fact]
    public void An_unknown_property_is_kept_and_warned_about_not_thrown()
    {
        var sheet = Stylesheet.Parse("box { font-family: mono; padding: 2 }");

        var declaration = sheet.Rules[0].Declarations[0];
        Assert.False(declaration.Known);
        Assert.Equal("font-family", declaration.Name);
        var warning = Assert.Single(sheet.Warnings);
        Assert.Contains("font-family", warning);
        Assert.Contains("line 1", warning);
    }

    [Theory]
    [InlineData("box:hover { }", "hover")]
    [InlineData("> text { }", "start with a combinator")]
    [InlineData("box > { }", "end with a combinator")]
    [InlineData("box { padding 1 }", "name: value")]
    [InlineData("box { padding: 1 ", "closing")]
    [InlineData("box .a[x] { }", "unexpected")]
    [InlineData("box { padding: 1 } /* open", "comment is not closed")]
    public void What_the_grammar_does_not_cover_is_refused_with_a_reason(string css, string reason)
    {
        var ex = Assert.Throws<FormatException>(() => Stylesheet.Parse(css));
        Assert.Contains(reason, ex.Message);
    }

    [Fact]
    public void Focus_dependence_is_known_per_sheet()
    {
        Assert.False(Stylesheet.Parse("box { padding: 1 }").DependsOnFocus);
        var focus = Stylesheet.Parse("box:focus { border: single }");
        Assert.True(focus.DependsOnFocus);
        Assert.False(focus.FocusAffectsDescendants);
        var within = Stylesheet.Parse("box:focus-within text { color: red }");
        Assert.True(within.DependsOnFocus);
        Assert.True(within.FocusAffectsDescendants);
    }

    [Fact]
    public void An_empty_sheet_has_no_rules()
    {
        Assert.Empty(Stylesheet.Parse("  \n/* nothing */\n").Rules);
    }
}
