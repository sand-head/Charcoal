namespace SlopTui.Layout;

/// <summary>
/// Flexbox, and grid through <see cref="GridLayout"/>, on integer cells, as a
/// cached measure pass and an arrange pass. Each node's rect is its border
/// box in absolute terminal cells.
/// </summary>
/// <remarks>
/// <para>
/// Constraints are the room left for a node's border box after its margins,
/// and percentages resolve against that room rather than the parent's whole
/// content box, so a child never overflows because of its margins.
/// </para>
/// <para>
/// As in CSS, a flex item that does not clip cannot shrink below its content
/// unless it sets a minimum; an item with hidden or scrolling overflow can
/// shrink to nothing.
/// </para>
/// </remarks>
public static class FlexLayout
{
    private const double Epsilon = 1e-9;

    /// <summary>Lays out the whole tree with the root filling the viewport.</summary>
    public static void Layout(LayoutNode root, Size viewport)
    {
        var rect = new Rect(0, 0, Math.Max(0, viewport.Width), Math.Max(0, viewport.Height));
        Measure(root, rect.Width, rect.Height);
        Arrange(root, rect, rect);
    }

    /// <summary>The border-box size the node wants; a null constraint is unbounded.</summary>
    public static Size Measure(LayoutNode node, int? availableWidth, int? availableHeight)
    {
        var style = node.Style;
        if (style.Display == Display.None) return Size.Empty;

        if (TryGetCachedMeasure(node, availableWidth, availableHeight, out var cached)) return cached;

        var result = MeasureUncached(node, availableWidth, availableHeight);
        node.MeasureCacheAlt = node.LayoutDirty ? null : node.MeasureCache;
        node.MeasureCache = (availableWidth, availableHeight, result);
        node.LayoutDirty = false;
        return result;
    }

    private static bool TryGetCachedMeasure(LayoutNode node, int? availableWidth, int? availableHeight, out Size size)
    {
        if (!node.LayoutDirty)
        {
            if (node.MeasureCache is { } cache && cache.Width == availableWidth && cache.Height == availableHeight)
            {
                size = cache.Result;
                return true;
            }
            if (node.MeasureCacheAlt is { } alt && alt.Width == availableWidth && alt.Height == availableHeight)
            {
                size = alt.Result;
                return true;
            }
        }
        size = Size.Empty;
        return false;
    }

    private static Size MeasureUncached(LayoutNode node, int? availableWidth, int? availableHeight)
    {
        var style = node.Style;
        var inset = style.Inset;

        var explicitWidth = style.Width.Resolve(availableWidth);
        var explicitHeight = style.Height.Resolve(availableHeight);

        var innerWidth = Inner(explicitWidth ?? availableWidth, inset.Horizontal);
        var innerHeight = Inner(explicitHeight ?? availableHeight, inset.Vertical);

        int width, height;
        if (explicitWidth is { } ew && explicitHeight is { } eh)
        {
            width = ew;
            height = eh;
        }
        else
        {
            var content = MeasureContentBox(node, innerWidth, innerHeight);
            width = explicitWidth ?? content.Width + inset.Horizontal;
            height = explicitHeight ?? content.Height + inset.Vertical;
        }

        width = Clamp(width, style.MinWidth.Resolve(availableWidth), style.MaxWidth.Resolve(availableWidth));
        height = Clamp(height, style.MinHeight.Resolve(availableHeight), style.MaxHeight.Resolve(availableHeight));
        return new Size(Math.Max(0, width), Math.Max(0, height));
    }

    private static Size MeasureContentBox(LayoutNode node, int? innerWidth, int? innerHeight)
    {
        if (node.IsLeaf) return node.MeasureContent(innerWidth, innerHeight);
        if (node.Style.Display == Display.Grid) return GridLayout.MeasureContent(node, innerWidth, innerHeight);
        return MeasureChildren(node, innerWidth, innerHeight);
    }

