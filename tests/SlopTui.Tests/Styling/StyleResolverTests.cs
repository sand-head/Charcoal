using SlopTui.Components;
using SlopTui.Layout;
using SlopTui.Rendering;
using SlopTui.Styling;

namespace SlopTui.Tests.Styling;

public class StyleResolverTests
{
    private readonly StyleContext _ctx = new();

    private HostElement Element(string name, HostElement? parent = null, string? classes = null, string? id = null)
    {
        var element = new HostElement(name, null, _ctx);
        parent?.InsertChild(parent.Children.Count, element);
        if (classes is not null) element.SetAttribute("class", classes, 0);
        if (id is not null) element.SetAttribute("id", id, 0);
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
    public void Type_class_id_and_star_match_what_they_name()
    {
        var box = Element("box", classes: "a b", id: "main");
        var text = Element("text", box);

        Assert.True(StyleResolver.Matches(box, Selector.Parse("box")));
        Assert.True(StyleResolver.Matches(box, Selector.Parse("*")));
        Assert.True(StyleResolver.Matches(box, Selector.Parse(".a")));
        Assert.True(StyleResolver.Matches(box, Selector.Parse(".b.a")));
        Assert.True(StyleResolver.Matches(box, Selector.Parse("#main")));
        Assert.True(StyleResolver.Matches(box, Selector.Parse("box#main.a")));
        Assert.False(StyleResolver.Matches(box, Selector.Parse("text")));
        Assert.False(StyleResolver.Matches(box, Selector.Parse(".c")));
        Assert.False(StyleResolver.Matches(text, Selector.Parse("#main")));
    }

    [Fact]
    public void Descendant_and_child_combinators_walk_elements_through_containers()
    {
        var root = Element("box", classes: "root");
        var mid = Element("box", root, classes: "mid");
        var container = Container(mid);
        var text = new HostElement("text", null, _ctx);
        container.InsertChild(0, text);

        Assert.True(StyleResolver.Matches(text, Selector.Parse(".root text")));
        Assert.True(StyleResolver.Matches(text, Selector.Parse(".mid > text")));   // the component container between does not count
        Assert.False(StyleResolver.Matches(text, Selector.Parse(".root > text")));
        Assert.True(StyleResolver.Matches(text, Selector.Parse(".root > .mid > text")));
        Assert.False(StyleResolver.Matches(mid, Selector.Parse(".mid text")));
    }

    [Fact]
    public void Focus_pseudo_classes_follow_the_context()
    {
        var outer = Element("box");
        var inner = Element("box", outer);
        var text = Element("text", inner);
        _ctx.Focused = inner;

        Assert.True(StyleResolver.Matches(inner, Selector.Parse("box:focus"), inner));
        Assert.False(StyleResolver.Matches(outer, Selector.Parse("box:focus"), inner));
        Assert.True(StyleResolver.Matches(outer, Selector.Parse("box:focus-within"), inner));
        Assert.True(StyleResolver.Matches(inner, Selector.Parse("box:focus-within"), inner));
        Assert.False(StyleResolver.Matches(text, Selector.Parse(":focus-within"), inner));
        Assert.True(StyleResolver.Matches(text, Selector.Parse("box:focus > text"), inner));
    }

    [Fact]
    public void The_cascade_applies_lower_specificity_first_so_the_higher_wins()
    {
        _ctx.Sheets.Add("box { padding: 1; gap: 3 } .wide { padding: 4 } #main { gap: 9 }");
        var box = Element("box", classes: "wide", id: "main");

        Assert.Equal(new Edges(4), box.Node.Style.Padding);
        Assert.Equal(9, box.Node.Style.RowGap);
    }

    [Fact]
    public void At_equal_specificity_the_later_rule_and_the_later_sheet_win()
    {
        _ctx.Sheets.Add(".a { padding: 1 } .a { padding: 2 }");
        _ctx.Sheets.Add(".a { gap: 5 }");
        _ctx.Sheets.Add(".a { gap: 6 }");
        var box = Element("box", classes: "a");

        Assert.Equal(new Edges(2), box.Node.Style.Padding);
        Assert.Equal(6, box.Node.Style.RowGap);
    }

    [Fact]
    public void A_selector_list_takes_the_specificity_of_the_selector_that_matched()
    {
        _ctx.Sheets.Add("#main, text { padding: 5 } box.a { padding: 2 }");
        var box = Element("box", classes: "a", id: "main");

        // "#main, text" matched through #main (10000), which beats box.a (101).
        Assert.Equal(new Edges(5), box.Node.Style.Padding);
    }

    [Fact]
    public void An_inline_attribute_and_inline_style_beat_every_sheet()
    {
        _ctx.Sheets.Add("#main { padding: 5; gap: 5; color: red }");
        var box = Element("box", id: "main");
        box.SetAttribute("padding", "1", 0);
        box.SetAttribute("style", "gap: 2", 0);

        Assert.Equal(new Edges(1), box.Node.Style.Padding);
        Assert.Equal(2, box.Node.Style.RowGap);
        Assert.Equal(Color.Red, box.Node.Style.Color);   // untouched by the element: the sheet's
    }

    [Fact]
    public void An_unknown_property_in_a_sheet_is_skipped_at_resolve_time()
    {
        _ctx.Sheets.Add("box { font-family: mono; padding: 3 }");
        var box = Element("box");

        Assert.Equal(new Edges(3), box.Node.Style.Padding);
    }

    [Fact]
    public void A_text_inherits_the_nearest_ancestors_colour_and_accumulates_flags()
    {
        _ctx.Sheets.Add(".outer { color: red; bold: true } .inner { color: green; italic: true }");
        var outer = Element("box", classes: "outer");
        var inner = Element("box", outer, classes: "inner");
        var text = Element("text", inner);
        var own = Element("text", inner);
        own.SetAttribute("color", "blue", 0);

        Assert.Equal(Color.Green, text.Node.Style.Color);
        Assert.Equal(TextStyle.Bold | TextStyle.Italic, text.Node.Style.TextStyle);
        Assert.Equal(Color.Blue, own.Node.Style.Color);
        Assert.Equal(TextStyle.Bold | TextStyle.Italic, own.Node.Style.TextStyle);
    }

    [Fact]
    public void Wrap_is_not_inherited_and_a_box_does_not_inherit_at_all()
    {
        _ctx.Sheets.Add(".outer { wrap: clip; color: red }");
        var outer = Element("box", classes: "outer");
        var box = Element("box", outer);
        var text = Element("text", box);

        Assert.Equal(TextWrap.Wrap, text.Node.Style.Wrap);
        Assert.Equal(Color.Default, box.Node.Style.Color);
        Assert.Equal(Color.Red, text.Node.Style.Color);
    }

    [Fact]
    public void Inheritance_works_without_any_sheet_too()
    {
        var outer = new HostElement("box");
        outer.SetAttribute("color", "red", 0);
        outer.SetAttribute("bold", true, 0);
        var text = new HostElement("text");
        outer.InsertChild(0, text);
        text.Restyle();

        Assert.Equal(Color.Red, text.Node.Style.Color);
        Assert.Equal(TextStyle.Bold, text.Node.Style.TextStyle);
    }

    [Fact]
    public void Changing_a_class_re_resolves_the_descendants_that_match_through_it()
    {
        _ctx.Sheets.Add(".hot text { color: red }");
        var box = Element("box");
        var text = Element("text", box);
        Assert.Equal(Color.Default, text.Node.Style.Color);

        box.SetAttribute("class", "hot", 0);
        Assert.Equal(Color.Red, text.Node.Style.Color);

        box.RemoveAttribute("class");
        Assert.Equal(Color.Default, text.Node.Style.Color);
    }

    [Fact]
    public void Changing_an_inherited_colour_re_resolves_descendant_text_but_not_siblings()
    {
        var parent = Element("box");
        var box = Element("box", parent);
        var text = Element("text", box);
        var sibling = Element("text", parent);
        sibling.SetAttribute("padding", "1", 0);
        var siblingStyle = sibling.Node.Style;

        box.SetAttribute("color", "green", 0);

        Assert.Equal(Color.Green, text.Node.Style.Color);
        Assert.Same(siblingStyle, sibling.Node.Style);   // not even re-resolved
    }

    [Fact]
    public void A_sheet_added_later_applies_after_a_restyle()
    {
        var box = Element("box");
        Assert.Equal(Edges.Zero, box.Node.Style.Padding);

        _ctx.Sheets.Add("box { padding: 2 }");
        box.Restyle();

        Assert.Equal(new Edges(2), box.Node.Style.Padding);
    }

    [Fact]
    public void Classes_accept_a_string_or_a_sequence()
    {
        var a = Element("box");
        a.SetAttribute("class", "  one two\tthree ", 0);
        Assert.Equal(["one", "three", "two"], a.Classes.Order());

        var b = Element("box");
        b.SetAttribute("class", new[] { "x", "", "y" }, 0);
        Assert.Equal(["x", "y"], b.Classes.Order());
        Assert.Equal("main", Element("box", id: "main").Id);
    }
}
