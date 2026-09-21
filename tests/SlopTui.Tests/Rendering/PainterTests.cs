using SlopTui.Layout;
using SlopTui.Rendering;

namespace SlopTui.Tests.Rendering;

public class PainterTests
{
    /// <summary>A text leaf that carries its runs; layout is assigned by hand.</summary>
    private sealed class TextNode : LayoutNode, ITextContent
    {
        public TextNode(Style style, params TextRun[] runs)
        {
            Style = style;
            Runs = runs;
        }

        public TextNode(Style style, string text) : this(style, new TextRun(text)) { }
        public override IReadOnlyList<LayoutNode> Children => [];
        public IReadOnlyList<TextRun> Runs { get; }
        public Rect At { init => Layout = value; }
    }

    private sealed class CanvasNode(Action<CellBuffer, Rect> paint) : LayoutNode, ICustomPaint
    {
        public override IReadOnlyList<LayoutNode> Children => [];
        public void Paint(CellBuffer buffer, Rect rect) => paint(buffer, rect);
        public Rect At { init => Layout = value; }
    }

    private static BoxNode Box(Style style, Rect at, params LayoutNode[] children)
    {
        var box = new BoxNode(style, children) { };
        Place(box, at);
        return box;
    }

    private static void Place(LayoutNode node, Rect at) => node.Layout = at;

    private static string[] Rows(CellBuffer buffer) => buffer.ToString().Split('\n');

    [Fact]
    public void A_box_without_a_background_paints_nothing()
    {
        var buffer = new CellBuffer(4, 2);
        Painter.Paint(Box(new Style(), new Rect(0, 0, 4, 2)), buffer);
        Assert.All(Rows(buffer), row => Assert.Equal("", row));
        Assert.Equal(Cell.Blank, buffer[0, 0]);
    }

    [Fact]
    public void A_box_with_a_background_fills_its_rect()
    {
        var buffer = new CellBuffer(4, 2);
        Painter.Paint(Box(new Style { Background = Color.Red }, new Rect(1, 0, 2, 2)), buffer);
        Assert.Equal(Color.Default, buffer[0, 0].Background);
        Assert.Equal(Color.Red, buffer[1, 0].Background);
        Assert.Equal(Color.Red, buffer[2, 1].Background);
        Assert.Equal(Color.Default, buffer[3, 1].Background);
    }

    [Fact]
    public void A_child_without_a_background_shows_the_parent_fill()
    {
        var buffer = new CellBuffer(4, 1);
        var child = Box(new Style(), new Rect(1, 0, 2, 1));
        var parent = Box(new Style { Background = Color.Blue }, new Rect(0, 0, 4, 1), child);
        Painter.Paint(parent, buffer);
        Assert.Equal(Color.Blue, buffer[1, 0].Background);
    }

    [Fact]
    public void The_border_is_drawn_inside_the_rect()
    {
        var buffer = new CellBuffer(4, 3);
        Painter.Paint(Box(new Style { Border = BorderStyle.Single }, new Rect(0, 0, 4, 3)), buffer);
        Assert.Equal(["┌──┐", "│  │", "└──┘"], Rows(buffer));
    }

    [Fact]
    public void Children_are_clipped_to_the_box_inside_its_border()
    {
        var buffer = new CellBuffer(6, 3);
        var child = new TextNode(new Style(), "abcdefgh") { At = new Rect(1, 1, 8, 1) };
        var parent = Box(new Style { Border = BorderStyle.Single }, new Rect(0, 0, 6, 3), child);
        Painter.Paint(parent, buffer);
        Assert.Equal("│abcd│", Rows(buffer)[1]);
    }

    [Fact]
    public void Overflow_visible_lets_children_escape()
    {
        var buffer = new CellBuffer(8, 1);
        var child = new TextNode(new Style(), "abcdefgh") { At = new Rect(0, 0, 8, 1) };
        var parent = Box(new Style { Overflow = Overflow.Visible }, new Rect(0, 0, 4, 1), child);
        Painter.Paint(parent, buffer);
        Assert.Equal("abcdefgh", Rows(buffer)[0]);
    }

    [Fact]
    public void Overflow_hidden_clips_children_to_the_box()
    {
        var buffer = new CellBuffer(8, 1);
        var child = new TextNode(new Style(), "abcdefgh") { At = new Rect(0, 0, 8, 1) };
        var parent = Box(new Style(), new Rect(0, 0, 4, 1), child);
        Painter.Paint(parent, buffer);
        Assert.Equal("abcd", Rows(buffer)[0]);
    }

    [Fact]
    public void Nested_clips_intersect()
    {
        var buffer = new CellBuffer(8, 1);
        var text = new TextNode(new Style(), "abcdefgh") { At = new Rect(0, 0, 8, 1) };
        var inner = Box(new Style(), new Rect(0, 0, 6, 1), text);
        var outer = Box(new Style(), new Rect(0, 0, 3, 1), inner);
        Painter.Paint(outer, buffer);
        Assert.Equal("abc", Rows(buffer)[0]);
    }

    [Fact]
    public void Text_wraps_inside_its_content_box_and_honours_padding()
    {
        var buffer = new CellBuffer(8, 3);
        var text = new TextNode(new Style { Padding = new Edges(1) }, "ab cd")
        {
            At = new Rect(0, 0, 4, 3),
        };
        Painter.Paint(text, buffer);
        Assert.Equal(["", " ab", ""], Rows(buffer));
    }