    /// <summary>
    /// A flex container's content size. A single bounded line is resolved as
    /// arrange will resolve it, so each item's cross size is measured at the
    /// main size it will actually get, such as the width a shrunk text wraps at.
    /// </summary>
    private static Size MeasureChildren(LayoutNode node, int? innerWidth, int? innerHeight)
    {
        var style = node.Style;
        var row = IsRow(style.FlexDirection);
        var wrap = style.FlexWrap != FlexWrap.NoWrap;
        var gap = row ? style.ColumnGap : style.RowGap;
        var crossGap = row ? style.RowGap : style.ColumnGap;
        var innerMain = row ? innerWidth : innerHeight;

        var items = FlexItems(node, innerWidth, innerHeight, row, innerMain);
        if (items.Count == 0) return Size.Empty;

        if (!wrap && innerMain is { } bounded) ResolveMainSizes(items, bounded, gap, row);

        foreach (var item in items)
        {
            // Only a row item that was actually resized needs measuring again;
            // the rest are already cached at their hypothetical size. Heights
            // stay unconstrained so each item keeps a single cache entry.
            var resized = row && !wrap && innerMain is { } && item.Main != item.Hypothetical;
            var size = resized
                ? Measure(item.Node, item.Main, null)
                : Measure(item.Node, item.AvailW, null);
            item.Cross = row ? size.Height : size.Width;
        }

        var lines = Lines(items, wrap ? innerMain : null, gap, row);
        var main = 0;
        var cross = crossGap * (lines.Count - 1);
        foreach (var line in lines)
        {
            main = Math.Max(main, MainUsed(line, gap, row));
            cross += Math.Max(0, line.Max(item => item.Cross + CrossMargins(item, row)));
        }

        return row ? new Size(main, cross) : new Size(cross, main);
    }

    private static IEnumerable<LayoutNode> InFlowChildren(LayoutNode node) =>
        node.Children.Where(child => child.Style.Display != Display.None && child.Style.Position != Position.Absolute);

    /// <summary>
    /// The item's flex basis, explicit size or content size, clamped by its
    /// explicit minimum and maximum. The automatic minimum is left to shrinking.
    /// </summary>
    private static int Hypothetical(LayoutNode child, bool row, int? availW, int? availH, int? containerMain)
    {
        var cs = child.Style;
        var availMain = row ? availW : availH;
        var basis = cs.FlexBasis.Resolve(containerMain) ?? (row ? cs.Width : cs.Height).Resolve(availMain);
        if (basis is null)
        {
            var measured = Measure(child, availW, null);
            basis = row ? measured.Width : measured.Height;
        }
        var min = (row ? cs.MinWidth : cs.MinHeight).Resolve(availMain);
        var max = (row ? cs.MaxWidth : cs.MaxHeight).Resolve(availMain);
        return Math.Max(0, Clamp(basis.Value, min, max));
    }

    /// <summary>
    /// The item's minimum main size: its own, else the automatic minimum for
    /// an item that does not clip, capped by its maximum.
    /// </summary>
    private static int? MinMain(LayoutNode child, bool row, int? availW, int? availH)
    {
        var cs = child.Style;
        var availMain = row ? availW : availH;
        var explicitMin = (row ? cs.MinWidth : cs.MinHeight).Resolve(availMain);
        if (explicitMin is { }) return explicitMin;
        if (cs.Overflow != Overflow.Visible) return null;

        var content = AutomaticMinimum(child, row, availW, availH);
        var max = (row ? cs.MaxWidth : cs.MaxHeight).Resolve(availMain);
        return max is { } limit ? Math.Min(content, limit) : content;
    }

    /// <summary>The narrowest a node can be without losing content, as CSS's <c>min-width: auto</c>.</summary>
    public static int MinContentWidth(LayoutNode node) => AutomaticMinimum(node, true, null, null);

