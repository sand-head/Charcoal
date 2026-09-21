using SlopTui.Layout;
using SlopTui.Rendering;

namespace SlopTui.Components;

/// <summary>
/// A node in the tree the renderer builds from Blazor's render batches. As in
/// the browser renderer, components and regions are containers that count as
/// siblings but take no part in layout.
/// </summary>
public abstract class HostNode
{
    private readonly List<HostNode> _children = [];

    public HostNode? Parent { get; private set; }

    /// <summary>Logical children, containers included, as render edits count them.</summary>
    public IReadOnlyList<HostNode> Children => _children;

    /// <summary>This node if it is an element, else its nearest element ancestor.</summary>
    public HostElement? ClosestElement
    {
        get
        {
            for (var node = this; node is not null; node = node.Parent)
            {
                if (node is HostElement element) return element;
            }
            return null;
        }
    }

    internal void InsertChild(int index, HostNode child)
    {
        child.Parent?.DetachChild(child);
        _children.Insert(Math.Clamp(index, 0, _children.Count), child);
        child.Parent = this;
        StructureChanged();
    }

    internal HostNode RemoveChildAt(int index)
    {
        var child = _children[index];
        _children.RemoveAt(index);
        child.Parent = null;
        StructureChanged();
        return child;
    }

    private void DetachChild(HostNode child)
    {
        _children.Remove(child);
        child.Parent = null;
        StructureChanged();
    }

    internal void Permute(IReadOnlyList<(int From, int To)> moves)
    {
        var snapshot = _children.ToArray();
        foreach (var (from, to) in moves) _children[to] = snapshot[from];
        StructureChanged();
    }

    protected void StructureChanged() => ClosestElement?.DescendantsChanged(structural: true);

    /// <summary>Text changed but the tree did not, so the cached layout children still hold.</summary>
    protected void ContentChanged() => ClosestElement?.DescendantsChanged(structural: false);

    /// <summary>Every node below this one, depth first.</summary>
    public IEnumerable<HostNode> Descendants()
    {
        var stack = new Stack<HostNode>();
        for (var i = _children.Count - 1; i >= 0; i--) stack.Push(_children[i]);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            yield return node;
            for (var i = node._children.Count - 1; i >= 0; i--) stack.Push(node._children[i]);
        }
    }
}

/// <summary>A component or a region.</summary>
public sealed class HostContainer : HostNode
{
    /// <summary>The component rendered into this container, or null for a region.</summary>
    public int? ComponentId { get; internal set; }
}

/// <summary>A text frame, collected into runs by the enclosing <c>text</c> element.</summary>
public sealed class HostTextNode : HostNode
{
    private string _text = "";

    public string Text
    {
        get => _text;
        internal set
        {
            if (_text == value) return;
            _text = value;
            ContentChanged();
        }
    }
}

/// <summary>
/// A <c>box</c>, <c>text</c> or <c>canvas</c> element. Its layout children are
/// its descendant elements with containers flattened out; a <c>text</c>
/// element is a leaf whose nested <c>text</c> elements are styled runs.
/// </summary>
/// <remarks>
/// The style is rebuilt from all the attributes whenever one changes, so a
/// removed attribute falls back to its default.
/// </remarks>
public sealed class HostElement : HostNode
{
    private readonly Dictionary<string, object?> _attributes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ulong> _handlers = new(StringComparer.Ordinal);
    private List<LayoutNode>? _layoutChildren;
    private List<TextRun>? _runs;

    private readonly CanvasRegistry? _canvases;

    public HostElement(string name, CanvasRegistry? canvases = null)
    {
        Name = name;
        Node = ElementLayoutNode.For(this);
        _canvases = canvases;
    }

    public string Name { get; }

    public ElementLayoutNode Node { get; }

    public bool IsText => Name == "text";
    public bool IsCanvas => Name == "canvas";

    /// <summary>Whether this is a <c>text</c> element nested in another, and so a styled run.</summary>
    public bool IsInlineText => IsText && Parent?.ClosestElement is { IsText: true };

    public bool Focusable { get; private set; }

    /// <summary>Where this element wants the caret, relative to its content box.</summary>
    public (int Column, int Row)? Cursor { get; private set; }

    public IReadOnlyDictionary<string, object?> Attributes => _attributes;

