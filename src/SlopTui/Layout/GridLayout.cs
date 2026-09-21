namespace SlopTui.Layout;

/// <summary>
/// A small CSS grid on integer cells: explicit column and row tracks, implicit
/// auto rows, and sparse auto-placement.
/// </summary>
/// <remarks>
/// <para>
/// Auto tracks fit the largest item that sits in them alone; items spanning
/// several tracks then grow the auto tracks they cover by whatever they still
/// lack. Fraction tracks share the space left over, or size like auto tracks
/// when the axis is unbounded.
/// </para>
/// <para>
/// Items with a row and a column are placed first, then the rest in order
/// from a cursor that only moves forward. Within its area an item is aligned
/// by <see cref="Style.JustifyItems"/> and <see cref="Style.AlignItems"/>, and
/// leftover height is shared by <see cref="Style.AlignContent"/>.
/// </para>
/// </remarks>
public static class GridLayout
{
    private const double Epsilon = 1e-9;

    private sealed class Placed
    {
        public required LayoutNode Node;
        public int Column;
        public int Row;
        public int ColumnSpan;
        public int RowSpan;

        public bool HasColumn => Column >= 0;
        public bool HasRow => Row >= 0;
    }

    private sealed class Computed
    {
        public required List<Placed> Items;
        public required int[] Columns;
        public required int[] Rows;
        public required int[] ColumnOffsets;
        public required int[] RowOffsets;

        public int Width => Columns.Length == 0 ? 0 : ColumnOffsets[^1] + Columns[^1];
        public int Height => Rows.Length == 0 ? 0 : RowOffsets[^1] + Rows[^1];
    }

    /// <summary>The size of the tracks and the gaps between them.</summary>
    internal static Size MeasureContent(LayoutNode node, int? innerWidth, int? innerHeight)
    {
        var computed = Compute(node, innerWidth, innerHeight);
        return new Size(computed.Width, computed.Height);
    }

    /// <summary>Fixed columns as they are, the others at their items' minimum widths, plus gaps.</summary>
    internal static int MinContentWidth(LayoutNode node)
    {
        var style = node.Style;
        var columns = ColumnCount(style);
        var items = Place(node, columns);
        var width = style.ColumnGap * (columns - 1);
        for (var column = 0; column < columns; column++)
        {
            var track = ColumnTrack(style, column);
            if (track.Unit == TrackUnit.Cells)
            {
                width += Math.Max(0, (int)track.Value);
                continue;
            }

            var inColumn = ItemsOnlyIn(items, column, i => i.Column, i => i.ColumnSpan);
            width += Largest(inColumn, item => FlexLayout.MinContentWidth(item.Node) + item.Node.Style.Margin.Horizontal);
        }
        return width;
    }

    /// <summary>Lays the items out inside the content box, shifted by the scroll offset.</summary>
    internal static void Arrange(LayoutNode node, Rect content, Rect visible)
    {
        var style = node.Style;
        var computed = Compute(node, content.Width, content.Height);
        node.ContentSize = new Size(computed.Width, computed.Height);

        var (rowStart, rowBetween) = AlignRows(style, computed, content.Height - computed.Height);
        var (scrollX, scrollY) = FlexLayout.ScrollOffset(style);
        foreach (var item in computed.Items)
        {
            var area = new Rect(
                computed.ColumnOffsets[item.Column],
                computed.RowOffsets[item.Row] + rowStart + rowBetween * item.Row,
                Span(computed.Columns, computed.ColumnOffsets, item.Column, item.ColumnSpan),
                Span(computed.Rows, computed.RowOffsets, item.Row, item.RowSpan) + rowBetween * (item.RowSpan - 1));
            var rect = PlaceInArea(item.Node, area, style).Offset(content.X - scrollX, content.Y - scrollY);
            FlexLayout.ArrangeChild(node, item.Node, rect, visible);
        }
    }

