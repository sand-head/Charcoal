using System.Diagnostics;
using SlopTui.Layout;

namespace SlopTui.Tests.Layout;

public class AutoMinFollowUpTests
{
    private static readonly Style Row = new() { Display = Display.Flex, FlexDirection = FlexDirection.Row };
    private static readonly Style Column = new() { Display = Display.Flex, FlexDirection = FlexDirection.Column };

    private static TextLeafNode Leaf(int width, int height, Style? style = null) => new(width, height, style);

    /// <summary>A leaf that wraps words like text: widest line in the space across, lines down, longest word as its min.</summary>
    private sealed class Words : LayoutNode
    {
        private readonly string[] _words;
        public int Measures;

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
            Measures++;
            var limit = Math.Max(1, availableWidth ?? int.MaxValue);
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
    public void A_visible_column_around_a_hidden_transcript_shrinks_to_the_viewport_and_keeps_its_chrome()
    {
        // The Transcript example's shape: root → app column (visible, grow) →
        // header, transcript column (hidden, grow, 200 rows), composer, status.
        var header = Leaf(10, 1);
        var transcript = new BoxNode(Column with { Overflow = Overflow.Hidden, FlexGrow = 1 });
        for (var i = 0; i < 200; i++) transcript.Add(Leaf(10, 1));
        var composer = new BoxNode(Row with { BorderStyle = BorderStyle.Solid }, Leaf(5, 1));
        var status = Leaf(10, 1);
        var app = new BoxNode(Column with { FlexGrow = 1 }, header, transcript, composer, status);
        var root = new BoxNode(Column, app);

        FlexLayout.Layout(root, new Size(40, 14));

        Assert.Equal(14, app.Layout.Height);
        Assert.Equal(9, transcript.Layout.Height);
        Assert.Equal(new Rect(0, 10, 40, 3), composer.Layout);
        Assert.Equal(new Rect(0, 13, 40, 1), status.Layout);
        Assert.True(status.Layout.Bottom <= 14);
    }

    [Fact]
    public void A_row_measures_as_tall_as_its_texts_wrap_at_their_shrunk_widths()
    {
        var first = new Words("short words wrap freely here");
        var second = new Words("an unbreakableidentifier holds");
        var a = new BoxNode(Row with { FlexGrow = 1 }, first);
        var b = new BoxNode(Row with { FlexGrow = 1 }, second);
        var row = new BoxNode(Row with { ColumnGap = 1 }, a, b);

        Assert.Equal(5, FlexLayout.Measure(row, 30, null).Height);

        var column = new BoxNode(Column, row, Leaf(5, 1));
        FlexLayout.Layout(column, new Size(30, 20));

        // Shrink is weighted by basis: the second box freezes at its longest
        // word (21) and the first takes what is left (29 - 21 = 8), as CSS.
        Assert.Equal(5, row.Layout.Height);
        Assert.Equal(8, a.Layout.Width);
        Assert.Equal(21, b.Layout.Width);
        // The texts stretch to the row's height (align-items: stretch); their
        // own line counts are what the widths wrap them to.
        Assert.Equal(5, first.Layout.Height);
        Assert.Equal(5, FlexLayout.Measure(first, 8, null).Height);    // short / words / wrap / freely / here
        Assert.Equal(3, FlexLayout.Measure(second, 21, null).Height);  // an / unbreakableidentifier / holds
        Assert.Equal(5, column.Children[1].Layout.Y);
    }

    [Fact]
    public void One_streamed_change_in_a_long_list_costs_one_row_not_the_list()
    {
        var transcript = new BoxNode(Column with { Overflow = Overflow.Hidden, FlexGrow = 1, JustifyContent = JustifyContent.FlexEnd });
        var words = new List<Words>();
        for (var i = 0; i < 10_000; i++)
        {
            var text = new Words("some words in a row " + i, new Style { FlexGrow = 1 });
            words.Add(text);
            transcript.Add(new BoxNode(Row with { ColumnGap = 1 }, Leaf(5, 1), text));
        }
        var app = new BoxNode(Column with { FlexGrow = 1 }, Leaf(10, 1), transcript, Leaf(10, 1));
        var root = new BoxNode(Column, app);
        FlexLayout.Layout(root, new Size(120, 40));

        var before = words.Sum(w => w.Measures);
        words[^1].InvalidateLayout();
        var sw = Stopwatch.StartNew();
        FlexLayout.Layout(root, new Size(120, 40));
        sw.Stop();

        var measured = words.Sum(w => w.Measures) - before;
        Assert.True(measured <= 4, $"{measured} texts were re-measured for one change");
        Assert.True(sw.ElapsedMilliseconds < 50, $"relayout took {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void The_automatic_minimum_is_answered_from_cache_on_a_clean_node()
    {
        var words = new Words("hello there");
        var row = new BoxNode(Row, words, Leaf(20, 1));
        FlexLayout.Layout(row, new Size(10, 2));
        var first = FlexLayout.AutomaticMinimum(words, true, 10, 2);
        var measures = words.Measures;

        Assert.Equal(first, FlexLayout.AutomaticMinimum(words, true, 10, 2));
        Assert.Equal(measures, words.Measures);
        Assert.Equal(5, first);
    }
}
