using SlopTui.Layout;

namespace SlopTui.Tests.Layout;

public class AutoMinAndScrollTests
{
    private static readonly Style Row = new() { FlexDirection = FlexDirection.Row };
    private static readonly Style Column = new() { FlexDirection = FlexDirection.Column };

    private static TextLeafNode Leaf(int width, int height, Style? style = null) => new(width, height, style);

    /// <summary>
    /// A leaf that wraps words the way text does: its width is the longest
    /// line it can make in the space, its min-content width the longest word.
    /// </summary>
    private sealed class Words : LayoutNode
    {
        private readonly string[] _words;

        public Words(string text, Style? style = null)
        {
            _words = text.Split(' ');
            if (style is not null) Style = style;
        }

        public override IReadOnlyList<LayoutNode> Children => [];
        public override bool IsLeaf => true;
        public override int MinContentWidth() => _words.Max(w => w.Length);

        public override Size MeasureContent(int? availableWidth, int? availableHeight)
        {
            var limit = availableWidth ?? int.MaxValue;
            var lines = 1;
            var line = 0;
            var widest = 0;
            foreach (var word in _words)
            {
                var needed = line == 0 ? word.Length : line + 1 + word.Length;
                if (line > 0 && needed > limit) { lines++; line = word.Length; }
                else line = needed;
                widest = Math.Max(widest, Math.Min(line, limit));
            }
            return new Size(widest, lines);
        }
    }

    [Fact]
    public void A_header_beside_an_overflowing_column_keeps_its_height()
    {
        var header = Leaf(5, 1);
        var transcript = new BoxNode(Column with { Overflow = Overflow.Hidden });
        for (var i = 0; i < 20; i++) transcript.Add(Leaf(5, 1));
        var footer = Leaf(5, 1);
        var root = new BoxNode(Column, header, transcript, footer);

        FlexLayout.Layout(root, new Size(20, 10));

        Assert.Equal(1, header.Layout.Height);
        Assert.Equal(8, transcript.Layout.Height);
        Assert.Equal(1, footer.Layout.Height);
        Assert.Equal(9, footer.Layout.Y);
    }

    [Fact]
    public void A_clipping_box_gives_way_to_a_sibling_that_cannot_shrink()
    {
        var hidden = new BoxNode(Row with { Overflow = Overflow.Hidden }, Leaf(8, 1));
        var word = new Words("abcdefghij");
        var root = new BoxNode(Row, hidden, word);

        FlexLayout.Layout(root, new Size(10, 1));

        Assert.Equal(0, hidden.Layout.Width);
        Assert.Equal(10, word.Layout.Width);
    }

    [Fact]
    public void Text_in_a_row_shrinks_to_its_longest_word_and_no_further()
    {
        var first = new Words("hello world");
        var second = new Words("foo bar baz");
        var root = new BoxNode(Row, first, second);

        FlexLayout.Layout(root, new Size(8, 3));

        Assert.Equal(5, first.Layout.Width);
        Assert.Equal(3, second.Layout.Width);
    }

    [Fact]
    public void An_explicit_minimum_replaces_the_automatic_one()
    {
        var first = new Words("hello", new Style { MinWidth = 2 });
        var second = new Words("abcdefghij");
        var root = new BoxNode(Row, first, second);

        FlexLayout.Layout(root, new Size(10, 1));

        // Both minimums hold, so the row overflows by two: the explicit two
        // and the automatic ten, as CSS resolves it.
        Assert.Equal(2, first.Layout.Width);
        Assert.Equal(10, second.Layout.Width);
    }

    [Fact]
    public void A_visible_box_in_a_row_is_at_least_its_children_side_by_side()
    {
        var box = new BoxNode(Row with { ColumnGap = 1 }, new Words("ab"), new Words("cd"));
        var other = Leaf(20, 1);
        var root = new BoxNode(Row, box, other);

        FlexLayout.Layout(root, new Size(10, 1));

        Assert.Equal(5, box.Layout.Width);   // 2 + gap + 2
        Assert.Equal(5, other.Layout.Width);
    }

    [Fact]
    public void A_wrapping_visible_box_is_at_least_its_widest_child()
    {
        var box = new BoxNode(Row with { FlexWrap = FlexWrap.Wrap }, new Words("abcd"), new Words("ef"));
        Assert.Equal(4, FlexLayout.MinContentWidth(box));
    }

    [Fact]
    public void A_max_width_caps_the_automatic_minimum()
    {
        var word = new Words("abcdefghij", new Style { MaxWidth = 6 });
        var other = Leaf(10, 1);
        var root = new BoxNode(Row, word, other);

        FlexLayout.Layout(root, new Size(10, 1));

        Assert.Equal(6, word.Layout.Width);
    }

    [Fact]
    public void A_scroll_offset_shifts_a_column_up()
    {
        var root = new BoxNode(Column with { Overflow = Overflow.Scroll, ScrollY = 3 });
        for (var i = 0; i < 10; i++) root.Add(Leaf(5, 1));

        FlexLayout.Layout(root, new Size(10, 5));

        Assert.Equal(-3, root.Children[0].Layout.Y);
        Assert.Equal(0, root.Children[3].Layout.Y);
        Assert.Equal(10, root.ContentSize.Height);
    }

    [Fact]
    public void Scrolled_away_children_are_deferred_and_visible_ones_arranged()
    {
        var root = new BoxNode(Column with { Overflow = Overflow.Scroll, ScrollY = 3 });
        for (var i = 0; i < 10; i++) root.Add(new BoxNode(Row, Leaf(2, 1)));

        FlexLayout.Layout(root, new Size(10, 5));

        Assert.True(root.Children[0].ArrangeDeferred);
        Assert.False(root.Children[3].ArrangeDeferred);
        Assert.Equal(0, root.Children[3].Children[0].Layout.Y);
        Assert.True(root.Children[9].ArrangeDeferred);   // below the box
    }

    [Fact]
    public void A_scroll_offset_shifts_a_row_left()
    {
        // Rigid items (shrink 0), or the scroll box would shrink them to fit instead of scrolling.
        var rigid = new Style { FlexShrink = 0 };
        var root = new BoxNode(Row with { Overflow = Overflow.Scroll, ScrollX = 2 }, Leaf(4, 1, rigid), Leaf(4, 1, rigid));
        FlexLayout.Layout(root, new Size(6, 1));

        Assert.Equal(-2, root.Children[0].Layout.X);
        Assert.Equal(2, root.Children[1].Layout.X);
        Assert.Equal(8, root.ContentSize.Width);
    }

    [Fact]
    public void A_visible_box_has_nothing_to_scroll()
    {
        var root = new BoxNode(Column with { ScrollY = 3 }, Leaf(5, 1), Leaf(5, 1));
        FlexLayout.Layout(root, new Size(10, 5));
        Assert.Equal(0, root.Children[0].Layout.Y);
    }

    [Fact]
    public void Changing_the_offset_rearranges()
    {
        var root = new BoxNode(Column with { Overflow = Overflow.Scroll, ScrollY = 0 }, Leaf(5, 1), Leaf(5, 1), Leaf(5, 1));
        FlexLayout.Layout(root, new Size(10, 2));
        Assert.Equal(0, root.Children[0].Layout.Y);

        root.Style = root.Style with { ScrollY = 1 };
        FlexLayout.Layout(root, new Size(10, 2));

        Assert.Equal(-1, root.Children[0].Layout.Y);
        Assert.Equal(1, root.Children[2].Layout.Y);
    }
}