    /// <summary>
    /// Shares leftover height between the rows: stretching grows the auto
    /// rows in place, the other values return where rows start and the space
    /// between them.
    /// </summary>
    private static (int Start, int Between) AlignRows(Style style, Computed computed, int leftover)
    {
        var rows = computed.Rows.Length;
        if (leftover <= 0 || rows == 0) return (0, 0);

        switch (style.AlignContent)
        {
            case AlignContent.Stretch:
                var autoRows = Enumerable.Range(0, rows).Where(row => IsFlexible(RowTrack(style, row))).ToList();
                if (autoRows.Count > 0)
                {
                    ShareEvenly(computed.Rows, autoRows, leftover);
                    Reoffset(computed.Rows, computed.RowOffsets, style.RowGap);
                }
                return (0, 0);
            case AlignContent.Center:
                return (leftover / 2, 0);
            case AlignContent.FlexEnd:
                return (leftover, 0);
            case AlignContent.SpaceBetween when rows > 1:
                return (0, leftover / (rows - 1));
            case AlignContent.SpaceAround:
                var between = leftover / rows;
                return (between / 2, between);
            default:
                return (0, 0);
        }
    }

    /// <summary>An item's rect within its grid area, relative to the content box.</summary>
    private static Rect PlaceInArea(LayoutNode child, Rect area, Style grid)
    {
        var cs = child.Style;
        var availableWidth = Math.Max(0, area.Width - cs.Margin.Horizontal);
        var availableHeight = Math.Max(0, area.Height - cs.Margin.Vertical);
        var justify = grid.JustifyItems;
        var align = FlexLayout.Align(cs.AlignSelf, grid.AlignItems);

        var width = justify == AlignItems.Stretch && cs.Width.IsAuto
            ? availableWidth
            : Math.Min(availableWidth, FlexLayout.Measure(child, availableWidth, availableHeight).Width);
        var height = align == AlignItems.Stretch && cs.Height.IsAuto
            ? availableHeight
            : Math.Min(availableHeight, FlexLayout.Measure(child, width, availableHeight).Height);

        var x = area.X + cs.Margin.Left + AlignmentOffset(justify, availableWidth - width);
        var y = area.Y + cs.Margin.Top + AlignmentOffset(align, availableHeight - height);
        return new Rect(x, y, width, height);
    }

    private static int AlignmentOffset(AlignItems alignment, int free) => alignment switch
    {
        AlignItems.Center => free / 2,
        AlignItems.FlexEnd => free,
        _ => 0,
    };

    private static int ColumnCount(Style style) => Math.Max(1, style.GridTemplateColumns.Count);

    private static Track ColumnTrack(Style style, int column) =>
        column < style.GridTemplateColumns.Count ? style.GridTemplateColumns[column] : Track.Auto;

    private static Track RowTrack(Style style, int row) =>
        row < style.GridTemplateRows.Count ? style.GridTemplateRows[row] : Track.Auto;

    private static bool IsFlexible(Track track) => track.Unit is TrackUnit.Auto or TrackUnit.Fraction;

    /// <summary>The extent of <paramref name="span"/> tracks from <paramref name="start"/>, gaps included.</summary>
    private static int Span(int[] tracks, int[] offsets, int start, int span)
    {
        var end = Math.Min(tracks.Length, start + span) - 1;
        return end < start ? 0 : offsets[end] + tracks[end] - offsets[start];
    }

    private static void Reoffset(int[] tracks, int[] offsets, int gap)
    {
        var position = 0;
        for (var i = 0; i < tracks.Length; i++)
        {
            offsets[i] = position;
            position += tracks[i] + gap;
        }
    }

    /// <summary>Adds <paramref name="amount"/> to the given tracks as evenly as whole cells allow.</summary>
    private static void ShareEvenly(int[] sizes, List<int> tracks, int amount)
    {
        var each = amount / tracks.Count;
        var extra = amount - each * tracks.Count;
        for (var i = 0; i < tracks.Count; i++)
        {
            sizes[tracks[i]] += each + (i < extra ? 1 : 0);
        }
    }

    /// <summary>The largest size among the items, or zero.</summary>
    private static int Largest(IEnumerable<Placed> items, Func<Placed, int> sizeOf)
    {
        var largest = 0;
        foreach (var item in items)
        {
            largest = Math.Max(largest, sizeOf(item));
        }
        return largest;
    }

    private static IEnumerable<Placed> ItemsOnlyIn(List<Placed> items, int track, Func<Placed, int> startOf, Func<Placed, int> spanOf) =>
        items.Where(item => startOf(item) == track && spanOf(item) == 1);

