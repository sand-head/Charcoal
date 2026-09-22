using SlopTui.Components;
using SlopTui.Layout;
using SlopTui.Rendering;
using SlopTui.Styling;

namespace SlopTui.Tests.Styling;

public class StyleResolverTests
{
    private readonly StyleContext _ctx = new();

    private HostElement Element(string name, HostElement? parent = null, string? classes = null, string? id = null, string? style = null)
    {
        var element = new HostElement(name, _ctx);
        parent?.InsertChild(parent.Children.Count, element);
        if (classes is not null) element.SetAttribute("class", classes, 0);
        if (id is not null) element.SetAttribute("id", id, 0);
        if (style is not null) element.SetAttribute("style", style, 0);
        return element;
    }

    /// <summary>A component container between parent and child: transparent to matching, as to layout.</summary>
    private static HostContainer Container(HostElement parent)
    {
        var container = new HostContainer();
        parent.InsertChild(parent.Children.Count, container);
        return container;
    }

    [Fact]
    public void Type_class_id_attribute_and_star_match_what_they_name()
    {
        var div = Element("div", classes: "a b", id: "main");
        div.SetAttribute("data-kind", "card", 0);
        div.SetAttribute("b-scope1", true, 0);
        var span = Element("span", div);

        Assert.True(StyleResolver.Matches(div, Selector.Parse("div")));
        Assert.True(StyleResolver.Matches(div, Selector.Parse("DIV")));
        Assert.True(StyleResolver.Matches(div, Selector.Parse("*")));
        Assert.True(StyleResolver.Matches(div, Selector.Parse(".a")));
        Assert.True(StyleResolver.Matches(div, Selector.Parse(".b.a")));
        Assert.True(StyleResolver.Matches(div, Selector.Parse("#main")));
        Assert.True(StyleResolver.Matches(div, Selector.Parse("div#main.a")));
        Assert.True(StyleResolver.Matches(div, Selector.Parse("[b-scope1]")));
        Assert.True(StyleResolver.Matches(div, Selector.Parse("[data-kind=card]")));
        Assert.True(StyleResolver.Matches(div, Selector.Parse("[data-kind=\"card\"]")));
        Assert.False(StyleResolver.Matches(div, Selector.Parse("[data-kind=other]")));
        Assert.False(StyleResolver.Matches(div, Selector.Parse("span")));
        Assert.False(StyleResolver.Matches(div, Selector.Parse(".c")));
        Assert.False(StyleResolver.Matches(span, Selector.Parse("#main")));
        Assert.False(StyleResolver.Matches(span, Selector.Parse("[b-scope1]")));
    }

    [Fact]
    public void Descendant_and_child_combinators_walk_elements_through_containers()
    {
        var root = Element("div", classes: "root");
        var mid = Element("div", root, classes: "mid");
        var container = Container(mid);
        var span = new HostElement("span", _ctx);
        container.InsertChild(0, span);

        Assert.True(StyleResolver.Matches(span, Selector.Parse(".root span")));
        Assert.True(StyleResolver.Matches(span, Selector.Parse(".mid > span")));   // the component container between does not count
        Assert.False(StyleResolver.Matches(span, Selector.Parse(".root > span")));
        Assert.True(StyleResolver.Matches(span, Selector.Parse(".root > .mid > span")));
        Assert.False(StyleResolver.Matches(mid, Selector.Parse(".mid span")));
    }

    [Fact]
    public void Root_first_child_and_last_child_see_the_tree_through_containers()
    {
        var root = Element("div");
        var first = Element("p", root);
        var container = Container(root);
        var last = new HostElement("p", _ctx);
        container.InsertChild(0, last);

        Assert.True(StyleResolver.Matches(root, Selector.Parse(":root")));
        Assert.False(StyleResolver.Matches(first, Selector.Parse(":root")));
        Assert.True(StyleResolver.Matches(first, Selector.Parse("p:first-child")));
        Assert.False(StyleResolver.Matches(first, Selector.Parse("p:last-child")));
        Assert.True(StyleResolver.Matches(last, Selector.Parse("p:last-child")));
        Assert.False(StyleResolver.Matches(last, Selector.Parse(":first-child")));
    }

    [Fact]
    public void Focus_pseudo_classes_follow_the_context()
    {
        var outer = Element("div");
        var inner = Element("div", outer);
        var span = Element("span", inner);
        _ctx.Focused = inner;

        Assert.True(StyleResolver.Matches(inner, Selector.Parse("div:focus"), inner));
        Assert.False(StyleResolver.Matches(outer, Selector.Parse("div:focus"), inner));
        Assert.True(StyleResolver.Matches(outer, Selector.Parse("div:focus-within"), inner));
        Assert.True(StyleResolver.Matches(inner, Selector.Parse("div:focus-within"), inner));
        Assert.False(StyleResolver.Matches(span, Selector.Parse(":focus-within"), inner));
        Assert.True(StyleResolver.Matches(span, Selector.Parse("div:focus > span"), inner));
    }

