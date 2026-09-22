using SlopTui.Styling;

namespace SlopTui.Tests.Styling;

public class MediaQueryTests
{
    private static readonly MediaEnvironment Wide = new() { Width = 120, Height = 40 };
    private static readonly MediaEnvironment Narrow = new() { Width = 60, Height = 40 };
    private static readonly MediaEnvironment Tall = new() { Width = 40, Height = 60 };

    private static bool Matches(string query, MediaEnvironment env) => MediaQueryList.Parse(query).Matches(env);

    [Theory]
    [InlineData("(min-width: 80)", true, false)]
    [InlineData("(max-width: 80)", false, true)]
    [InlineData("(width >= 80)", true, false)]
    [InlineData("(width < 80)", false, true)]
    [InlineData("(80 <= width <= 200)", true, false)]
    [InlineData("(50 < width < 100)", false, true)]
    [InlineData("(200 > width > 100)", true, false)]
    [InlineData("(width: 120)", true, false)]
    [InlineData("(min-width: 80ch)", true, false)]
    [InlineData("(min-height: 30lh)", true, true)]
    [InlineData("screen and (min-width: 80)", true, false)]
    [InlineData("only screen and (min-width: 80)", true, false)]
    [InlineData("not screen and (min-width: 80)", false, true)]
    [InlineData("not (min-width: 80)", false, true)]
    [InlineData("(min-width: 80), (max-width: 70)", true, true)]
    [InlineData("(min-width: 80) or (max-width: 70)", true, true)]
    [InlineData("(min-width: 80) and (max-height: 50)", true, false)]
    [InlineData("((min-width: 80) or (max-width: 70)) and (min-height: 10)", true, true)]
    [InlineData("print", false, false)]
    [InlineData("all", true, true)]
    [InlineData("tty", false, false)]
    public void Sizes_types_and_logic(string query, bool wide, bool narrow)
    {
        Assert.Equal(wide, Matches(query, Wide));
        Assert.Equal(narrow, Matches(query, Narrow));
    }

    [Fact]
    public void Orientation_and_aspect_ratio_follow_the_shape()
    {
        Assert.True(Matches("(orientation: landscape)", Wide));
        Assert.False(Matches("(orientation: portrait)", Wide));
        Assert.True(Matches("(orientation: portrait)", Tall));
        Assert.True(Matches("(orientation: portrait)", new MediaEnvironment { Width = 40, Height = 40 }));   // square is portrait, as CSS has it
        Assert.True(Matches("(min-aspect-ratio: 2/1)", Wide));
        Assert.False(Matches("(min-aspect-ratio: 4/1)", Wide));
        Assert.True(Matches("(aspect-ratio: 3/1)", Wide));
        Assert.True(Matches("(aspect-ratio <= 1)", Tall));
    }

    [Fact]
    public void The_terminal_answers_the_environment_features()
    {
        var env = new MediaEnvironment { ColorScheme = ColorScheme.Light, ColorBits = 4, Pointer = false, ReducedMotion = true };
        Assert.True(Matches("(prefers-color-scheme: light)", env));
        Assert.False(Matches("(prefers-color-scheme: dark)", env));
        Assert.True(Matches("(prefers-color-scheme: dark)", Wide));
        Assert.True(Matches("(color)", env));
        Assert.True(Matches("(min-color: 4)", env));
        Assert.False(Matches("(min-color: 8)", env));
        Assert.True(Matches("(color >= 8)", Wide));
        Assert.False(Matches("(monochrome)", env));
        Assert.True(Matches("(grid)", env));
        Assert.True(Matches("(grid: 1)", env));
        Assert.False(Matches("(hover: hover)", env));
        Assert.True(Matches("(hover: none)", env));
        Assert.True(Matches("(pointer: none)", env));
        Assert.True(Matches("(pointer: fine)", Wide));
        Assert.False(Matches("(pointer)", env));
        Assert.True(Matches("(prefers-reduced-motion: reduce)", env));
        Assert.True(Matches("(prefers-reduced-motion: no-preference)", Wide));
        Assert.True(Matches("(display-mode: fullscreen)", env));
        Assert.False(Matches("(display-mode: browser)", env));
        Assert.True(Matches("(prefers-contrast: no-preference)", env));
        Assert.True(Matches("(forced-colors: none)", env));
    }

    [Fact]
    public void An_unknown_feature_or_a_pixel_length_is_unknown_and_makes_the_query_false_not_an_error()
    {
        var pixels = MediaQueryList.Parse("(min-width: 768px)");
        Assert.Equal(MediaResult.Unknown, pixels.Evaluate(Wide));
        Assert.False(pixels.Matches(Wide));
        Assert.False(Matches("not (min-width: 768px)", Wide));                   // not unknown is unknown
        Assert.False(Matches("(min-width: 80) and (resolution: 2dppx)", Wide)); // unknown poisons an and
        Assert.True(Matches("(min-width: 80) or (resolution: 2dppx)", Wide));   // but not an or with a true side
        Assert.False(Matches("(orientation: sideways)", Wide));
    }

    [Theory]
    [InlineData("")]
    [InlineData("(min-width 80)")]
    [InlineData("(min-width: 80) and or (max-width: 90)")]
    [InlineData("(min-width: 80) and (max-width: 90) or (color)")]
    [InlineData("screen or (color)")]
    [InlineData("(width >)")]
    [InlineData("(min-width: 80")]
    public void A_syntax_error_is_refused(string query) =>
        Assert.Throws<FormatException>(() => MediaQueryList.Parse(query));

    [Fact]
    public void Nesting_combines_with_and_and_the_result_is_memoised_per_environment()
    {
        var outer = MediaQueryList.Parse("(min-width: 80)");
        var inner = MediaQueryList.Parse("(orientation: landscape)");
        var both = MediaQueryList.And(outer, inner);
        Assert.True(both.Matches(Wide));
        Assert.False(both.Matches(Narrow));
        Assert.False(both.Matches(new MediaEnvironment { Width = 100, Height = 120 }));
        Assert.Equal("(min-width: 80) and (orientation: landscape)", both.Text);
    }

    [Theory]
    [InlineData("(display: grid)", true)]
    [InlineData("(display: table-caption)", false)]
    [InlineData("(width: fit-content)", true)]
    [InlineData("(width: 12px)", false)]
    [InlineData("(font-family: monospace)", false)]   // accepted and ignored is not supported
    [InlineData("(--x: 1)", true)]
    [InlineData("(color: var(--x))", true)]
    [InlineData("not (display: grid)", false)]
    [InlineData("(display: grid) and (gap: 1)", true)]
    [InlineData("(display: grid) and (gap: 1px)", false)]
    [InlineData("(display: grid) or (gap: 1px)", true)]
    [InlineData("((display: grid) or (gap: 1px)) and (color: red)", true)]
    [InlineData("selector(:focus-within)", true)]
    [InlineData("selector(:hover)", false)]
    [InlineData("selector(div > p)", true)]
    public void Supports_answers_from_the_parser(string condition, bool expected) =>
        Assert.Equal(expected, SupportsCondition.Evaluate(condition));
}
