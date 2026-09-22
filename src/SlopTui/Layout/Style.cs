using SlopTui.Rendering;

namespace SlopTui.Layout;

/// <summary>
/// CSS <c>display</c>. As in CSS the initial value is <see cref="Inline"/>,
/// and the user-agent sheet makes block elements <see cref="Block"/>. The
/// layout engine treats an inline node it is handed as a block; only the host
/// tree groups inline elements into text.
/// </summary>
public enum Display { Block, Inline, Flex, Grid, None }
public enum FlexDirection { Row, Column, RowReverse, ColumnReverse }
public enum FlexWrap { NoWrap, Wrap, WrapReverse }
public enum JustifyContent { FlexStart, Center, FlexEnd, SpaceBetween, SpaceAround, SpaceEvenly }
public enum AlignItems { Stretch, FlexStart, Center, FlexEnd }

/// <summary>How the lines of a wrapped flex container, or the tracks of a grid, share leftover cross space.</summary>
public enum AlignContent { Stretch, FlexStart, Center, FlexEnd, SpaceBetween, SpaceAround }

/// <summary><see cref="AlignItems"/> for one item, or <see cref="Auto"/> to take the container's.</summary>
public enum AlignSelf { Auto, Stretch, FlexStart, Center, FlexEnd }

/// <summary>
/// What a box does with content larger than itself. <see cref="Scroll"/>
/// (also <c>auto</c>) clips and scrolls by <see cref="LayoutNode.ScrollTop"/>
/// and <see cref="LayoutNode.ScrollLeft"/>. As in CSS, only a
/// <see cref="Visible"/> box keeps its content's size as its minimum.
/// </summary>
public enum Overflow { Visible, Hidden, Scroll }
public enum Position { Relative, Absolute }

/// <summary>CSS <c>visibility</c>: a hidden box keeps its place and paints nothing.</summary>
public enum Visibility { Visible, Hidden }

/// <summary>
/// CSS <c>border-style</c>. Solid is a single line, rounded with a
/// <c>border-radius</c> and heavy with a <c>thick</c> width; dashed and
/// dotted are drawn in ASCII.
/// </summary>
public enum BorderStyle { None, Solid, Double, Dashed, Dotted }

/// <summary>The glyphs a border is drawn with, derived from its style, width and radius.</summary>
public enum BorderGlyphSet { None, Single, Double, Round, Bold, Classic }

/// <summary>CSS <c>white-space</c>: whether whitespace collapses and whether lines wrap.</summary>
public enum WhiteSpace { Normal, NoWrap, Pre, PreWrap, PreLine }

/// <summary>
/// CSS <c>text-overflow</c> for a line that neither wraps nor fits.
/// <see cref="EllipsisStart"/> is written <c>ellipsis clip</c> and
/// <see cref="EllipsisMiddle"/> <c>ellipsis ellipsis</c>.
/// </summary>
public enum TextOverflow { Clip, Ellipsis, EllipsisStart, EllipsisMiddle }

public enum TextAlign { Left, Center, Right }

/// <summary>How a text fits its width, derived from <c>white-space</c> and <c>text-overflow</c>.</summary>
public enum TextWrap
{
    /// <summary>Break at spaces, then anywhere, so every cluster is shown.</summary>
    Wrap,
    /// <summary>One line, cut at the end with an ellipsis.</summary>
    Truncate,
    /// <summary>One line, cut at the start with an ellipsis.</summary>
    TruncateStart,
    /// <summary>One line, cut in the middle with an ellipsis.</summary>
    TruncateMiddle,
    /// <summary>One line per source line, clipped by the box with no marker.</summary>
    Clip,
}

/// <summary>What kind of value a grid <see cref="Track"/> holds.</summary>
public enum TrackUnit { Auto, Cells, Percent, Fraction }

/// <summary>A grid track: cells, a percentage, a fraction (<c>fr</c>) of the rest, or auto.</summary>
public readonly record struct Track(TrackUnit Unit, double Value)
{
    public static readonly Track Auto = new(TrackUnit.Auto, 0);
    public static Track Cells(int cells) => new(TrackUnit.Cells, cells);
    public static Track Percent(double percent) => new(TrackUnit.Percent, percent);
    public static Track Fr(double fraction) => new(TrackUnit.Fraction, fraction);

    public override string ToString() => Unit switch
    {
        TrackUnit.Cells => ((int)Value).ToString(),
        TrackUnit.Percent => Value + "%",
        TrackUnit.Fraction => Value + "fr",
        _ => "auto",
    };
}

/// <summary>Grid tracks that format as a template such as <c>1fr 20 auto</c>.</summary>
public sealed class TrackList : List<Track>
{
    public TrackList() { }
    public TrackList(IEnumerable<Track> tracks) : base(tracks) { }