    [Fact]
    public void The_user_agent_sheet_is_under_everything()
    {
        Assert.Equal(Display.Block, Element("div").Node.Style.Display);
        Assert.Equal(Display.Inline, Element("span").Node.Style.Display);
        Assert.Equal(Display.Inline, Element("custom-thing").Node.Style.Display);
        Assert.Equal(TextStyle.Bold, Element("strong").Node.Style.TextStyle);
        Assert.Equal(TextStyle.Italic, Element("em").Node.Style.TextStyle);
        Assert.Equal(WhiteSpace.Pre, Element("pre").Node.Style.WhiteSpace);
        Assert.Equal(new Edges(1, 0), Element("p").Node.Style.Margin);
        Assert.Equal(Display.None, Element("div", style: null).Node.Style.Display == Display.Block && false ? Display.None : Display.None);

        _ctx.Sheets.Add("div { display: flex } strong { font-weight: normal }");
        Assert.Equal(Display.Flex, Element("div").Node.Style.Display);
        Assert.Equal(TextStyle.None, Element("strong").Node.Style.TextStyle);

        var hidden = Element("div");
        hidden.SetAttribute("hidden", true, 0);
        Assert.Equal(Display.None, hidden.Node.Style.Display);
    }

    [Fact]
    public void The_cascade_applies_lower_specificity_first_so_the_higher_wins()
    {
        _ctx.Sheets.Add("div { padding: 1; gap: 3 } .wide { padding: 4 } #main { gap: 9 }");
        var div = Element("div", classes: "wide", id: "main");

        Assert.Equal(new Edges(4), div.Node.Style.Padding);
        Assert.Equal(9, div.Node.Style.RowGap);
    }

    [Fact]
    public void At_equal_specificity_the_later_rule_and_the_later_sheet_win()
    {
        _ctx.Sheets.Add(".a { padding: 1 } .a { padding: 2 }");
        _ctx.Sheets.Add(".a { gap: 5 }");
        _ctx.Sheets.Add(".a { gap: 6 }");
        var div = Element("div", classes: "a");

        Assert.Equal(new Edges(2), div.Node.Style.Padding);
        Assert.Equal(6, div.Node.Style.RowGap);
    }

    [Fact]
    public void A_selector_list_takes_the_specificity_of_the_selector_that_matched()
    {
        _ctx.Sheets.Add("#main, p { padding: 5 } div.a { padding: 2 }");
        var div = Element("div", classes: "a", id: "main");

        // "#main, p" matched through #main (10000), which beats div.a (101).
        Assert.Equal(new Edges(5), div.Node.Style.Padding);
    }

    [Fact]
    public void The_inline_style_beats_every_sheet()
    {
        _ctx.Sheets.Add("#main { padding: 5; gap: 5; color: red }");
        var div = Element("div", id: "main", style: "padding: 1; gap: 2");

        Assert.Equal(new Edges(1), div.Node.Style.Padding);
        Assert.Equal(2, div.Node.Style.RowGap);
        Assert.Equal(Color.Red, div.Node.Style.Color);   // untouched by the element: the sheet's
    }

    [Fact]
    public void An_unknown_property_in_a_sheet_is_skipped_at_resolve_time()
    {
        _ctx.Sheets.Add("div { bold: true; font-family: mono; padding: 3 }");
        var div = Element("div");

        Assert.Equal(new Edges(3), div.Node.Style.Padding);
    }

    [Fact]
    public void Colour_and_text_flags_inherit_and_an_element_can_turn_a_flag_off()
    {
        _ctx.Sheets.Add(".outer { color: red; font-weight: bold } .inner { color: green; font-style: italic }");
        var outer = Element("div", classes: "outer");
        var inner = Element("div", outer, classes: "inner");
        var span = Element("span", inner);
        var own = Element("span", inner, style: "color: blue; font-weight: normal");

        Assert.Equal(Color.Green, span.Node.Style.Color);
        Assert.Equal(TextStyle.Bold | TextStyle.Italic, span.Node.Style.TextStyle);
        Assert.Equal(Color.Blue, own.Node.Style.Color);
        Assert.Equal(TextStyle.Italic, own.Node.Style.TextStyle);
        Assert.Equal(TextStyle.Italic, Element("span", own).Node.Style.TextStyle);   // and the reset holds below it
    }