    /// <summary>
    /// The automatic minimum size of a node on one axis, cached until the
    /// node is invalidated.
    /// </summary>
    /// <remarks>
    /// A clipping box has none. Otherwise an explicit size is the minimum; a
    /// leaf answers for itself; a grid is its columns' minimums across and its
    /// measured height down; a flex box is its children's minimums side by
    /// side on its main axis, or its largest child's across or when it wraps.
    /// Computing it through the children rather than from the measured
    /// content is what keeps a column of chrome around a scrolling transcript
    /// from being as tall as the transcript.
    /// </remarks>
    public static int AutomaticMinimum(LayoutNode node, bool widthAxis, int? availW, int? availH)
    {
        if (CachedMinimum(node, availW, availH) is { } cached)
        {
            var known = widthAxis ? cached.MinWidth : cached.MinHeight;
            if (known >= 0) return known;
        }

        var value = ComputeAutomaticMinimum(node, widthAxis, availW, availH);

        var entry = CachedMinimum(node, availW, availH) ?? (availW, availH, -1, -1);
        node.MinCache = widthAxis
            ? (entry.Width, entry.Height, value, entry.MinHeight)
            : (entry.Width, entry.Height, entry.MinWidth, value);
        return value;
    }

    private static (int? Width, int? Height, int MinWidth, int MinHeight)? CachedMinimum(LayoutNode node, int? availW, int? availH)
    {
        if (node.MinCache is { } entry && entry.Width == availW && entry.Height == availH) return entry;
        return null;
    }

    private static int ComputeAutomaticMinimum(LayoutNode node, bool widthAxis, int? availW, int? availH)
    {
        var style = node.Style;
        if (style.Display == Display.None) return 0;
        if (style.Overflow != Overflow.Visible) return 0;

        var availOwn = widthAxis ? availW : availH;
        var explicitSize = (widthAxis ? style.Width : style.Height).Resolve(availOwn);
        if (explicitSize is { } size) return Math.Max(0, size);

        // A measured height already includes the inset and the clamps.
        if (!widthAxis && (node.IsLeaf || style.Display == Display.Grid)) return Measure(node, availW, null).Height;
        if (style.Display == Display.Grid && !node.IsLeaf) return GridLayout.MinContentWidth(node);

        var content = node.IsLeaf ? node.MinContentWidth() : FlexAutomaticMinimum(node, widthAxis, availW, availH);
        content += widthAxis ? style.Inset.Horizontal : style.Inset.Vertical;
        content = Clamp(content,
            (widthAxis ? style.MinWidth : style.MinHeight).Resolve(availOwn),
            (widthAxis ? style.MaxWidth : style.MaxHeight).Resolve(availOwn));
        return Math.Max(0, content);
    }

    /// <summary>
    /// The children's minimums side by side on the main axis, or the largest
    /// of them across or when the box wraps, since wrapped items can move to
    /// another line.
    /// </summary>
    private static int FlexAutomaticMinimum(LayoutNode node, bool widthAxis, int? availW, int? availH)
    {
        var style = node.Style;
        var sideBySide = IsRow(style.FlexDirection) == widthAxis && style.FlexWrap == FlexWrap.NoWrap;
        // Children are measured within the box's own explicit size, not the room around it.
        var innerW = Inner(style.Width.Resolve(availW) ?? availW, style.Inset.Horizontal);
        var innerH = Inner(style.Height.Resolve(availH) ?? availH, style.Inset.Vertical);

        var sum = 0;
        var largest = 0;
        var count = 0;
        foreach (var child in InFlowChildren(node))
        {
            var margin = child.Style.Margin;
            var childMinimum = ChildMinimum(child, widthAxis, Inner(innerW, margin.Horizontal), Inner(innerH, margin.Vertical))
                + (widthAxis ? margin.Horizontal : margin.Vertical);
            sum += childMinimum;
            largest = Math.Max(largest, childMinimum);
            count++;
        }

        if (!sideBySide) return largest;
        var gap = widthAxis ? style.ColumnGap : style.RowGap;
        return sum + (count > 1 ? gap * (count - 1) : 0);
    }

