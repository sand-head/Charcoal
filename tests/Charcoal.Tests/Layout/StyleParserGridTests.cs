using Charcoal.Layout;

namespace Charcoal.Tests.Layout;

public class StyleParserGridTests
{
    private static Style Apply(string name, object? value) => StyleParser.Apply(Style.Default, name, value);

    [Fact]
    public void Wrap_align_content_and_justify_items_read_their_enums()
    {
        Assert.Equal(FlexWrap.WrapReverse, Apply("flex-wrap", "wrap-reverse").FlexWrap);
        Assert.Equal(FlexWrap.Wrap, Apply("flex-wrap", FlexWrap.Wrap).FlexWrap);
        Assert.Equal(AlignContent.SpaceAround, Apply("align-content", "space-around").AlignContent);
        Assert.Equal(AlignItems.Center, Apply("justify-items", "center").JustifyItems);
        Assert.Equal(Overflow.Scroll, Apply("overflow", "scroll").Overflow);
        Assert.Equal(Display.Grid, Apply("display", "grid").Display);
    }

    [Fact]
    public void A_track_template_reads_cells_percent_fractions_auto_and_repeat()
    {
        var tracks = Apply("grid-template-columns", "20 25% 1.5fr auto repeat(2, 1fr 4)").GridTemplateColumns;
        Assert.Equal(
            [Track.Cells(20), Track.Percent(25), Track.Fr(1.5), Track.Auto, Track.Fr(1), Track.Cells(4), Track.Fr(1), Track.Cells(4)],
            tracks);
    }

    [Fact]
    public void A_track_template_round_trips_through_its_string()
    {
        var typed = new TrackList([Track.Cells(20), Track.Percent(25), Track.Fr(2), Track.Auto]);
        Assert.Equal("20 25% 2fr auto", typed.ToString());
        Assert.Equal(typed, Apply("grid-template-rows", typed.ToString()).GridTemplateRows);
        Assert.Same(typed, Apply("grid-template-rows", typed).GridTemplateRows);
    }

    [Theory]
    [InlineData("2", 2, 1)]
    [InlineData("2 / 4", 2, 2)]
    [InlineData("2 / span 3", 2, 3)]
    [InlineData("span 2", null, 2)]
    [InlineData("auto", null, 1)]
    public void Grid_column_reads_every_placement_form(string spelling, int? start, int span)
    {
        var style = Apply("grid-column", spelling);
        Assert.Equal(start, style.GridColumnStart);
        Assert.Equal(span, style.GridColumnSpan);
    }

    [Fact]
    public void Grid_row_end_sets_the_span_from_the_start()
    {
        var style = StyleParser.ApplyInline(Style.Default, "grid-row-start: 2; grid-row-end: 5");
        Assert.Equal(2, style.GridRowStart);
        Assert.Equal(3, style.GridRowSpan);
        Assert.Equal(2, StyleParser.ApplyInline(Style.Default, "grid-row-end: span 2").GridRowSpan);
    }

    [Fact]
    public void A_bad_track_or_placement_throws_naming_the_property()
    {
        var ex = Assert.Throws<FormatException>(() => Apply("grid-template-columns", "1fr 2px"));
        Assert.Contains("grid-template-columns", ex.Message);
        Assert.Throws<FormatException>(() => Apply("grid-column", "1 / 2 / 3"));
    }

    [Fact]
    public void The_grid_names_are_properties()
    {
        foreach (var name in new[] { "flex-wrap", "align-content", "justify-items",
                     "grid-template-columns", "grid-template-rows", "grid-column", "grid-row",
                     "grid-column-start", "grid-column-end", "grid-row-start", "grid-row-end" })
            Assert.True(StyleParser.IsProperty(name), name);
    }
}
