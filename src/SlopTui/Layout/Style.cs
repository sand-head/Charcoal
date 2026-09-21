using SlopTui.Rendering;

namespace SlopTui.Layout;

public enum Display { Flex, Grid, None }
public enum FlexDirection { Row, Column, RowReverse, ColumnReverse }
public enum FlexWrap { NoWrap, Wrap, WrapReverse }
public enum JustifyContent { FlexStart, Center, FlexEnd, SpaceBetween, SpaceAround, SpaceEvenly }
public enum AlignItems { Stretch, FlexStart, Center, FlexEnd }

/// <summary>How the lines of a wrapped flex container, or the tracks of a grid, share leftover cross space.</summary>
public enum AlignContent { Stretch, FlexStart, Center, FlexEnd, SpaceBetween, SpaceAround }

/// <summary><see cref="AlignItems"/> for one item, or <see cref="Auto"/> to take the container's.</summary>
public enum AlignSelf { Auto, Stretch, FlexStart, Center, FlexEnd }

/// <summary>
/// What a box does with content larger than itself. As in CSS, only a
/// <see cref="Visible"/> box keeps its content's size as its minimum.
/// </summary>
public enum Overflow { Visible, Hidden, Scroll }
public enum Position { Relative, Absolute }
public enum BorderStyle { None, Single, Double, Round, Bold, Classic }

/// <summary>How a text leaf fits its width.</summary>
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

/// <summary>What kind of value a <see cref="Length"/> holds.</summary>
public enum LengthUnit { Auto, Cells, Percent }

/// <summary>A size on one axis: cells, a percentage of the parent's content box, or auto.</summary>
public readonly record struct Length(LengthUnit Unit, int Value)
{
    public static readonly Length Auto = new(LengthUnit.Auto, 0);

    public static Length Cells(int cells) => new(LengthUnit.Cells, cells);
    public static Length Percent(int percent) => new(LengthUnit.Percent, percent);

    public bool IsAuto => Unit == LengthUnit.Auto;

    /// <summary>The length in cells, or null when it is auto or a percentage of an unknown extent.</summary>
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
        _ => "auto",
    };
}

/// <summary>
/// The CSS subset the layout engine and the painter understand. Boxes ignore
/// the text properties; text leaves are flex items too, so they share the rest.
/// </summary>
public sealed record Style
{
    // Layout
    public Display Display { get; init; } = Display.Flex;
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

    /// <summary>How far a scrolling box's content is scrolled, in cells.</summary>
    public int ScrollX { get; init; }
    public int ScrollY { get; init; }

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
    public BorderStyle Border { get; init; } = BorderStyle.None;
    public Color BorderColor { get; init; } = Color.Default;
    public bool BorderTop { get; init; } = true;
    public bool BorderRight { get; init; } = true;
    public bool BorderBottom { get; init; } = true;
    public bool BorderLeft { get; init; } = true;

    // Text
    public Color Color { get; init; } = Color.Default;
    public TextStyle TextStyle { get; init; } = TextStyle.None;
    public TextWrap Wrap { get; init; } = TextWrap.Wrap;

    public static readonly Style Default = new();

    /// <summary>The cells the border takes on each side.</summary>
    public Edges BorderEdges => Border == BorderStyle.None
        ? Edges.Zero
        : new Edges(BorderTop ? 1 : 0, BorderRight ? 1 : 0, BorderBottom ? 1 : 0, BorderLeft ? 1 : 0);

    /// <summary>Border plus padding.</summary>
    public Edges Inset => BorderEdges + Padding;
}
