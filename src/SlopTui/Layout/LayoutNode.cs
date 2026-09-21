namespace SlopTui.Layout;

/// <summary>
/// A node the layout engine can arrange. Measurements are cached per node, so
/// anything that changes what a node measures must call <see cref="InvalidateLayout"/>.
/// </summary>
public abstract class LayoutNode
{
    private Style _style = Style.Default;

    /// <summary>Setting a different style invalidates the layout.</summary>
    public Style Style
    {
        get => _style;
        set
        {
            if (ReferenceEquals(_style, value) || _style == value) return;
            _style = value;
            InvalidateLayout();
        }
    }

    /// <summary>The node this one is laid out inside, or null for the root.</summary>
    public LayoutNode? LayoutParent { get; internal set; }

    /// <summary>The children the engine arranges, in order.</summary>
    public abstract IReadOnlyList<LayoutNode> Children { get; }

    /// <summary>The rect the engine assigned, in absolute terminal cells.</summary>
    public Rect Layout { get; internal set; }

    /// <summary>A leaf's size comes from <see cref="MeasureContent"/> rather than from children.</summary>
    public virtual bool IsLeaf => Children.Count == 0;

    /// <summary>The intrinsic size of a leaf's content; a null constraint is unbounded.</summary>
    public virtual Size MeasureContent(int? availableWidth, int? availableHeight) => Size.Empty;

    /// <summary>Marks this node and its ancestors for re-measuring; siblings keep their cache.</summary>
    public void InvalidateLayout()
    {
        for (var node = this; node is not null && !(node.LayoutDirty && node.ArrangeDirty); node = node.LayoutParent)
        {
            node.LayoutDirty = true;
            node.ArrangeDirty = true;
        }
    }

    internal bool LayoutDirty { get; set; } = true;

    internal bool ArrangeDirty { get; set; } = true;

    /// <summary>
    /// Set when the node was placed off screen and its subtree left unarranged,
    /// so the next arrange that finds it visible must not skip it.
    /// </summary>
    internal bool ArrangeDeferred { get; set; }

    internal bool HasDeferredChildren { get; set; }
    internal Rect ArrangedVisible { get; set; }

    internal (int? Width, int? Height, Size Result)? MeasureCache { get; set; }

    /// <summary>
    /// The previous cache entry. A node is measured once while its container is
    /// measured and again while it is arranged, often under different constraints.
    /// </summary>
    internal (int? Width, int? Height, Size Result)? MeasureCacheAlt { get; set; }
}

/// <summary>A layout node with an explicit child list.</summary>
public class BoxNode : LayoutNode
{
    private readonly List<LayoutNode> _children = [];

    public BoxNode() { }

    public BoxNode(Style style, params LayoutNode[] children)
    {
        Style = style;
        foreach (var child in children) Add(child);
    }

    public override IReadOnlyList<LayoutNode> Children => _children;

    public void Add(LayoutNode child)
    {
        child.LayoutParent = this;
        _children.Add(child);
        InvalidateLayout();
    }

    public void Remove(LayoutNode child)
    {
        if (!_children.Remove(child)) return;
        child.LayoutParent = null;
        InvalidateLayout();
    }
}

/// <summary>A leaf measured by a delegate.</summary>
public class TextLeafNode : LayoutNode
{
    private readonly Func<int?, int?, Size> _measure;

    public TextLeafNode(Func<int?, int?, Size> measure, Style? style = null)
    {
        _measure = measure;
        if (style is not null) Style = style;
    }

    /// <summary>A leaf of one fixed size whatever the constraints.</summary>
    public TextLeafNode(int width, int height, Style? style = null)
        : this((_, _) => new Size(width, height), style) { }

    public override IReadOnlyList<LayoutNode> Children => [];

    public override bool IsLeaf => true;

    public override Size MeasureContent(int? availableWidth, int? availableHeight) =>
        _measure(availableWidth, availableHeight);
}