    /// <summary>A child's explicit minimum, or else its automatic one.</summary>
    private static int ChildMinimum(LayoutNode child, bool widthAxis, int? availW, int? availH)
    {
        var cs = child.Style;
        var explicitMin = (widthAxis ? cs.MinWidth : cs.MinHeight).Resolve(widthAxis ? availW : availH);
        return explicitMin ?? AutomaticMinimum(child, widthAxis, availW, availH);
    }

    /// <summary>Gives the node its rect and lays out its subtree inside it.</summary>
    public static void Arrange(LayoutNode node, Rect rect) => Arrange(node, rect, rect);

    /// <summary>
    /// Gives the node its rect and lays out its subtree inside it. Children
    /// entirely outside <paramref name="visible"/> are placed but left
    /// unarranged until they come into view.
    /// </summary>
    public static void Arrange(LayoutNode node, Rect rect, Rect visible)
    {
        if (IsArrangementCurrent(node, rect, visible)) return;

        node.Layout = rect;
        node.LayoutDirty = false;
        node.ArrangeDirty = false;
        node.ArrangeDeferred = false;
        node.HasDeferredChildren = false;
        node.ArrangedVisible = visible;
        node.ContentSize = Size.Empty;
        if (node.Style.Display == Display.None)
        {
            node.Layout = Rect.Empty;
            return;
        }
        if (node.IsLeaf) return;

        var style = node.Style;
        var content = rect.Deflate(style.Inset);
        var paddingBox = rect.Deflate(style.BorderEdges);
        var childVisible = style.Overflow == Overflow.Visible ? visible : visible.Intersect(paddingBox);

        if (style.Display == Display.Grid)
        {
            GridLayout.Arrange(node, content, childVisible);
        }
        else
        {
            ArrangeFlow(node, content, childVisible);
        }

        foreach (var child in node.Children)
        {
            var cs = child.Style;
            if (cs.Display == Display.None)
            {
                Arrange(child, Rect.Empty, childVisible);
            }
            else if (cs.Position == Position.Absolute)
            {
                ArrangeAbsolute(child, paddingBox, childVisible);
            }
        }
    }

    private static bool IsArrangementCurrent(LayoutNode node, Rect rect, Rect visible)
    {
        if (node.ArrangeDirty || node.ArrangeDeferred || node.Layout != rect) return false;
        return !node.HasDeferredChildren || node.ArrangedVisible == visible;
    }

    /// <summary>Arranges a child, or only places it when it is entirely outside the visible region.</summary>
    internal static void ArrangeChild(LayoutNode parent, LayoutNode child, Rect rect, Rect visible)
    {
        if (rect.Intersect(visible).IsEmpty && !rect.IsEmpty)
        {
            child.Layout = rect;
            child.ArrangeDeferred = true;
            parent.HasDeferredChildren = true;
            return;
        }
        Arrange(child, rect, visible);
    }

    /// <summary>How far a clipping box scrolls its in-flow children.</summary>
    internal static (int X, int Y) ScrollOffset(Style style)
    {
        if (style.Overflow == Overflow.Visible) return (0, 0);
        return (Math.Max(0, style.ScrollX), Math.Max(0, style.ScrollY));
    }

    private sealed class Item
    {
        public required LayoutNode Node;
        public required Edges Margin;
        public int? AvailW;
        public int? AvailH;
        public int Hypothetical;
        public double Target;
        public int Main;
        public int Cross;
        public bool Frozen;
        public int? Max;
        public double Grow;
        public double Shrink;
        private int? _min;
        private bool _minKnown;

        /// <summary>The minimum main size, computed on first use since only shrinking needs it.</summary>
        public int? Min(bool row)
        {
            if (!_minKnown)
            {
                _min = MinMain(Node, row, AvailW, AvailH);
                _minKnown = true;
            }
            return _min;
        }
    }

    private static int MainMargins(Item item, bool row) => row ? item.Margin.Horizontal : item.Margin.Vertical;

    private static int CrossMargins(Item item, bool row) => row ? item.Margin.Vertical : item.Margin.Horizontal;

