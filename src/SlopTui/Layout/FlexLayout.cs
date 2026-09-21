namespace SlopTui.Layout;

/// <summary>
/// Flexbox on integer cells, as a cached measure pass and an arrange pass.
/// Each node's rect is its border box in absolute terminal cells.
/// </summary>
/// <remarks>
/// Constraints are the room left for a node's border box after its margins,
/// and percentages resolve against that room rather than the parent's whole
/// content box, so a child never overflows because of its margins.
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
            Size content = node.IsLeaf
                ? node.MeasureContent(innerWidth, innerHeight)
                : MeasureChildren(node, innerWidth, innerHeight);
            width = explicitWidth ?? content.Width + inset.Horizontal;
            height = explicitHeight ?? content.Height + inset.Vertical;
        }

        width = Clamp(width, style.MinWidth.Resolve(availableWidth), style.MaxWidth.Resolve(availableWidth));
        height = Clamp(height, style.MinHeight.Resolve(availableHeight), style.MaxHeight.Resolve(availableHeight));
        return new Size(Math.Max(0, width), Math.Max(0, height));
    }

    /// <summary>
    /// A container's content size from its in-flow children's hypothetical
    /// sizes, before any growing or shrinking.
    /// </summary>
    private static Size MeasureChildren(LayoutNode node, int? innerWidth, int? innerHeight)
    {
        var style = node.Style;
        var row = IsRow(style.FlexDirection);
        var gap = row ? style.ColumnGap : style.RowGap;

        var main = 0;
        var cross = 0;
        var count = 0;
        foreach (var child in node.Children)
        {
            var cs = child.Style;
            if (cs.Display == Display.None || cs.Position == Position.Absolute) continue;

            var margin = cs.Margin;
            var availW = Inner(innerWidth, margin.Horizontal);
            var availH = Inner(innerHeight, margin.Vertical);
            var hypothetical = Hypothetical(child, row, availW, availH, row ? innerWidth : innerHeight);
            var size = Measure(child, availW, availH);
            var childCross = row ? size.Height + margin.Vertical : size.Width + margin.Horizontal;

            main += hypothetical + (row ? margin.Horizontal : margin.Vertical);
            cross = Math.Max(cross, childCross);
            count++;
        }
        if (count > 1) main += gap * (count - 1);

        return row ? new Size(main, cross) : new Size(cross, main);
    }

    /// <summary>The item's flex basis, explicit size or content size, clamped by its min and max.</summary>
    private static int Hypothetical(LayoutNode child, bool row, int? availW, int? availH, int? containerMain)
    {
        var cs = child.Style;
        var availMain = row ? availW : availH;
        var basis = cs.FlexBasis.Resolve(containerMain)
            ?? (row ? cs.Width : cs.Height).Resolve(availMain);
        if (basis is null)
        {
            var measured = Measure(child, availW, availH);
            basis = row ? measured.Width : measured.Height;
        }
        var min = (row ? cs.MinWidth : cs.MinHeight).Resolve(availMain);
        var max = (row ? cs.MaxWidth : cs.MaxHeight).Resolve(availMain);
        return Math.Max(0, Clamp(basis.Value, min, max));
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

        ArrangeFlow(node, content, childVisible);

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
    private static void ArrangeChild(LayoutNode parent, LayoutNode child, Rect rect, Rect visible)
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

    private sealed class Item
    {
        public required LayoutNode Node;
        public required Edges Margin;
        public int Hypothetical;
        public double Target;
        public int Main;
        public int Cross;
        public bool Frozen;
        public int? Min;
        public int? Max;
        public double Grow;
        public double Shrink;
    }

    private static void ArrangeFlow(LayoutNode node, Rect content, Rect visible)
    {
        var style = node.Style;
        var row = IsRow(style.FlexDirection);
        var reverse = style.FlexDirection is FlexDirection.RowReverse or FlexDirection.ColumnReverse;
        var gap = row ? style.ColumnGap : style.RowGap;
        var mainSize = row ? content.Width : content.Height;
        var crossSize = row ? content.Height : content.Width;

        var items = new List<Item>();
        foreach (var child in node.Children)
        {
            var cs = child.Style;
            if (cs.Display == Display.None || cs.Position == Position.Absolute) continue;
            var margin = cs.Margin;
            var availW = Inner(content.Width, margin.Horizontal);
            var availH = Inner(content.Height, margin.Vertical);
            var availMain = row ? availW : availH;
            var item = new Item
            {
                Node = child,
                Margin = margin,
                Hypothetical = Hypothetical(child, row, availW, availH, mainSize),
                Min = (row ? cs.MinWidth : cs.MinHeight).Resolve(availMain),
                Max = (row ? cs.MaxWidth : cs.MaxHeight).Resolve(availMain),
                Grow = Math.Max(0, cs.FlexGrow),
                Shrink = Math.Max(0, cs.FlexShrink),
            };
            items.Add(item);
        }
        if (items.Count == 0) return;

        ResolveMainSizes(items, mainSize, gap, row);
        foreach (var item in items)
        {
            item.Cross = CrossSize(item, style.AlignItems, crossSize, row);
        }

        var used = gap * (items.Count - 1);
        foreach (var item in items)
        {
            used += item.Main + (row ? item.Margin.Horizontal : item.Margin.Vertical);
        }
        var free = mainSize - used;
        var (start, between) = Justify(style.JustifyContent, free, items.Count);

        var position = start;
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var marginStart = row ? item.Margin.Left : item.Margin.Top;
            var marginEnd = row ? item.Margin.Right : item.Margin.Bottom;
            var mainPos = position + marginStart;
            position += marginStart + item.Main + marginEnd + gap + between;

            var align = Align(item.Node.Style.AlignSelf, style.AlignItems);
            var crossStart = row ? item.Margin.Top : item.Margin.Left;
            var crossEnd = row ? item.Margin.Bottom : item.Margin.Right;
            var crossPos = align switch
            {
                AlignItems.Center => (crossSize - item.Cross - crossStart - crossEnd) / 2 + crossStart,
                AlignItems.FlexEnd => crossSize - item.Cross - crossEnd,
                _ => crossStart,
            };

            if (reverse) mainPos = mainSize - mainPos - item.Main;

            var childRect = row
                ? new Rect(content.X + mainPos, content.Y + crossPos, item.Main, item.Cross)
                : new Rect(content.X + crossPos, content.Y + mainPos, item.Cross, item.Main);
            ArrangeChild(node, item.Node, childRect, visible);
        }
    }

    /// <summary>An item's explicit cross size, or the whole line when stretched, or its content's.</summary>
    private static int CrossSize(Item item, AlignItems alignItems, int lineCross, bool row)
    {
        var cs = item.Node.Style;
        var marginCross = row ? item.Margin.Vertical : item.Margin.Horizontal;
        var availCross = Math.Max(0, lineCross - marginCross);
        var explicitCross = (row ? cs.Height : cs.Width).Resolve(availCross);

        int cross;
        if (explicitCross is { } size)
        {
            cross = size;
        }
        else if (Align(cs.AlignSelf, alignItems) == AlignItems.Stretch)
        {
            cross = availCross;
        }
        else
        {
            var measured = row
                ? Measure(item.Node, item.Main, availCross)
                : Measure(item.Node, availCross, item.Main);
            cross = row ? measured.Height : measured.Width;
        }

        var minCross = (row ? cs.MinHeight : cs.MinWidth).Resolve(availCross);
        var maxCross = (row ? cs.MaxHeight : cs.MaxWidth).Resolve(availCross);
        return Math.Max(0, Clamp(cross, minCross, maxCross));
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
            margins += row ? item.Margin.Horizontal : item.Margin.Vertical;
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
                var clamped = Math.Max(0, Clamp(item.Target, item.Min, item.Max));
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

    private static (int Start, int Between) Justify(JustifyContent justify, int free, int count)
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

    private static AlignItems Align(AlignSelf self, AlignItems items) => self switch
    {
        AlignSelf.Stretch => AlignItems.Stretch,
        AlignSelf.FlexStart => AlignItems.FlexStart,
        AlignSelf.Center => AlignItems.Center,
        AlignSelf.FlexEnd => AlignItems.FlexEnd,
        _ => items,
    };

    private static bool IsRow(FlexDirection direction) =>
        direction is FlexDirection.Row or FlexDirection.RowReverse;

    private static int? Inner(int? outer, int inset) => outer is { } o ? Math.Max(0, o - inset) : null;

    private static int Clamp(int value, int? min, int? max)
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