    /// <summary>Blazor event handler ids by attribute name, such as <c>onkeypress</c>.</summary>
    public IReadOnlyDictionary<string, ulong> Handlers => _handlers;

    public ulong? HandlerFor(string eventName) => _handlers.TryGetValue(eventName, out var id) ? id : null;

    internal void SetAttribute(string name, object? value, ulong eventHandlerId)
    {
        if (eventHandlerId != 0)
        {
            _handlers[name] = eventHandlerId;
            return;
        }
        _attributes[name] = value;
        Rebuild();
    }

    internal void RemoveAttribute(string name)
    {
        if (_handlers.Remove(name)) return;
        if (_attributes.Remove(name)) Rebuild();
    }

    private void Rebuild()
    {
        var style = Style.Default;
        Focusable = false;
        Cursor = null;
        foreach (var (name, value) in _attributes)
        {
            switch (name)
            {
                case "style" when value is string css:
                    style = StyleParser.ApplyInline(style, css);
                    break;
                case "focusable":
                    Focusable = IsTrue(value);
                    break;
                case "cursor":
                    Cursor = ParseCursor(value);
                    break;
                case "paint":
                case "id":
                case "key":
                    break;
                default:
                    if (StyleParser.IsStyleAttribute(name)) style = StyleParser.Apply(style, name, value);
                    break;
            }
        }
        Node.Style = style;
        if (IsInlineText) Parent?.ClosestElement?.DescendantsChanged(structural: false);
    }

    private static bool IsTrue(object? value) => value switch
    {
        bool flag => flag,
        string text => !string.Equals(text, "false", StringComparison.OrdinalIgnoreCase),
        _ => false,
    };

    private static (int, int)? ParseCursor(object? value)
    {
        switch (value)
        {
            case null: return null;
            case int column: return (column, 0);
            case ValueTuple<int, int> pair: return pair;
            case string text:
                var parts = text.Trim('(', ')', ' ').Split(',', StringSplitOptions.TrimEntries);
                if (parts.Length == 1 && int.TryParse(parts[0], out var only)) return (only, 0);
                if (parts.Length == 2 && int.TryParse(parts[0], out var x) && int.TryParse(parts[1], out var y))
                {
                    return (x, y);
                }
                return null;
            default: return null;
        }
    }

    internal void DescendantsChanged(bool structural)
    {
        if (structural) _layoutChildren = null;
        _runs = null;
        Node.InvalidateLayout();
        if (IsInlineText) Parent?.ClosestElement?.DescendantsChanged(structural);
    }

    internal IReadOnlyList<LayoutNode> LayoutChildren
    {
        get
        {
            if (_layoutChildren is not null) return _layoutChildren;
            var list = new List<LayoutNode>();
            if (!IsText && !IsCanvas) Collect(this, list);
            foreach (var child in list) child.LayoutParent = Node;
            _layoutChildren = list;
            return list;
        }
    }

    private static void Collect(HostNode node, List<LayoutNode> into)
    {
        foreach (var child in node.Children)
        {
            switch (child)
            {
                case HostElement element:
                    into.Add(element.Node);
                    break;
                case HostContainer container:
                    Collect(container, into);
                    break;
            }
        }
    }

    /// <summary>The styled runs of a text leaf.</summary>
    public IReadOnlyList<TextRun> Runs
    {
        get
        {
            if (_runs is not null) return _runs;
            var runs = new List<TextRun>();
            if (IsText) CollectRuns(this, Node.Style.Color, Node.Style.Background, Node.Style.TextStyle, runs);
            _runs = runs;
            return runs;
        }
    }

    private static void CollectRuns(HostNode node, Color fg, Color bg, TextStyle style, List<TextRun> into)
    {
        foreach (var child in node.Children)
        {
            switch (child)
            {
                case HostTextNode text:
                    if (text.Text.Length > 0 && !IsMarkupWhitespace(text.Text))
                    {
                        into.Add(new TextRun(text.Text, fg, bg, style));
                    }
                    break;
                case HostElement { IsText: true } inline when inline.Attributes.ContainsKey("newline"):
                    into.Add(new TextRun("\n", fg, bg, style));
                    break;
                case HostElement { IsText: true } inline:
                    var inlineStyle = inline.Node.Style;
                    var inlineFg = inlineStyle.Color.Kind == ColorKind.Default ? fg : inlineStyle.Color;
                    var inlineBg = inlineStyle.Background.Kind == ColorKind.Default ? bg : inlineStyle.Background;
                    CollectRuns(inline, inlineFg, inlineBg, style | inlineStyle.TextStyle, into);
                    break;
                case HostContainer container:
                    CollectRuns(container, fg, bg, style, into);
                    break;
            }
        }
    }