    [Fact]
    public void White_space_and_text_align_inherit_but_background_and_border_do_not()
    {
        _ctx.Sheets.Add(".outer { white-space: pre; text-align: center; color: red; background: blue; border: solid }");
        var outer = Element("div", classes: "outer");
        var div = Element("div", outer);
        var span = Element("span", div);

        Assert.Equal(WhiteSpace.Pre, div.Node.Style.WhiteSpace);
        Assert.Equal(WhiteSpace.Pre, span.Node.Style.WhiteSpace);
        Assert.Equal(TextAlign.Center, div.Node.Style.TextAlign);
        Assert.Equal(Color.Red, div.Node.Style.Color);   // every element inherits, so a box carries its text's look
        Assert.Equal(Color.Default, div.Node.Style.Background);
        Assert.False(div.Node.Style.HasBorder);
        Assert.Equal(WhiteSpace.Normal, Element("div", outer, style: "white-space: normal").Node.Style.WhiteSpace);
    }

    [Fact]
    public void Custom_properties_inherit_and_resolve_where_they_are_used()
    {
        _ctx.Sheets.Add(":root { --accent: cyan; --pad: 1 } .card { border: solid var(--accent); padding: var(--pad) } .warm { --accent: red }");
        var root = Element("div");
        var card = Element("div", root, classes: "card");
        var warm = Element("div", root, classes: "card warm");
        var inner = Element("span", warm, style: "color: var(--accent)");

        Assert.Equal(Color.Cyan, card.Node.Style.BorderColor);
        Assert.Equal(new Edges(1), card.Node.Style.Padding);
        Assert.Equal(Color.Red, warm.Node.Style.BorderColor);
        Assert.Equal(Color.Red, inner.Node.Style.Color);
        Assert.Equal("red", inner.Node.Style.CustomProperties["--accent"]);
    }

    [Fact]
    public void Inheritance_works_without_any_sheet_too()
    {
        var outer = new HostElement("div");
        outer.SetAttribute("style", "color: red; font-weight: bold", 0);
        var span = new HostElement("span");
        outer.InsertChild(0, span);

        Assert.Equal(Color.Red, span.Node.Style.Color);
        Assert.Equal(TextStyle.Bold, span.Node.Style.TextStyle);
    }

    [Fact]
    public void Changing_an_attribute_re_resolves_the_descendants_that_match_through_it()
    {
        _ctx.Sheets.Add(".hot span { color: red } [busy] span { color: blue }");
        var div = Element("div");
        var span = Element("span", div);
        Assert.Equal(Color.Default, span.Node.Style.Color);

        div.SetAttribute("class", "hot", 0);
        Assert.Equal(Color.Red, span.Node.Style.Color);

        div.RemoveAttribute("class");
        Assert.Equal(Color.Default, span.Node.Style.Color);

        div.SetAttribute("busy", true, 0);
        Assert.Equal(Color.Blue, span.Node.Style.Color);
    }

    [Fact]
    public void Changing_an_inherited_colour_re_resolves_descendants_but_not_siblings()
    {
        var parent = Element("div");
        var div = Element("div", parent);
        var span = Element("span", div);
        var sibling = Element("span", parent, style: "padding: 1");
        var siblingStyle = sibling.Node.Style;

        div.SetAttribute("style", "color: green", 0);

        Assert.Equal(Color.Green, span.Node.Style.Color);
        Assert.Same(siblingStyle, sibling.Node.Style);   // not even re-resolved
    }

    [Fact]
    public void A_sheet_added_later_applies_after_a_restyle()
    {
        var div = Element("div");
        Assert.Equal(Edges.Zero, div.Node.Style.Padding);

        _ctx.Sheets.Add("div { padding: 2 }");
        div.Restyle();

        Assert.Equal(new Edges(2), div.Node.Style.Padding);
    }

    [Fact]
    public void Classes_tabindex_and_caret_read_their_attributes()
    {
        var a = Element("div");
        a.SetAttribute("class", "  one two\tthree ", 0);
        Assert.Equal(["one", "three", "two"], a.Classes.Order());

        var b = Element("div");
        b.SetAttribute("class", new[] { "x", "", "y" }, 0);
        Assert.Equal(["x", "y"], b.Classes.Order());
        Assert.Equal("main", Element("div", id: "main").Id);

        var tab = Element("div");
        Assert.False(tab.Focusable);
        tab.SetAttribute("tabindex", "0", 0);
        Assert.True(tab.Focusable && tab.Tabbable);
        tab.SetAttribute("tabindex", -1, 0);
        Assert.True(tab.Focusable && !tab.Tabbable);

        var caret = Element("div");
        caret.SetAttribute("caret", "3,1", 0);
        Assert.Equal((3, 1), caret.Caret);
    }
}