    public override string ToString() => string.Join(' ', this);
}

/// <summary>What kind of value a <see cref="Length"/> holds.</summary>
public enum LengthUnit { Auto, Cells, Percent, FitContent }

/// <summary>
/// A size on one axis: cells, a percentage of the parent's content box,
/// <see cref="Auto"/> for whatever the layout gives, or
/// <see cref="FitContent"/> for the content's own size.
/// </summary>
public readonly record struct Length(LengthUnit Unit, int Value)
{
    public static readonly Length Auto = new(LengthUnit.Auto, 0);
    public static readonly Length FitContent = new(LengthUnit.FitContent, 0);

    public static Length Cells(int cells) => new(LengthUnit.Cells, cells);
    public static Length Percent(int percent) => new(LengthUnit.Percent, percent);

    public bool IsAuto => Unit == LengthUnit.Auto;

    /// <summary>The length in cells, or null when it is not fixed or is a percentage of an unknown extent.</summary>
    public int? Resolve(int? parentExtent) => Unit switch
    {
        LengthUnit.Cells => Value,
        LengthUnit.Percent => parentExtent is { } extent ? (int)Math.Round(extent * Value / 100.0) : null,
        _ => null,
    };

    public static implicit operator Length(int cells) => Cells(cells);

    public override string ToString() => Unit switch
    {
        LengthUnit.Cells => Value.ToString(),
        LengthUnit.Percent => Value + "%",
        LengthUnit.FitContent => "fit-content",
        _ => "auto",
    };
}

/// <summary>
/// The CSS properties the layout engine and the painter understand, under
/// their CSS names, measured in cells where CSS uses pixels.
/// </summary>
/// <remarks>
/// Terminal attributes without a CSS property are reached through the
/// closest one: <c>opacity</c> dims and <c>filter: invert()</c> inverts. The
/// text properties are inherited, so every element carries the look its text
/// paints with.
/// </remarks>
public sealed record Style
{
    // Layout
    public Display Display { get; init; } = Display.Inline;
    public Position Position { get; init; } = Position.Relative;
    public FlexDirection FlexDirection { get; init; } = FlexDirection.Row;
    public FlexWrap FlexWrap { get; init; } = FlexWrap.NoWrap;
    public JustifyContent JustifyContent { get; init; } = JustifyContent.FlexStart;
    public AlignItems AlignItems { get; init; } = AlignItems.Stretch;
    public AlignContent AlignContent { get; init; } = AlignContent.Stretch;
    public AlignSelf AlignSelf { get; init; } = AlignSelf.Auto;

    /// <summary>Aligns grid items horizontally within their cells.</summary>
    public AlignItems JustifyItems { get; init; } = AlignItems.Stretch;
    public double FlexGrow { get; init; }
    public double FlexShrink { get; init; } = 1;
    public Length FlexBasis { get; init; } = Length.Auto;
    public Length Width { get; init; } = Length.Auto;
    public Length Height { get; init; } = Length.Auto;
    public Length MinWidth { get; init; } = Length.Auto;
    public Length MinHeight { get; init; } = Length.Auto;
    public Length MaxWidth { get; init; } = Length.Auto;
    public Length MaxHeight { get; init; } = Length.Auto;
    public Edges Padding { get; init; } = Edges.Zero;
    public Edges Margin { get; init; } = Edges.Zero;
    public int RowGap { get; init; }
    public int ColumnGap { get; init; }
    public Overflow Overflow { get; init; } = Overflow.Visible;
    public Visibility Visibility { get; init; } = Visibility.Visible;

    // Grid
    /// <summary>Empty means one auto column.</summary>
    public IReadOnlyList<Track> GridTemplateColumns { get; init; } = [];
    /// <summary>Rows beyond these are sized to their content.</summary>
    public IReadOnlyList<Track> GridTemplateRows { get; init; } = [];
    /// <summary>A 1-based grid line, or null for auto-placement.</summary>
    public int? GridColumnStart { get; init; }
    public int? GridRowStart { get; init; }
    public int GridColumnSpan { get; init; } = 1;
    public int GridRowSpan { get; init; } = 1;

    /// <summary>Offsets for <see cref="Position.Absolute"/>; null means unset.</summary>
    public int? Top { get; init; }
    public int? Right { get; init; }
    public int? Bottom { get; init; }
    public int? Left { get; init; }

    // Box visuals
    public Color Background { get; init; } = Color.Default;

    /// <summary>The style of every side that does not set its own.</summary>
    public BorderStyle BorderStyle { get; init; } = BorderStyle.None;
    public BorderStyle? BorderTopStyle { get; init; }
    public BorderStyle? BorderRightStyle { get; init; }
    public BorderStyle? BorderBottomStyle { get; init; }
    public BorderStyle? BorderLeftStyle { get; init; }