    /// <summary>Whitespace containing a line break: the indentation between elements on separate lines.</summary>
    private static bool IsMarkupWhitespace(string text)
    {
        var hasLineBreak = false;
        foreach (var c in text)
        {
            if (c is '\n' or '\r')
            {
                hasLineBreak = true;
            }
            else if (!char.IsWhiteSpace(c))
            {
                return false;
            }
        }
        return hasLineBreak;
    }

    /// <summary>Paints a canvas with the delegate registered under its <c>paint</c> attribute.</summary>
    public void Paint(CellBuffer buffer, Rect rect)
    {
        if (!_attributes.TryGetValue("paint", out var paint) || paint is not string key) return;
        _canvases?.Resolve(key)?.Invoke(buffer, rect);
    }

    /// <summary>The deepest element whose rect contains the point, or null.</summary>
    public HostElement? HitTest(int x, int y)
    {
        if (Node.Style.Display == Display.None || !Node.Layout.Contains(x, y)) return null;
        for (var i = LayoutChildren.Count - 1; i >= 0; i--)
        {
            if (LayoutChildren[i] is not ElementLayoutNode child) continue;
            if (child.Element.HitTest(x, y) is { } hit) return hit;
        }
        return this;
    }

    /// <summary>The ancestor elements, nearest first.</summary>
    public IEnumerable<HostElement> AncestorElements()
    {
        for (var node = Parent; node is not null; node = node.Parent)
        {
            if (node is HostElement element) yield return element;
        }
    }
}

/// <summary>The layout node of a <see cref="HostElement"/>.</summary>
public class ElementLayoutNode : LayoutNode
{
    public ElementLayoutNode(HostElement element) => Element = element;

    public HostElement Element { get; }

    public override IReadOnlyList<LayoutNode> Children => Element.LayoutChildren;

    public static ElementLayoutNode For(HostElement element) => element.Name switch
    {
        "text" => new TextLayoutNode(element),
        "canvas" => new CanvasLayoutNode(element),
        _ => new ElementLayoutNode(element),
    };
}

/// <summary>A <c>text</c> element's node, measured by wrapping its runs.</summary>
public sealed class TextLayoutNode : ElementLayoutNode, ITextContent
{
    public TextLayoutNode(HostElement element) : base(element) { }

    public override bool IsLeaf => true;

    public IReadOnlyList<TextRun> Runs => Element.Runs;

    public override Size MeasureContent(int? availableWidth, int? availableHeight) =>
        TextLayout.Measure(Element.Runs, availableWidth, Style.Wrap);
}

/// <summary>A <c>canvas</c> element's node, painted by a delegate.</summary>
public sealed class CanvasLayoutNode : ElementLayoutNode, ICustomPaint
{
    public CanvasLayoutNode(HostElement element) : base(element) { }

    public override bool IsLeaf => true;

    public void Paint(CellBuffer buffer, Rect rect) => Element.Paint(buffer, rect);
}

/// <summary>
/// Paint delegates for <c>canvas</c> elements, by key. Blazor cannot pass a
/// delegate as an element attribute, so the element carries the key instead.
/// </summary>
public sealed class CanvasRegistry
{
    private readonly Dictionary<string, Action<CellBuffer, Rect>> _painters = new(StringComparer.Ordinal);
    private long _next;

    public string NewKey() => $"canvas-{Interlocked.Increment(ref _next)}";

    public void Register(string key, Action<CellBuffer, Rect> paint)
    {
        lock (_painters) _painters[key] = paint;
    }

    public void Unregister(string key)
    {
        lock (_painters) _painters.Remove(key);
    }

    public Action<CellBuffer, Rect>? Resolve(string key)
    {
        lock (_painters) return _painters.GetValueOrDefault(key);
    }
}
