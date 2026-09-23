using Charcoal.Layout;
using Charcoal.Rendering;

namespace Charcoal.Tests.Rendering;

public class BordersTests
{
    private static string[] Rows(CellBuffer buffer) => buffer.ToString().Split('\n');

    [Theory]
    [InlineData("border: solid", "┌──┐", "│  │", "└──┘")]
    [InlineData("border: double", "╔══╗", "║  ║", "╚══╝")]
    [InlineData("border: solid; border-radius: 1", "╭──╮", "│  │", "╰──╯")]
    [InlineData("border: thick solid", "┏━━┓", "┃  ┃", "┗━━┛")]
    [InlineData("border-style: dashed", "+--+", "|  |", "+--+")]
    [InlineData("border: dotted", "+--+", "|  |", "+--+")]
    public void Each_style_draws_its_glyphs(string css, string top, string middle, string bottom)
    {
        var buffer = new CellBuffer(4, 3);
        Borders.Draw(buffer, new Rect(0, 0, 4, 3), StyleParser.ApplyInline(Style.Default, css));
        Assert.Equal([top, middle, bottom], Rows(buffer));
    }

    [Fact]
    public void None_draws_nothing()
    {
        var buffer = new CellBuffer(4, 3);
        Borders.Draw(buffer, new Rect(0, 0, 4, 3), new Style { BorderStyle = BorderStyle.None });
        Assert.Equal(["", "", ""], Rows(buffer));
    }

    [Fact]
    public void Only_a_top_side_is_a_rule_with_no_corners()
    {
        var buffer = new CellBuffer(4, 2);
        Borders.Draw(buffer, new Rect(0, 0, 4, 2), StyleParser.ApplyInline(Style.Default, "border-top: solid"));
        Assert.Equal(["────", ""], Rows(buffer));
    }

    [Fact]
    public void Only_a_left_side_is_a_vertical_with_no_corners()
    {
        var buffer = new CellBuffer(3, 3);
        Borders.Draw(buffer, new Rect(0, 0, 3, 3), StyleParser.ApplyInline(Style.Default, "border-left-style: solid"));
        Assert.Equal(["│", "│", "│"], Rows(buffer));
    }

    [Fact]
    public void Corners_appear_only_where_two_drawn_sides_meet()
    {
        var buffer = new CellBuffer(4, 3);
        Borders.Draw(buffer, new Rect(0, 0, 4, 3), StyleParser.ApplyInline(Style.Default, "border: solid; border-bottom: none"));
        Assert.Equal(["┌──┐", "│  │", "│  │"], Rows(buffer));
    }

    [Fact]
    public void Draws_inside_the_given_rect_only()
    {
        var buffer = new CellBuffer(6, 5);
        Borders.Draw(buffer, new Rect(1, 1, 4, 3), new Style { BorderStyle = BorderStyle.Solid });
        Assert.Equal(["", " ┌──┐", " │  │", " └──┘", ""], Rows(buffer));
    }

    [Fact]
    public void Uses_the_border_colour_over_the_background_and_the_text_colour_when_none_is_given()
    {
        var buffer = new CellBuffer(2, 2);
        Borders.Draw(buffer, new Rect(0, 0, 2, 2),
            new Style { BorderStyle = BorderStyle.Solid, BorderColor = Color.Red, Background = Color.Blue });
        var cell = buffer[0, 0];
        Assert.Equal(Color.Red, cell.Foreground);
        Assert.Equal(Color.Blue, cell.Background);

        // currentcolor: a border with no colour of its own takes the text's.
        buffer = new CellBuffer(2, 2);
        Borders.Draw(buffer, new Rect(0, 0, 2, 2), new Style { BorderStyle = BorderStyle.Solid, Color = Color.Green });
        Assert.Equal(Color.Green, buffer[0, 0].Foreground);
    }

    [Fact]
    public void Respects_the_clip()
    {
        var buffer = new CellBuffer(4, 3);
        buffer.PushClip(new Rect(0, 0, 4, 1));
        Borders.Draw(buffer, new Rect(0, 0, 4, 3), new Style { BorderStyle = BorderStyle.Solid });
        Assert.Equal(["┌──┐", "", ""], Rows(buffer));
    }
}