    /// <summary>The main-axis extent of a line: sizes, margins and gaps.</summary>
    private static int MainUsed(List<Item> line, int gap, bool row)
    {
        var used = line.Count > 1 ? gap * (line.Count - 1) : 0;
        foreach (var item in line)
        {
            used += item.Main + MainMargins(item, row);
        }
        return used;
    }

    /// <summary>
    /// Breaks items into lines by their hypothetical sizes. Without a limit
    /// there is one line; otherwise every line takes at least one item.
    /// </summary>
    private static List<List<Item>> Lines(List<Item> items, int? mainLimit, int gap, bool row)
    {
        if (mainLimit is not { } limit) return [items];

        var lines = new List<List<Item>>();
        var line = new List<Item>();
        var used = 0;
        foreach (var item in items)
        {
            var extent = item.Hypothetical + MainMargins(item, row);
            var needed = line.Count == 0 ? extent : used + gap + extent;
            if (line.Count > 0 && needed > limit)
            {
                lines.Add(line);
                line = [];
                needed = extent;
            }
            line.Add(item);
            used = needed;
        }
        if (line.Count > 0) lines.Add(line);
        return lines;
    }

    /// <summary>
    /// Sizes the items line by line, spreads the lines across the cross axis,
    /// then places every item, shifted by the scroll offset.
    /// </summary>
    private static void ArrangeFlow(LayoutNode node, Rect content, Rect visible)
    {
        var style = node.Style;
        var row = IsRow(style.FlexDirection);
        var wrap = style.FlexWrap != FlexWrap.NoWrap;
        var gap = row ? style.ColumnGap : style.RowGap;
        var crossGap = row ? style.RowGap : style.ColumnGap;
        var mainSize = row ? content.Width : content.Height;
        var crossSize = row ? content.Height : content.Width;

        var items = FlexItems(node, content.Width, content.Height, row, mainSize);
        if (items.Count == 0) return;

        var lines = Lines(items, wrap ? mainSize : null, gap, row);
        var lineCross = new int[lines.Count];
        for (var l = 0; l < lines.Count; l++)
        {
            ResolveMainSizes(lines[l], mainSize, gap, row);
            foreach (var item in lines[l])
            {
                item.Cross = CrossSize(item, crossSize, row);
                lineCross[l] = Math.Max(lineCross[l], item.Cross + CrossMargins(item, row));
            }
        }

        int contentCross;
        var (crossStart, crossBetween) = (0, 0);
        if (wrap)
        {
            contentCross = lineCross.Sum() + crossGap * (lines.Count - 1);
            (crossStart, crossBetween) = AlignLines(style.AlignContent, lineCross, crossSize - contentCross);
        }
        else
        {
            // A single line fills the container's cross size, as in CSS.
            lineCross[0] = crossSize;
            contentCross = crossSize;
        }

        var mainContent = wrap ? lines.Max(line => MainUsed(line, gap, row)) : MainUsed(lines[0], gap, row);
        node.ContentSize = row ? new Size(mainContent, contentCross) : new Size(contentCross, mainContent);

        var placement = new LinePlacement(node, content, visible, row, mainSize, gap, ScrollOffset(style));
        var crossPosition = crossStart;
        foreach (var l in LineOrder(lines.Count, style.FlexWrap))
        {
            placement.Place(lines[l], crossPosition, lineCross[l]);
            crossPosition += lineCross[l] + crossGap + crossBetween;
        }
    }

    private static IEnumerable<int> LineOrder(int count, FlexWrap wrap)
    {
        var order = Enumerable.Range(0, count);
        return wrap == FlexWrap.WrapReverse ? order.Reverse() : order;
    }