    /// <summary>Places every in-flow child: explicit positions first, then the auto-placement cursor.</summary>
    private static List<Placed> Place(LayoutNode node, int columns)
    {
        var items = new List<Placed>();
        foreach (var child in node.Children)
        {
            var cs = child.Style;
            if (cs.Display == Display.None || cs.Position == Position.Absolute) continue;

            var columnSpan = Math.Clamp(cs.GridColumnSpan, 1, columns);
            var column = cs.GridColumnStart is { } line ? Math.Max(0, line - 1) : -1;
            items.Add(new Placed
            {
                Node = child,
                Column = column >= 0 ? Math.Min(column, columns - columnSpan) : -1,
                Row = cs.GridRowStart is { } rowLine ? Math.Max(0, rowLine - 1) : -1,
                ColumnSpan = columnSpan,
                RowSpan = Math.Max(1, cs.GridRowSpan),
            });
        }

        var grid = new Occupancy(columns);
        foreach (var item in items.Where(i => i.HasColumn && i.HasRow))
        {
            grid.Take(item);
        }

        var cursor = (Row: 0, Column: 0);
        foreach (var item in items.Where(i => !(i.HasColumn && i.HasRow)))
        {
            if (item.HasRow)
            {
                item.Column = grid.FirstFreeColumn(item);
            }
            else if (item.HasColumn)
            {
                item.Row = grid.FirstFreeRow(item, cursor.Row);
                cursor = (item.Row, item.Column + item.ColumnSpan);
            }
            else
            {
                (item.Row, item.Column) = grid.NextFreeArea(item, cursor);
                cursor = (item.Row, item.Column + item.ColumnSpan);
            }
            grid.Take(item);
        }
        return items;
    }

    /// <summary>Which cells of the grid are taken.</summary>
    private sealed class Occupancy(int columns)
    {
        private readonly List<bool[]> _rows = [];

        public bool IsFree(int row, int column, int columnSpan, int rowSpan)
        {
            for (var r = row; r < Math.Min(row + rowSpan, _rows.Count); r++)
            {
                for (var c = column; c < Math.Min(column + columnSpan, columns); c++)
                {
                    if (_rows[r][c]) return false;
                }
            }
            return true;
        }

        public void Take(Placed item)
        {
            while (_rows.Count < item.Row + item.RowSpan) _rows.Add(new bool[columns]);
            for (var r = item.Row; r < item.Row + item.RowSpan; r++)
            {
                for (var c = item.Column; c < Math.Min(columns, item.Column + item.ColumnSpan); c++)
                {
                    _rows[r][c] = true;
                }
            }
        }

        /// <summary>The first column in the item's row where it fits, or 0 when none does.</summary>
        public int FirstFreeColumn(Placed item)
        {
            for (var column = 0; column + item.ColumnSpan <= columns; column++)
            {
                if (IsFree(item.Row, column, item.ColumnSpan, item.RowSpan)) return column;
            }
            return 0;
        }

        /// <summary>The first row at or after <paramref name="from"/> where the item's column is free.</summary>
        public int FirstFreeRow(Placed item, int from)
        {
            var row = from;
            while (!IsFree(row, item.Column, item.ColumnSpan, item.RowSpan)) row++;
            return row;
        }

        /// <summary>The first area at or after the cursor, in reading order, that fits the item.</summary>
        public (int Row, int Column) NextFreeArea(Placed item, (int Row, int Column) cursor)
        {
            var (row, column) = cursor;
            while (true)
            {
                if (column + item.ColumnSpan > columns)
                {
                    row++;
                    column = 0;
                }
                else if (IsFree(row, column, item.ColumnSpan, item.RowSpan))
                {
                    return (row, column);
                }
                else
                {
                    column++;
                }
            }
        }
    }

