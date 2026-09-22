using SlopTui.Styling;

namespace SlopTui.Tests.Styling;

public class StylesheetTests
{
    [Fact]
    public void A_rule_has_its_selectors_and_declarations_in_order()
    {
        var sheet = Stylesheet.Parse("div, p.title { padding: 1; color: red } p { font-weight: bold }");

        Assert.Equal(2, sheet.Rules.Count);
        Assert.Equal(["div", "p.title"], sheet.Rules[0].Selectors.Select(s => s.Text));
        Assert.Equal([("padding", "1"), ("color", "red")], sheet.Rules[0].Declarations.Select(d => (d.Name, d.Value)));
        Assert.Equal(0, sheet.Rules[0].Order);
        Assert.Equal(1, sheet.Rules[1].Order);
        Assert.Empty(sheet.Warnings);
    }

    [Theory]
    [InlineData("div", "div", 0, 0)]
    [InlineData("*", "*", 0, 0)]
    [InlineData(".a", null, 1, 0)]
    [InlineData("#main", null, 0, 1)]
    [InlineData("p.a.b", "p", 2, 0)]
    [InlineData("div#main.a", "div", 1, 1)]
    [InlineData("div:focus", "div", 1, 0)]
    [InlineData(".a:focus-within", null, 2, 0)]
    [InlineData("[hidden]", null, 1, 0)]
    [InlineData("div[b-x1y2z3].card", "div", 2, 0)]
    [InlineData("p:first-child", "p", 1, 0)]
    [InlineData(":root", null, 1, 0)]
    public void A_compound_selector_reads_its_parts(string text, string? type, int classesAndPseudos, int ids)
    {
        var selector = Selector.Parse(text);
        var compound = Assert.Single(selector.Compounds);

        Assert.Equal(type, compound.Type);
        Assert.Equal(classesAndPseudos, compound.Classes.Count + compound.Attributes.Count + System.Numerics.BitOperations.PopCount((uint)compound.Pseudo));
        Assert.Equal(ids, compound.Id is null ? 0 : 1);
        Assert.Equal(ids * 10_000 + classesAndPseudos * 100 + (type is not null && type != "*" ? 1 : 0), selector.Specificity);
    }

    [Fact]
    public void Combinators_are_descendant_for_a_space_and_child_for_an_angle()
    {
        var selector = Selector.Parse("div .a > p");

        Assert.Equal(3, selector.Compounds.Count);
        Assert.Equal(Combinator.None, selector.Compounds[0].Combinator);
        Assert.Equal(Combinator.Descendant, selector.Compounds[1].Combinator);
        Assert.Equal(Combinator.Child, selector.Compounds[2].Combinator);
        Assert.Equal("div .a > p", selector.Text);
    }

    [Fact]
    public void An_angle_without_spaces_is_still_a_child_combinator()
    {
        var selector = Selector.Parse("div>p");
        Assert.Equal(Combinator.Child, selector.Compounds[1].Combinator);
    }

    [Fact]
    public void Specificity_orders_ids_over_classes_over_types()
    {
        Assert.True(Selector.Parse("#a").Specificity > Selector.Parse(".a.b.c.d").Specificity);
        Assert.True(Selector.Parse(".a").Specificity > Selector.Parse("div p canvas").Specificity);
        Assert.Equal(Selector.Parse("div:focus").Specificity, Selector.Parse("div.a").Specificity);
        Assert.Equal(Selector.Parse("div[x]").Specificity, Selector.Parse("div.a").Specificity);
        Assert.Equal(0, Selector.Parse("*").Specificity);
    }

    [Fact]
    public void Comments_are_ignored_and_lines_keep_counting_through_them()
    {
        var sheet = Stylesheet.Parse("/* the header */\ndiv { /* inner */ padding: 1; }\n/* multi\nline */\np { color: red }");

        Assert.Equal(2, sheet.Rules.Count);
        Assert.Single(sheet.Rules[0].Declarations);
    }

    [Fact]
    public void A_bad_value_names_the_line_the_selector_and_the_property()
    {
        var ex = Assert.Throws<FormatException>(() => Stylesheet.Parse("div { padding: 1 }\n\np.title { color: notacolour }"));

        Assert.Contains("line 3", ex.Message);
        Assert.Contains("p.title", ex.Message);
        Assert.Contains("color: notacolour", ex.Message);
    }

    [Fact]
    public void An_unknown_property_is_kept_and_warned_about_and_a_web_only_one_is_quietly_ignored()
    {
        var sheet = Stylesheet.Parse("div { bold: true; font-family: mono; padding: 2 }");

        var declaration = sheet.Rules[0].Declarations[0];
        Assert.False(declaration.Known);
        Assert.Equal("bold", declaration.Name);
        Assert.True(sheet.Rules[0].Declarations[1].Known);   // font-family: CSS, not drawn, no warning
        var warning = Assert.Single(sheet.Warnings);
        Assert.Contains("bold", warning);
        Assert.Contains("line 1", warning);
    }