    private static List<Item> FlexItems(LayoutNode node, int? innerWidth, int? innerHeight, bool row, int? innerMain)
    {
        var items = new List<Item>();
        foreach (var child in InFlowChildren(node))
        {
            var cs = child.Style;
            var margin = cs.Margin;
            var availW = Inner(innerWidth, margin.Horizontal);
            var availH = Inner(innerHeight, margin.Vertical);
            var hypothetical = Hypothetical(child, row, availW, availH, innerMain);
            items.Add(new Item
            {
                Node = child,
                Margin = margin,
                AvailW = availW,
                AvailH = availH,
                Hypothetical = hypothetical,
                Main = hypothetical,
                Max = (row ? cs.MaxWidth : cs.MaxHeight).Resolve(row ? availW : availH),
                Grow = Math.Max(0, cs.FlexGrow),
                Shrink = Math.Max(0, cs.FlexShrink),
            });
        }
        return items;
    }

    /// <summary>An item's explicit cross size, or its content's at its final main size.</summary>
    private static int CrossSize(Item item, int lineCross, bool row)
    {
        var cs = item.Node.Style;
        var availCross = Math.Max(0, lineCross - CrossMargins(item, row));
        var explicitCross = (row ? cs.Height : cs.Width).Resolve(availCross);

        int cross;
        if (explicitCross is { } size)
        {
            cross = size;
        }
        else
        {
            var measured = row
                ? Measure(item.Node, item.Main, null)
                : Measure(item.Node, availCross, null);
            cross = row ? measured.Height : measured.Width;
        }

        var minCross = (row ? cs.MinHeight : cs.MinWidth).Resolve(availCross);
        var maxCross = (row ? cs.MaxHeight : cs.MaxWidth).Resolve(availCross);
        return Math.Max(0, Clamp(cross, minCross, maxCross));
    }

    /// <summary>
    /// Shares leftover cross space between wrapped lines, stretching them in
    /// place or returning where the first starts and the space between them.
    /// </summary>
    private static (int Start, int Between) AlignLines(AlignContent alignContent, int[] lineCross, int leftover)
    {
        if (leftover <= 0) return (0, 0);
        var count = lineCross.Length;
        switch (alignContent)
        {
            case AlignContent.Stretch:
                var each = leftover / count;
                var extra = leftover - each * count;
                for (var l = 0; l < count; l++)
                {
                    lineCross[l] += each + (l < extra ? 1 : 0);
                }
                return (0, 0);
            case AlignContent.Center:
                return (leftover / 2, 0);
            case AlignContent.FlexEnd:
                return (leftover, 0);
            case AlignContent.SpaceBetween when count > 1:
                return (0, leftover / (count - 1));
            case AlignContent.SpaceAround:
                var between = leftover / count;
                return (between / 2, between);
            default:
                return (0, 0);
        }
    }

    /// <summary>Places the items of one line within the container's content box.</summary>
    private sealed class LinePlacement(LayoutNode node, Rect content, Rect visible, bool row, int mainSize, int gap, (int X, int Y) scroll)
    {
        private readonly bool _reverse = node.Style.FlexDirection is FlexDirection.RowReverse or FlexDirection.ColumnReverse;

        public void Place(List<Item> line, int crossPosition, int lineCross)
        {
            var style = node.Style;
            var free = mainSize - MainUsed(line, gap, row);
            var (start, between) = Justify(style.JustifyContent, free, line.Count);
            var position = start;
            foreach (var item in line)
            {
                var marginStart = row ? item.Margin.Left : item.Margin.Top;
                var marginEnd = row ? item.Margin.Right : item.Margin.Bottom;
                var mainPos = position + marginStart;
                position += marginStart + item.Main + marginEnd + gap + between;
                if (_reverse) mainPos = mainSize - mainPos - item.Main;

                var align = Align(item.Node.Style.AlignSelf, style.AlignItems);
                var itemCross = align == AlignItems.Stretch ? StretchedCross(item, lineCross) : item.Cross;
                var crossPos = crossPosition + CrossOffset(item, align, itemCross, lineCross);

                var childRect = row
                    ? new Rect(content.X + mainPos - scroll.X, content.Y + crossPos - scroll.Y, item.Main, itemCross)
                    : new Rect(content.X + crossPos - scroll.X, content.Y + mainPos - scroll.Y, itemCross, item.Main);
                ArrangeChild(node, item.Node, childRect, visible);
            }
        }