    /// <summary>1 for <c>thin</c> and <c>medium</c>, 2 for <c>thick</c>, which is drawn heavy.</summary>
    public int BorderWidth { get; init; } = 1;

    /// <summary>Any radius above zero rounds the corners of a solid border.</summary>
    public int BorderRadius { get; init; }

    /// <summary><see cref="Color.Default"/> means <c>currentcolor</c>.</summary>
    public Color BorderColor { get; init; } = Color.Default;

    // Text, inherited
    public Color Color { get; init; } = Color.Default;

    public TextStyle TextStyle { get; init; } = TextStyle.None;

    /// <summary>
    /// Flags this element turned off, such as <c>font-weight: normal</c> under
    /// a bold parent, so inheritance does not turn them back on.
    /// </summary>
    public TextStyle TextStyleReset { get; init; } = TextStyle.None;

    public WhiteSpace WhiteSpace { get; init; } = WhiteSpace.Normal;
    public TextOverflow TextOverflow { get; init; } = TextOverflow.Clip;
    public TextAlign TextAlign { get; init; } = TextAlign.Left;

    /// <summary>The inherited properties this element set itself, which inheritance leaves alone.</summary>
    public StyleSet Set { get; init; } = StyleSet.None;

    /// <summary>The custom properties (<c>--name</c>) in force here, inherited.</summary>
    public IReadOnlyDictionary<string, string> CustomProperties { get; init; } = NoCustomProperties;

    public static readonly IReadOnlyDictionary<string, string> NoCustomProperties = new Dictionary<string, string>();

    public static readonly Style Default = new();

    /// <summary>A side's own border style, or else the shared one.</summary>
    public BorderStyle SideStyle(Side side) => side switch
    {
        Side.Top => BorderTopStyle ?? BorderStyle,
        Side.Right => BorderRightStyle ?? BorderStyle,
        Side.Bottom => BorderBottomStyle ?? BorderStyle,
        _ => BorderLeftStyle ?? BorderStyle,
    };

    public bool BorderTop => SideStyle(Side.Top) != BorderStyle.None;
    public bool BorderRight => SideStyle(Side.Right) != BorderStyle.None;
    public bool BorderBottom => SideStyle(Side.Bottom) != BorderStyle.None;
    public bool BorderLeft => SideStyle(Side.Left) != BorderStyle.None;

    public bool HasBorder => BorderTop || BorderRight || BorderBottom || BorderLeft;

    /// <summary>The glyphs the border is drawn with, chosen by the first style that draws.</summary>
    public BorderGlyphSet BorderGlyphs => FirstDrawnBorderStyle() switch
    {
        BorderStyle.Solid when BorderRadius > 0 => BorderGlyphSet.Round,
        BorderStyle.Solid when BorderWidth >= 2 => BorderGlyphSet.Bold,
        BorderStyle.Solid => BorderGlyphSet.Single,
        BorderStyle.Double => BorderGlyphSet.Double,
        BorderStyle.Dashed or BorderStyle.Dotted => BorderGlyphSet.Classic,
        _ => BorderGlyphSet.None,
    };

    private BorderStyle FirstDrawnBorderStyle()
    {
        BorderStyle?[] candidates = [BorderStyle, BorderTopStyle, BorderRightStyle, BorderBottomStyle, BorderLeftStyle];
        foreach (var candidate in candidates)
        {
            if (candidate is { } style and not BorderStyle.None) return style;
        }
        return BorderStyle.None;
    }

    /// <summary>The cells the border takes on each side.</summary>
    public Edges BorderEdges => new(BorderTop ? 1 : 0, BorderRight ? 1 : 0, BorderBottom ? 1 : 0, BorderLeft ? 1 : 0);

    /// <summary>Border plus padding.</summary>
    public Edges Inset => BorderEdges + Padding;

    /// <summary>Wrapping for the wrapping white-space modes, else the cut <c>text-overflow</c> asks for.</summary>
    public TextWrap Wrap
    {
        get
        {
            if (WhiteSpace is WhiteSpace.Normal or WhiteSpace.PreWrap or WhiteSpace.PreLine) return TextWrap.Wrap;
            return TextOverflow switch
            {
                TextOverflow.Ellipsis => TextWrap.Truncate,
                TextOverflow.EllipsisStart => TextWrap.TruncateStart,
                TextOverflow.EllipsisMiddle => TextWrap.TruncateMiddle,
                _ => TextWrap.Clip,
            };
        }
    }

    public bool IsInline => Display == Display.Inline;
}

public enum Side { Top, Right, Bottom, Left }

/// <summary>The inherited properties an element set itself; see <see cref="Style.Set"/>.</summary>
[Flags]
public enum StyleSet
{
    None = 0,
    Color = 1,
    WhiteSpace = 2,
    TextAlign = 4,
}