    private static Computed Compute(LayoutNode node, int? innerWidth, int? innerHeight)
    {
        var style = node.Style;
        var columns = ColumnCount(style);
        var items = Place(node, columns);

        var rowCount = Math.Max(style.GridTemplateRows.Count, items.Count == 0 ? 0 : items.Max(i => i.Row + i.RowSpan));
        var columnTracks = Enumerable.Range(0, columns).Select(c => ColumnTrack(style, c)).ToArray();
        var rowTracks = Enumerable.Range(0, rowCount).Select(r => RowTrack(style, r)).ToArray();

        int ItemWidth(Placed item)
        {
            var margin = item.Node.Style.Margin;
            return FlexLayout.Measure(item.Node, FlexLayout.Inner(innerWidth, margin.Horizontal), null).Width + margin.Horizontal;
        }

        var columnSizes = SizeTracks(columnTracks, innerWidth, style.ColumnGap,
            column => Largest(ItemsOnlyIn(items, column, i => i.Column, i => i.ColumnSpan), ItemWidth));
        GrowForSpans(columnTracks, columnSizes, style.ColumnGap, items, i => i.Column, i => i.ColumnSpan, ItemWidth);
        var columnOffsets = new int[columns];
        Reoffset(columnSizes, columnOffsets, style.ColumnGap);

        int ItemHeight(Placed item)
        {
            var margin = item.Node.Style.Margin;
            var areaWidth = Span(columnSizes, columnOffsets, item.Column, item.ColumnSpan);
            return FlexLayout.Measure(item.Node, Math.Max(0, areaWidth - margin.Horizontal), null).Height + margin.Vertical;
        }

        var rowSizes = SizeTracks(rowTracks, innerHeight, style.RowGap,
            row => Largest(ItemsOnlyIn(items, row, i => i.Row, i => i.RowSpan), ItemHeight));
        GrowForSpans(rowTracks, rowSizes, style.RowGap, items, i => i.Row, i => i.RowSpan, ItemHeight);
        var rowOffsets = new int[rowCount];
        Reoffset(rowSizes, rowOffsets, style.RowGap);

        return new Computed
        {
            Items = items,
            Columns = columnSizes,
            Rows = rowSizes,
            ColumnOffsets = columnOffsets,
            RowOffsets = rowOffsets,
        };
    }

    /// <summary>
    /// Grows the auto tracks under each spanning item by what the item still
    /// lacks, shared evenly. Fixed tracks never grow.
    /// </summary>
    private static void GrowForSpans(Track[] tracks, int[] sizes, int gap, List<Placed> items,
        Func<Placed, int> startOf, Func<Placed, int> spanOf, Func<Placed, int> sizeOf)
    {
        foreach (var item in items)
        {
            var span = spanOf(item);
            if (span <= 1) continue;

            var start = startOf(item);
            var end = Math.Min(tracks.Length, start + span);
            var covered = gap * (end - start - 1);
            var flexible = new List<int>();
            for (var track = start; track < end; track++)
            {
                covered += sizes[track];
                if (IsFlexible(tracks[track])) flexible.Add(track);
            }

            var shortfall = sizeOf(item) - covered;
            if (shortfall > 0 && flexible.Count > 0) ShareEvenly(sizes, flexible, shortfall);
        }
    }

    /// <summary>
    /// Sizes one axis's tracks: fixed and percentage tracks as they say, auto
    /// tracks from their content, and fraction tracks from what is left.
    /// </summary>
    private static int[] SizeTracks(Track[] tracks, int? available, int gap, Func<int, int> contentOf)
    {
        var sizes = new int[tracks.Length];
        var fractions = new List<int>();
        var used = tracks.Length > 1 ? gap * (tracks.Length - 1) : 0;
        for (var i = 0; i < tracks.Length; i++)
        {
            var track = tracks[i];
            switch (track.Unit)
            {
                case TrackUnit.Cells:
                    sizes[i] = Math.Max(0, (int)track.Value);
                    break;
                case TrackUnit.Percent when available is { } total:
                    sizes[i] = Math.Max(0, (int)Math.Round(total * track.Value / 100.0));
                    break;
                case TrackUnit.Fraction when available is { }:
                    fractions.Add(i);
                    continue;
                default:
                    sizes[i] = contentOf(i);
                    break;
            }
            used += sizes[i];
        }

        if (fractions.Count > 0) ShareFractions(tracks, sizes, fractions, Math.Max(0, available!.Value - used));
        return sizes;
    }

    /// <summary>
    /// Divides the remaining space between fraction tracks by their weights,
    /// giving the rounding leftovers to the largest fractional parts.
    /// </summary>
    private static void ShareFractions(Track[] tracks, int[] sizes, List<int> fractions, int remaining)
    {
        var totalWeight = fractions.Sum(i => Math.Max(0, tracks[i].Value));
        if (totalWeight <= 0) return;

        var shares = fractions.Select(i => remaining * Math.Max(0, tracks[i].Value) / totalWeight).ToArray();
        for (var k = 0; k < fractions.Count; k++)
        {
            sizes[fractions[k]] = (int)Math.Floor(shares[k] + Epsilon);
        }

        var leftover = remaining - fractions.Sum(i => sizes[i]);
        var byFraction = Enumerable.Range(0, fractions.Count).OrderByDescending(k => shares[k] - Math.Floor(shares[k] + Epsilon));
        foreach (var k in byFraction.Take(Math.Max(0, leftover)))
        {
            sizes[fractions[k]]++;
        }
    }
}