    [Fact]
    public void Text_lines_past_the_content_box_are_dropped()
    {
        var buffer = new CellBuffer(4, 1);
        var text = new TextNode(new Style(), "ab\ncd") { At = new Rect(0, 0, 4, 1) };
        Painter.Paint(text, buffer);
        Assert.Equal(["ab"], Rows(buffer));
    }

    [Fact]
    public void Unstyled_runs_inherit_the_element_colour_and_styled_runs_keep_theirs()
    {
        var buffer = new CellBuffer(4, 1);
        var text = new TextNode(
            new Style { Color = Color.Green, Background = Color.Black, TextStyle = TextStyle.Bold },
            new TextRun("a"),
            new TextRun("b", Color.Red, Color.Default, TextStyle.Underline))
        {
            At = new Rect(0, 0, 4, 1),
        };
        Painter.Paint(text, buffer);
        Assert.Equal(Color.Green, buffer[0, 0].Foreground);
        Assert.Equal(Color.Black, buffer[0, 0].Background);
        Assert.Equal(TextStyle.Bold, buffer[0, 0].Style);
        Assert.Equal(Color.Red, buffer[1, 0].Foreground);
        Assert.Equal(Color.Black, buffer[1, 0].Background);
        Assert.Equal(TextStyle.Bold | TextStyle.Underline, buffer[1, 0].Style);
    }

    [Fact]
    public void A_text_background_fills_the_whole_rect_not_just_the_glyphs()
    {
        var buffer = new CellBuffer(4, 1);
        var text = new TextNode(new Style { Background = Color.Red }, "a") { At = new Rect(0, 0, 4, 1) };
        Painter.Paint(text, buffer);
        Assert.Equal(Color.Red, buffer[3, 0].Background);
    }

    [Fact]
    public void A_custom_leaf_paints_inside_its_content_box_and_cannot_escape()
    {
        var buffer = new CellBuffer(6, 1);
        Rect given = default;
        var canvas = new CanvasNode((b, rect) =>
        {
            given = rect;
            b.PutText(0, 0, "abcdef", Color.Default, Color.Default, TextStyle.None);
        })
        {
            At = new Rect(1, 0, 3, 1),
            Style = new Style { Padding = new Edges(0, 1) },
        };
        Painter.Paint(canvas, buffer);
        Assert.Equal(new Rect(2, 0, 1, 1), given);
        Assert.Equal("", Rows(buffer)[0].Trim());   // (0,0) is outside the clip; nothing landed
    }

    [Fact]
    public void A_custom_leaf_gets_absolute_coordinates()
    {
        var buffer = new CellBuffer(6, 2);
        var canvas = new CanvasNode((b, rect) =>
            b.PutText(rect.X, rect.Y, "xy", Color.Default, Color.Default, TextStyle.None))
        {
            At = new Rect(2, 1, 3, 1),
        };
        Painter.Paint(canvas, buffer);
        Assert.Equal(["", "  xy"], Rows(buffer));
    }

    [Fact]
    public void Display_none_paints_nothing_including_children()
    {
        var buffer = new CellBuffer(4, 1);
        var child = new TextNode(new Style(), "abcd") { At = new Rect(0, 0, 4, 1) };
        var parent = Box(new Style { Display = Display.None, Background = Color.Red }, new Rect(0, 0, 4, 1), child);
        Painter.Paint(parent, buffer);
        Assert.Equal(Cell.Blank, buffer[0, 0]);
    }

    [Fact]
    public void An_empty_rect_paints_nothing()
    {
        var buffer = new CellBuffer(4, 1);
        Painter.Paint(Box(new Style { Background = Color.Red }, new Rect(0, 0, 0, 1)), buffer);
        Assert.Equal(Cell.Blank, buffer[0, 0]);
    }

    [Fact]
    public void Later_siblings_paint_over_earlier_ones()
    {
        var buffer = new CellBuffer(4, 1);
        var first = Box(new Style { Background = Color.Red }, new Rect(0, 0, 4, 1));
        var second = Box(new Style { Background = Color.Blue }, new Rect(1, 0, 2, 1));
        var parent = Box(new Style(), new Rect(0, 0, 4, 1), first, second);
        Painter.Paint(parent, buffer);
        Assert.Equal(Color.Red, buffer[0, 0].Background);
        Assert.Equal(Color.Blue, buffer[1, 0].Background);
        Assert.Equal(Color.Red, buffer[3, 0].Background);
    }

    [Fact]
    public void A_child_outside_the_clip_is_not_painted_at_all()
    {
        var painted = 0;
        var seen = new CanvasNode((_, _) => painted++);
        var hidden = new CanvasNode((_, _) => painted++);
        var column = new BoxNode(new Style { FlexDirection = FlexDirection.Column, JustifyContent = JustifyContent.FlexEnd, Height = 2 }, hidden, seen);
        hidden.Style = new Style { Height = 2, FlexShrink = 0 };
        seen.Style = new Style { Height = 2, FlexShrink = 0 };
        FlexLayout.Layout(column, new Size(10, 2));
        Assert.True(hidden.Layout.Bottom <= 0);

        Painter.Paint(column, new CellBuffer(10, 2));

        Assert.Equal(1, painted);
    }
}
