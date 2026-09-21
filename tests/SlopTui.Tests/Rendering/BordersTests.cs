using SlopTui.Layout;
using SlopTui.Rendering;

namespace SlopTui.Tests.Rendering;

public class BordersTests
{
    private static string[] Rows(CellBuffer buffer) => buffer.ToString().Split('\n');

    [Theory]
    [InlineData(BorderStyle.Single, "┌──┐", "│  │", "└──┘")]
    [InlineData(BorderStyle.Double, "╔══╗", "║  ║", "╚══╝")]
    [InlineData(BorderStyle.Round, "╭──╮", "│  │", "╰──╯")]
    [InlineData(BorderStyle.Bold, "┏━━┓", "┃  ┃", "┗━━┛")]
    [InlineData(BorderStyle.Classic, "+--+", "|  |", "+--+")]
    public void Each_style_draws_its_glyphs(BorderStyle style, string top, string middle, string bottom)
    {
        var buffer = new CellBuffer(4, 3);
        Borders.Draw(buffer, new Rect(0, 0, 4, 3), new Style { Border = style });
        Assert.Equal([top, middle, bottom], Rows(buffer));
    }

    [Fact]
    public void None_draws_nothing()
    {
        var buffer = new CellBuffer(4, 3);
        Borders.Draw(buffer, new Rect(0, 0, 4, 3), new Style { Border = BorderStyle.None });
        Assert.Equal(["", "", ""], Rows(buffer));
    }

    [Fact]
    public void Only_a_top_side_is_a_rule_with_no_corners()
    {
        var buffer = new CellBuffer(4, 2);
        Borders.Draw(buffer, new Rect(0, 0, 4, 2),
            new Style { Border = BorderStyle.Single, BorderLeft = false, BorderRight = false, BorderBottom = false });
        Assert.Equal(["────", ""], Rows(buffer));
    }

    [Fact]
    public void Only_a_left_side_is_a_vertical_with_no_corners()
    {
        var buffer = new CellBuffer(3, 3);
        Borders.Draw(buffer, new Rect(0, 0, 3, 3),
            new Style { Border = BorderStyle.Single, BorderTop = false, BorderRight = false, BorderBottom = false });
        Assert.Equal(["│", "│", "│"], Rows(buffer));
    }

    [Fact]
    public void Corners_appear_only_where_two_drawn_sides_meet()
    {
        var buffer = new CellBuffer(4, 3);
        Borders.Draw(buffer, new Rect(0, 0, 4, 3),
            new Style { Border = BorderStyle.Single, BorderBottom = false });
        Assert.Equal(["┌──┐", "│  │", "│  │"], Rows(buffer));
    }

    [Fact]
    public void Draws_inside_the_given_rect_only()
    {
        var buffer = new CellBuffer(6, 5);
        Borders.Draw(buffer, new Rect(1, 1, 4, 3), new Style { Border = BorderStyle.Single });
        Assert.Equal(["", " ┌──┐", " │  │", " └──┘", ""], Rows(buffer));
    }

    [Fact]
    public void Uses_the_border_colour_over_the_background()
    {
        var buffer = new CellBuffer(2, 2);
        Borders.Draw(buffer, new Rect(0, 0, 2, 2),
            new Style { Border = BorderStyle.Single, BorderColor = Color.Red, Background = Color.Blue });
        var cell = buffer[0, 0];
        Assert.Equal(Color.Red, cell.Foreground);
        Assert.Equal(Color.Blue, cell.Background);
    }

    [Fact]
    public void Respects_the_clip()
    {
        var buffer = new CellBuffer(4, 3);
        buffer.PushClip(new Rect(0, 0, 4, 1));
        Borders.Draw(buffer, new Rect(0, 0, 4, 3), new Style { Border = BorderStyle.Single });
        Assert.Equal(["┌──┐", "", ""], Rows(buffer));
    }
}