    [Fact]
    public void Unknown_at_rules_are_skipped_whole_and_custom_properties_are_declarations()
    {
        var sheet = Stylesheet.Parse("""
            @import url("shared.css");
            @media (prefers-color-scheme: dark) { div { color: white } .x { --y: 1 } }
            :root { --accent: cyan }
            div { color: var(--accent); padding: 1 !important }
            """);
        Assert.Equal(4, sheet.Rules.Count);   // the @media block is parsed, the @import skipped
        Assert.Single(sheet.Warnings);
        Assert.NotNull(sheet.Rules[0].Media);
        Assert.True(sheet.Rules[1].Declarations[0].IsCustom);
        Assert.True(sheet.Rules[2].Declarations[0].IsCustom);
        Assert.Equal("var(--accent)", sheet.Rules[3].Declarations[0].Value);
        Assert.Equal("1", sheet.Rules[3].Declarations[1].Value);
    }

    [Theory]
    [InlineData("div:hover { }", "hover")]
    [InlineData("> p { }", "start with a combinator")]
    [InlineData("div > { }", "end with a combinator")]
    [InlineData("div { padding 1 }", "name: value")]
    [InlineData("div { padding: 1 ", "closing")]
    [InlineData("div .a[x~=y] { }", "attribute selectors")]
    [InlineData("div::before { }", "pseudo-elements")]
    [InlineData("div { padding: 1 } /* open", "comment is not closed")]
    public void What_the_grammar_does_not_cover_is_refused_with_a_reason(string css, string reason)
    {
        var ex = Assert.Throws<FormatException>(() => Stylesheet.Parse(css));
        Assert.Contains(reason, ex.Message);
    }

    [Fact]
    public void Focus_dependence_is_known_per_sheet()
    {
        Assert.False(Stylesheet.Parse("div { padding: 1 }").DependsOnFocus);
        var focus = Stylesheet.Parse("div:focus { border: solid }");
        Assert.True(focus.DependsOnFocus);
        Assert.False(focus.FocusAffectsDescendants);
        var within = Stylesheet.Parse("div:focus-within p { color: red }");
        Assert.True(within.DependsOnFocus);
        Assert.True(within.FocusAffectsDescendants);
    }

    [Fact]
    public void An_empty_sheet_has_no_rules()
    {
        Assert.Empty(Stylesheet.Parse("  \n/* nothing */\n").Rules);
    }

    [Fact]
    public void Media_blocks_attach_their_query_to_the_rules_inside_and_nest_with_and()
    {
        var sheet = Stylesheet.Parse("""
            div { padding: 1 }
            @media (max-width: 60) {
                div { padding: 0 }
                @media (orientation: portrait) { .side { display: none } }
            }
            @media screen and (min-width: 120), print { .wide { display: block } }
            p { margin: 0 }
            """);
        Assert.Equal(5, sheet.Rules.Count);
        Assert.Null(sheet.Rules[0].Media);
        Assert.Equal("(max-width: 60)", sheet.Rules[1].Media!.Text);
        Assert.Equal("(max-width: 60) and (orientation: portrait)", sheet.Rules[2].Media!.Text);
        Assert.Equal("screen and (min-width: 120), print", sheet.Rules[3].Media!.Text);
        Assert.Null(sheet.Rules[4].Media);
        Assert.Equal([0, 1, 2, 3, 4], sheet.Rules.Select(r => r.Order));
        Assert.True(sheet.UsesMedia);
        Assert.Empty(sheet.Warnings);
        Assert.False(Stylesheet.Parse("div { padding: 1 }").UsesMedia);
    }

    [Fact]
    public void Supports_blocks_are_kept_or_dropped_at_parse_time()
    {
        var sheet = Stylesheet.Parse("""
            @supports (display: grid) { .a { display: grid } }
            @supports (display: grid) and (gap: 1px) { .b { padding: 9 } }
            @supports not (display: grid) { .c { padding: 9 } }
            @media (min-width: 1) { @supports (color: red) { .d { color: red } } }
            """);
        Assert.Equal(["a", "d"], sheet.Rules.Select(r => r.Selectors[0].Compounds[0].Classes[0]));
        Assert.Equal("(min-width: 1)", sheet.Rules[1].Media!.Text);
        Assert.Equal(2, sheet.Warnings.Count);
    }

    [Fact]
    public void A_bad_media_query_names_its_line()
    {
        var ex = Assert.Throws<FormatException>(() => Stylesheet.Parse("div { }\n@media (min-width 80) { div { } }"));
        Assert.Contains("line 2", ex.Message);
        Assert.Throws<FormatException>(() => Stylesheet.Parse("@media (min-width: 80) { div { padding: 1 }"));
    }
}