        /// <summary>A stretched item fills the line unless it sets its own cross size.</summary>
        private int StretchedCross(Item item, int lineCross)
        {
            var cs = item.Node.Style;
            if (!(row ? cs.Height : cs.Width).IsAuto) return item.Cross;

            var min = (row ? cs.MinHeight : cs.MinWidth).Resolve(lineCross);
            var max = (row ? cs.MaxHeight : cs.MaxWidth).Resolve(lineCross);
            return Math.Max(0, Clamp(lineCross - CrossMargins(item, row), min, max));
        }

        private int CrossOffset(Item item, AlignItems align, int itemCross, int lineCross)
        {
            var marginStart = row ? item.Margin.Top : item.Margin.Left;
            var marginEnd = row ? item.Margin.Bottom : item.Margin.Right;
            return align switch
            {
                AlignItems.Center => (lineCross - itemCross - marginStart - marginEnd) / 2 + marginStart,
                AlignItems.FlexEnd => lineCross - itemCross - marginEnd,
                _ => marginStart,
            };
        }
    }

    /// <summary>
    /// Resolves flexible lengths with the CSS freeze loop, then rounds so the
    /// items fill the container exactly.
    /// </summary>
    private static void ResolveMainSizes(List<Item> items, int mainSize, int gap, bool row)
    {
        var margins = 0;
        var hypotheticalSum = 0;
        foreach (var item in items)
        {
            margins += MainMargins(item, row);
            hypotheticalSum += item.Hypothetical;
        }
        var gaps = gap * (items.Count - 1);
        var available = mainSize - margins - gaps;
        var growing = available > hypotheticalSum;

        var anyFlexible = false;
        foreach (var item in items)
        {
            item.Target = item.Hypothetical;
            item.Frozen = growing ? item.Grow <= 0 : item.Shrink <= 0;
            item.Main = item.Hypothetical;
            anyFlexible |= !item.Frozen;
        }
        if (!anyFlexible) return;

        for (var iteration = 0; iteration < items.Count + 1; iteration++)
        {
            var unfrozen = items.Where(i => !i.Frozen).ToList();
            if (unfrozen.Count == 0) break;

            var frozenSum = items.Where(i => i.Frozen).Sum(i => i.Target);
            var remaining = available - frozenSum - unfrozen.Sum(i => i.Hypothetical);

            var weights = unfrozen.Select(i => growing ? i.Grow : i.Shrink * i.Hypothetical).ToList();
            var totalWeight = weights.Sum();
            for (var index = 0; index < unfrozen.Count; index++)
            {
                var share = totalWeight > 0 ? remaining * weights[index] / totalWeight : 0;
                unfrozen[index].Target = unfrozen[index].Hypothetical + share;
            }

            var violated = false;
            foreach (var item in unfrozen)
            {
                // A growing item is already above its minimum.
                var clamped = Math.Max(0, Clamp(item.Target, growing ? null : item.Min(row), item.Max));
                if (Math.Abs(clamped - item.Target) > Epsilon)
                {
                    item.Target = clamped;
                    item.Frozen = true;
                    violated = true;
                }
            }
            if (!violated) break;
        }

        RoundMainSizes(items, available);
    }

    /// <summary>
    /// Floors every size, then hands the leftover cells to the flexible items
    /// with the largest fractions.
    /// </summary>
    private static void RoundMainSizes(List<Item> items, int available)
    {
        foreach (var item in items)
        {
            item.Main = (int)Math.Floor(item.Target + Epsilon);
        }

        var leftover = Math.Max(0, available) - items.Sum(i => i.Main);
        var byFraction = items
            .Where(i => !i.Frozen)
            .OrderByDescending(i => i.Target - Math.Floor(i.Target + Epsilon));
        foreach (var item in byFraction.Take(Math.Max(0, leftover)))
        {
            item.Main++;
        }

        foreach (var item in items)
        {
            item.Main = Math.Max(0, item.Main);
        }
    }

    /// <summary>Places an absolutely positioned child by its offsets inside the parent's padding box.</summary>
    private static void ArrangeAbsolute(LayoutNode child, Rect paddingBox, Rect visible)
    {
        var cs = child.Style;
        var margin = cs.Margin;
        var availableWidth = Math.Max(0, paddingBox.Width - margin.Horizontal);
        var availableHeight = Math.Max(0, paddingBox.Height - margin.Vertical);

        int width;
        if (cs.Left is { } left && cs.Right is { } right && cs.Width.IsAuto)
        {
            width = Math.Max(0, paddingBox.Width - left - right - margin.Horizontal);
        }
        else
        {
            var offset = cs.Left ?? cs.Right ?? 0;
            width = Measure(child, Math.Max(0, availableWidth - offset), availableHeight).Width;
        }

        int height;
        if (cs.Top is { } top && cs.Bottom is { } bottom && cs.Height.IsAuto)
        {
            height = Math.Max(0, paddingBox.Height - top - bottom - margin.Vertical);
        }
        else
        {
            var offset = cs.Top ?? cs.Bottom ?? 0;
            height = Measure(child, width, Math.Max(0, availableHeight - offset)).Height;
        }

        var x = AbsoluteStart(paddingBox.X, paddingBox.Right, cs.Left, cs.Right, margin.Left, margin.Right, width);
        var y = AbsoluteStart(paddingBox.Y, paddingBox.Bottom, cs.Top, cs.Bottom, margin.Top, margin.Bottom, height);
        ArrangeChild(child.LayoutParent ?? child, child, new Rect(x, y, width, height), visible);
    }

    private static int AbsoluteStart(int boxStart, int boxEnd, int? startOffset, int? endOffset, int marginStart, int marginEnd, int size)
    {
        if (startOffset is { } start) return boxStart + start + marginStart;
        if (endOffset is { } end) return boxEnd - end - marginEnd - size;
        return boxStart + marginStart;
    }

    /// <summary>Where a run of items starts and the space between them.</summary>
    internal static (int Start, int Between) Justify(JustifyContent justify, int free, int count)
    {
        // Overflowing content still aligns to the end or the center, as in CSS;
        // the space-* values fall back to flex-start.
        if (free < 0)
        {
            return justify switch
            {
                JustifyContent.FlexEnd => (free, 0),
                JustifyContent.Center => (free / 2, 0),
                _ => (0, 0),
            };
        }
        if (free == 0) return (0, 0);
        return justify switch
        {
            JustifyContent.Center => (free / 2, 0),
            JustifyContent.FlexEnd => (free, 0),
            JustifyContent.SpaceBetween when count > 1 => (0, free / (count - 1)),
            JustifyContent.SpaceAround => (free / count / 2, free / count),
            JustifyContent.SpaceEvenly => (free / (count + 1), free / (count + 1)),
            _ => (0, 0),
        };
    }

    internal static AlignItems Align(AlignSelf self, AlignItems items) => self switch
    {
        AlignSelf.Stretch => AlignItems.Stretch,
        AlignSelf.FlexStart => AlignItems.FlexStart,
        AlignSelf.Center => AlignItems.Center,
        AlignSelf.FlexEnd => AlignItems.FlexEnd,
        _ => items,
    };

    private static bool IsRow(FlexDirection direction) =>
        direction is FlexDirection.Row or FlexDirection.RowReverse;

    internal static int? Inner(int? outer, int inset) => outer is { } o ? Math.Max(0, o - inset) : null;

    internal static int Clamp(int value, int? min, int? max)
    {
        if (max is { } hi) value = Math.Min(value, hi);
        if (min is { } lo) value = Math.Max(value, lo);
        return value;
    }

    private static double Clamp(double value, int? min, int? max)
    {
        if (max is { } hi) value = Math.Min(value, hi);
        if (min is { } lo) value = Math.Max(value, lo);
        return value;
    }
}
