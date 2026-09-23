using SlopTui.Layout;
using SlopTui.Rendering;
using SlopTui.Styling;

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
        RestyleInserted(child);
        StructureChanged();
    }

    /// <summary>
    /// Resolves the styles of inserted elements, now that their ancestors are
    /// known. A container can arrive already holding parsed markup.
    /// </summary>
    private static void RestyleInserted(HostNode child)
    {
        if (child is HostElement element)
        {
            element.Restyle();
            return;
        }
        foreach (var inner in child.Descendants().OfType<HostElement>())
        {
            inner.Restyle();
        }
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

/// <summary>A text frame, collected into a text leaf by the block it stands in.</summary>
public sealed class HostTextNode : HostNode
{
    private string _text = "";

    public string Text
    {
        get => _text;
        internal set
        {
            if (_text == value) return;
            // Empty and whitespace-only text may make no leaf, so a change
            // between those and real text regroups the block's leaves.
            var regroup = KindOf(_text) != KindOf(value);
            _text = value;
            if (regroup)
            {
                StructureChanged();
            }
            else
            {
                ContentChanged();
            }
        }
    }

    private enum TextKind { Empty, Whitespace, Content }

    private static TextKind KindOf(string text)
    {
        if (text.Length == 0) return TextKind.Empty;
        return string.IsNullOrWhiteSpace(text) ? TextKind.Whitespace : TextKind.Content;
    }
}

/// <summary>
/// An element under its HTML name, with its attributes and resolved style.
/// A block, flex or grid element is a box the layout engine arranges; an
/// inline element is a styled run of the text around it; <c>img</c>,
/// <c>canvas</c>, <c>input</c> and <c>textarea</c> are leaves that paint
/// themselves.
/// </summary>
/// <remarks>
/// <para>
/// A box's layout children are its descendant boxes with containers
/// flattened out. Text and inline elements between them are grouped into
/// anonymous text leaves, as CSS wraps a block's inline content.
/// </para>
/// <para>
/// The style is resolved through the whole cascade whenever an attribute
/// changes, so a removed attribute falls back to what the sheets say.
/// </para>
/// </remarks>
public sealed class HostElement : HostNode
{
    private static readonly IReadOnlySet<string> NoClasses = new HashSet<string>();

    private readonly Dictionary<string, object?> _attributes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ulong> _handlers = new(StringComparer.Ordinal);
    private readonly StyleContext? _styles;
    private readonly bool _deferred;
    private List<LayoutNode>? _layoutChildren;
    private List<TextRun>? _runs;

    public HostElement(string name, StyleContext? styles = null, Graphics? graphics = null)
        : this(name, styles, graphics, deferred: false) { }

    /// <param name="deferred">
    /// Whether to leave the style unresolved until insertion. The renderer
    /// sets every attribute before inserting, so resolving once at insertion
    /// saves resolving once per attribute.
    /// </param>
    internal HostElement(string name, StyleContext? styles, Graphics? graphics, bool deferred)
    {
        Name = name.ToLowerInvariant();
        Graphics = graphics;
        IsBreak = Name == "br";
        IsImage = Name == "img";
        IsCanvas = Name == "canvas";
        IsControl = Name is "input" or "textarea";
        Node = ElementLayoutNode.For(this);
        _styles = styles;
        _deferred = deferred;
        if (!deferred) Rebuild(null);
    }

    /// <summary>The terminal's picture capabilities, or null outside an app.</summary>
    public Graphics? Graphics { get; }

    /// <summary>The classes in the <c>class</c> attribute.</summary>
    public IReadOnlySet<string> Classes { get; private set; } = NoClasses;

    public string? Id { get; private set; }

    /// <summary>The <c>style</c> attribute as written, or null.</summary>
    public string? InlineStyle { get; private set; }

    /// <summary>The element name, lower-cased, which type selectors match.</summary>
    public string Name { get; }

    public ElementLayoutNode Node { get; }

    /// <summary>Whether this element is a run within the text around it, as <c>display: inline</c> makes it.</summary>
    public bool IsInline => Node.Style.IsInline && !IsImage && !IsCanvas && !IsControl;

    /// <summary>Whether this is a <c>&lt;br&gt;</c>.</summary>
    public bool IsBreak { get; }

    public bool IsImage { get; }

    public bool IsCanvas { get; }

    /// <summary>Whether this is an <c>&lt;input&gt;</c> or a <c>&lt;textarea&gt;</c>.</summary>
    public bool IsControl { get; }

    /// <summary>The form control's text, caret and attributes, or null for other elements.</summary>
    public TextControlLayoutNode? Control => Node as TextControlLayoutNode;

    /// <summary>Whether the element takes focus when it appears, if nothing else has it.</summary>
    public bool Autofocus { get; private set; }

    /// <summary>Whether the element has a <c>tabindex</c> or is an enabled form control, so a click can focus it.</summary>
    public bool Focusable { get; private set; }

    /// <summary>Whether Tab reaches this element, which takes a <c>tabindex</c> of zero or more.</summary>
    public bool Tabbable { get; private set; }

    /// <summary>The id of the <c>ElementReference</c> a component captured for this element.</summary>
    internal string? ReferenceId { get; set; }

    /// <summary>
    /// Where the terminal caret goes while this element is focused, relative
    /// to its content box: a form control's caret, or the <c>caret="col,row"</c>
    /// attribute.
    /// </summary>
    public (int Column, int Row)? Caret => _caret ?? Control?.CaretCell;

    private (int Column, int Row)? _caret;

    /// <summary>What a <c>canvas</c> element paints with, set through its <c>@ref</c>.</summary>
    public Action<CellBuffer, Rect>? Painter { get; set; }

    /// <summary>
    /// Whether the user can scroll this box with the wheel and keys, as
    /// <c>overflow: auto</c> or <c>scroll</c> allow. A <c>hidden</c> box can
    /// still be scrolled through <see cref="ScrollTop"/>.
    /// </summary>
    public bool IsScrollContainer => Node.Style.Overflow == Overflow.Scroll;

    /// <summary>The height of the content box in rows.</summary>
    public int ClientHeight => Node.Layout.Deflate(Node.Style.Inset).Height;

    /// <summary>The height of the scrolled content in rows.</summary>
    public int ScrollHeight => Node.ContentSize.Height;

    public int ScrollTopMax => Math.Max(0, ScrollHeight - ClientHeight);

    /// <summary>
    /// How many rows the content is scrolled up, clamped to the content.
    /// Setting it does not raise <c>scroll</c>; the app raises that once a frame.
    /// </summary>
    public int ScrollTop
    {
        get => Node.ScrollTop;
        set
        {
            var previous = Node.ScrollTop;
            Node.ScrollTop = Math.Clamp(value, 0, ScrollTopMax);
            AnchoredToEnd = Node.ScrollTop >= ScrollTopMax;
            // No component re-renders for a scroll, so it requests the frame itself.
            if (Node.ScrollTop != previous) RequestRepaint();
        }
    }

    /// <summary>Requests another frame. Only the root element has one, set by the renderer.</summary>
    internal Action? RepaintRequested { get; set; }

    private void RequestRepaint()
    {
        for (HostNode? node = this; node is not null; node = node.Parent)
        {
            if (node is HostElement { RepaintRequested: { } repaint })
            {
                repaint();
                return;
            }
        }
    }

    /// <summary>
    /// Whether the end was in view when this box was last scrolled or laid
    /// out. It starts true so that a box that starts full shows its end.
    /// </summary>
    internal bool AnchoredToEnd { get; set; } = true;

    /// <summary>The offset the last <c>scroll</c> event reported.</summary>
    internal int ReportedScrollTop { get; set; }

    /// <summary>
    /// For a query container, the media environment with its content box as
    /// the size, which its <c>@container</c> rules are evaluated against.
    /// Null until the element has been laid out.
    /// </summary>
    public MediaEnvironment? ContainerEnvironment { get; private set; }

    /// <summary>Records the container's laid-out content box, returning whether it changed.</summary>
    internal bool SetContainerSize(Size size)
    {
        if (ContainerEnvironment is { } known && known.Width == size.Width && known.Height == size.Height) return false;
        ContainerEnvironment = (_styles?.Media ?? MediaEnvironment.Default) with { Width = size.Width, Height = size.Height };
        return true;
    }

    public IReadOnlyDictionary<string, object?> Attributes => _attributes;

    /// <summary>Blazor event handler ids by attribute name, such as <c>onkeydown</c>.</summary>
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
        Rebuild(name);
    }

    internal void RemoveAttribute(string name)
    {
        if (_handlers.Remove(name)) return;
        if (_attributes.Remove(name)) Rebuild(name);
    }

    /// <summary>
    /// Resolves the style again, and the descendants' when an attribute a
    /// selector might test or an inherited property changed.
    /// </summary>
    internal void Restyle() => Rebuild(null);

    private void Rebuild(string? changed)
    {
        ReadAttributes();
        if (_deferred && Parent is null) return;

        var previous = Node.Style;
        var style = ResolveStyle(previous);
        Node.Style = style;

        var inheritedChanged = previous.Color != style.Color
            || previous.TextStyle != style.TextStyle
            || previous.WhiteSpace != style.WhiteSpace
            || previous.TextAlign != style.TextAlign
            || !ReferenceEquals(previous.CustomProperties, style.CustomProperties);
        var lookChanged = inheritedChanged || previous.Background != style.Background;
        if (lookChanged) _runs = null;

        if (previous.IsInline != style.IsInline)
        {
            Parent?.ClosestElement?.DescendantsChanged(structural: true);
        }
        else if (IsInline)
        {
            if (lookChanged || changed is not null) Parent?.ClosestElement?.DescendantsChanged(structural: false);
        }
        else if (lookChanged)
        {
            RestyleAnonymousText();
        }

        if (Node is ImageLayoutNode image && changed is "src" or "alt") image.Reload();
        if (Node is TextControlLayoutNode control) control.AttributesChanged(changed);

        var affectsDescendants = inheritedChanged || changed is not null and not "style";
        if (affectsDescendants) RestyleDescendants();
    }

    private Style ResolveStyle(Style previous)
    {
        var sheets = _styles?.Sheets ?? (IReadOnlyList<Stylesheet>)[];
        var style = StyleResolver.Resolve(this, sheets, _styles?.Focused, _styles?.Media);
        if (IsImage) style = WithImageSize(style);

        // Keeping the old map when the content is the same lets the style
        // compare equal, so nothing below restyles.
        var sameCustomProperties = !ReferenceEquals(previous.CustomProperties, style.CustomProperties)
            && HaveSameEntries(previous.CustomProperties, style.CustomProperties);
        return sameCustomProperties ? style with { CustomProperties = previous.CustomProperties } : style;
    }

    private static bool HaveSameEntries(IReadOnlyDictionary<string, string> a, IReadOnlyDictionary<string, string> b)
    {
        if (a.Count != b.Count) return false;
        foreach (var (key, value) in a)
        {
            if (!b.TryGetValue(key, out var other) || other != value) return false;
        }
        return true;
    }

    /// <summary>Applies an <c>img</c>'s <c>width</c> and <c>height</c> attributes where the cascade set no size.</summary>
    private Style WithImageSize(Style style)
    {
        if (style.Width.IsAuto && TryLengthAttribute("width", out var width)) style = style with { Width = width };
        if (style.Height.IsAuto && TryLengthAttribute("height", out var height)) style = style with { Height = height };
        return style;
    }

    private bool TryLengthAttribute(string name, out Length length)
    {
        length = Length.Auto;
        if (!_attributes.TryGetValue(name, out var value) || value is null) return false;
        try
        {
            length = StyleParser.Apply(Style.Default, "width", value).Width;
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    internal void RestyleDescendants()
    {
        foreach (var element in Descendants().OfType<HostElement>())
        {
            element.Rebuild(null);
        }
    }

    private void RestyleAnonymousText()
    {
        foreach (var anonymous in AnonymousTextChildren())
        {
            anonymous.Restyle();
        }
    }

    private IEnumerable<AnonymousTextNode> AnonymousTextChildren() =>
        _layoutChildren?.OfType<AnonymousTextNode>() ?? [];

    /// <summary>Reads the attributes that are not CSS into their properties.</summary>
    private void ReadAttributes()
    {
        Id = _attributes.TryGetValue("id", out var id) ? id?.ToString() : null;
        Classes = _attributes.TryGetValue("class", out var classes) ? ParseClasses(classes) : NoClasses;
        InlineStyle = _attributes.TryGetValue("style", out var style) && style is string { Length: > 0 } css ? css : null;
        var tabIndex = _attributes.TryGetValue("tabindex", out var tab) ? ParseTabIndex(tab) : null;
        // Enabled form controls are focusable and tabbable without a tabindex.
        var enabledControl = IsControl && _attributes.GetValueOrDefault("disabled") is null or false;
        Focusable = tabIndex is not null || enabledControl;
        Tabbable = tabIndex is >= 0 || (enabledControl && tabIndex is null);
        Autofocus = _attributes.GetValueOrDefault("autofocus") is not (null or false);
        _caret = _attributes.TryGetValue("caret", out var caret) ? ParseCaret(caret) : null;
    }

    private static int? ParseTabIndex(object? value) => value switch
    {
        int index => index,
        true => 0,
        false or null => null,
        string text => int.TryParse(text, out var index) ? index : 0,
        _ => 0,
    };

    private static IReadOnlySet<string> ParseClasses(object? value)
    {
        switch (value)
        {
            case null:
                return NoClasses;
            case string text:
                var names = text.Split((char[])[' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
                return names.Length == 0 ? NoClasses : new HashSet<string>(names, StringComparer.Ordinal);
            case IEnumerable<string> many:
                var set = new HashSet<string>(many.Where(c => !string.IsNullOrWhiteSpace(c)), StringComparer.Ordinal);
                return set.Count == 0 ? NoClasses : set;
            default:
                return ParseClasses(value.ToString());
        }
    }

    private static (int, int)? ParseCaret(object? value)
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
        if (Node is TextControlLayoutNode control)
        {
            control.ContentChanged();
        }
        if (structural)
        {
            _layoutChildren = null;
        }
        else
        {
            foreach (var anonymous in AnonymousTextChildren())
            {
                anonymous.ContentChanged();
            }
        }
        _runs = null;
        Node.InvalidateLayout();
        if (IsInline) Parent?.ClosestElement?.DescendantsChanged(structural);
    }

    /// <summary>
    /// Descendant boxes with containers flattened out. Text and inline
    /// elements between them are grouped into anonymous text leaves.
    /// </summary>
    internal IReadOnlyList<LayoutNode> LayoutChildren
    {
        get
        {
            if (_layoutChildren is not null) return _layoutChildren;
            var children = new List<LayoutNode>();
            if (!IsInline && !IsCanvas && !IsImage && !IsControl)
            {
                var inlineRun = new List<HostNode>();
                Collect(this, children, inlineRun);
                EndInlineRun(children, inlineRun);
            }
            foreach (var child in children) child.LayoutParent = Node;
            _layoutChildren = children;
            return children;
        }
    }

    private void Collect(HostNode node, List<LayoutNode> into, List<HostNode> inlineRun)
    {
        // Flex and grid items are blockified: each inline element becomes an
        // item of its own instead of joining the surrounding text.
        var blockify = Node.Style.Display is Display.Flex or Display.Grid;
        foreach (var child in node.Children)
        {
            switch (child)
            {
                case HostElement { Node.Style.Display: Display.None }:
                    break;
                case HostElement { IsInline: true } inline when blockify:
                    EndInlineRun(into, inlineRun);
                    into.Add(new AnonymousTextNode(this, [inline]));
                    break;
                case HostElement { IsInline: true } inline:
                    inlineRun.Add(inline);
                    break;
                case HostElement element:
                    EndInlineRun(into, inlineRun);
                    into.Add(element.Node);
                    break;
                case HostTextNode text:
                    inlineRun.Add(text);
                    break;
                case HostContainer container:
                    Collect(container, into, inlineRun);
                    break;
            }
        }
    }

    /// <summary>
    /// Turns the pending inline run into an anonymous text leaf, unless it is
    /// only the whitespace that formats the markup.
    /// </summary>
    private void EndInlineRun(List<LayoutNode> into, List<HostNode> inlineRun)
    {
        if (inlineRun.Count == 0) return;
        var keepsWhitespace = Node.Style.WhiteSpace is WhiteSpace.Pre or WhiteSpace.PreWrap;
        var hasContent = inlineRun.Any(part => part switch
        {
            HostElement => true,
            HostTextNode { Text: var text } => keepsWhitespace ? text.Length > 0 : !string.IsNullOrWhiteSpace(text),
            _ => false,
        });
        if (hasContent) into.Add(new AnonymousTextNode(this, [.. inlineRun]));
        inlineRun.Clear();
    }

    /// <summary>The styled runs of this element's content, whitespace processed by its <c>white-space</c>.</summary>
    public IReadOnlyList<TextRun> Runs
    {
        get
        {
            if (_runs is not null) return _runs;
            var runs = new List<TextRun>();
            var style = Node.Style;
            CollectRuns(Children, style.Color, Color.Default, style.TextStyle, runs);
            TextLayout.CollapseWhitespace(runs, style.WhiteSpace);
            _runs = runs;
            return runs;
        }
    }

    /// <summary>
    /// The raw runs of text nodes and inline elements, each inline element in
    /// its own look. Boxes inside the run are skipped; the box above arranges them.
    /// </summary>
    internal static void CollectRuns(IEnumerable<HostNode> nodes, Color fg, Color bg, TextStyle style, List<TextRun> into)
    {
        foreach (var child in nodes)
        {
            switch (child)
            {
                case HostTextNode text:
                    if (text.Text.Length > 0) into.Add(new TextRun(text.Text, fg, bg, style));
                    break;
                case HostElement { Node.Style.Display: Display.None }:
                    break;
                case HostElement { IsBreak: true }:
                    into.Add(TextRun.LineBreak(fg, bg, style));
                    break;
                case HostElement { IsInline: true } inline:
                    var inlineStyle = inline.Node.Style;
                    var inlineBg = inlineStyle.Background.Kind == ColorKind.Default ? bg : inlineStyle.Background;
                    CollectRuns(inline.Children, inlineStyle.Color, inlineBg, inlineStyle.TextStyle, into);
                    break;
                case HostContainer container:
                    CollectRuns(container.Children, fg, bg, style, into);
                    break;
            }
        }
    }

    public void Paint(CellBuffer buffer, Rect rect) => Painter?.Invoke(buffer, rect);

    /// <summary>The deepest box containing the point, or null. Inline runs are hit through their block.</summary>
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

    public static ElementLayoutNode For(HostElement element)
    {
        if (element.IsCanvas) return new CanvasLayoutNode(element);
        if (element.IsImage) return new ImageLayoutNode(element);
        if (element.IsControl) return new TextControlLayoutNode(element);
        return new ElementLayoutNode(element);
    }
}

/// <summary>
/// The anonymous text leaf CSS creates for text and inline elements directly
/// under a box. Its look is the box's inherited text properties.
/// </summary>
public sealed class AnonymousTextNode : LayoutNode, ITextContent
{
    private readonly List<HostNode> _parts;
    private List<TextRun>? _runs;

    internal AnonymousTextNode(HostElement owner, List<HostNode> parts)
    {
        Owner = owner;
        _parts = parts;
        Style = StyleFor(owner);
    }

    /// <summary>The box the text stands in.</summary>
    public HostElement Owner { get; }

    /// <summary>The text nodes and inline elements, in order.</summary>
    public IReadOnlyList<HostNode> Parts => _parts;

    public override IReadOnlyList<LayoutNode> Children => [];

    public override bool IsLeaf => true;

    public IReadOnlyList<TextRun> Runs
    {
        get
        {
            if (_runs is not null) return _runs;
            var runs = new List<TextRun>();
            var style = Owner.Node.Style;
            HostElement.CollectRuns(_parts, style.Color, Color.Default, style.TextStyle, runs);
            TextLayout.CollapseWhitespace(runs, style.WhiteSpace);
            _runs = runs;
            return runs;
        }
    }

    public override Size MeasureContent(int? availableWidth, int? availableHeight) =>
        TextLayout.Measure(Runs, availableWidth, Style.Wrap);

    public override int MinContentWidth() => TextLayout.MinContentWidth(Runs, Style.Wrap);

    internal void ContentChanged()
    {
        _runs = null;
        InvalidateLayout();
    }

    internal void Restyle()
    {
        _runs = null;
        Style = StyleFor(Owner);
        InvalidateLayout();
    }

    private static Style StyleFor(HostElement owner)
    {
        var style = owner.Node.Style;
        return Style.Default with
        {
            Display = Display.Block,
            Color = style.Color,
            TextStyle = style.TextStyle,
            WhiteSpace = style.WhiteSpace,
            TextOverflow = style.TextOverflow,
            TextAlign = style.TextAlign,
        };
    }
}

/// <summary>
/// An <c>img</c> element's node, sized from its pixels. The <c>src</c> is a
/// file path or a <c>data:</c> URI; <c>alt</c> shows when it cannot be decoded.
/// </summary>
public sealed class ImageLayoutNode : ElementLayoutNode, ICustomPaint
{
    private ImageData? _image;
    private string? _loadedSrc;
    private bool _loaded;

    public ImageLayoutNode(HostElement element) : base(element) { }

    public override bool IsLeaf => true;

    /// <summary>The decoded image, or null when there is no source or it did not decode.</summary>
    public ImageData? Image
    {
        get
        {
            var src = Element.Attributes.GetValueOrDefault("src")?.ToString();
            if (_loaded && src == _loadedSrc) return _image;
            _image = src is null ? null : ImageDecoder.Load(src);
            _loadedSrc = src;
            _loaded = true;
            return _image;
        }
    }

    private string Alt => Element.Attributes.GetValueOrDefault("alt")?.ToString() ?? "";

    internal void Reload()
    {
        _loaded = false;
        InvalidateLayout();
    }

    public override Size MeasureContent(int? availableWidth, int? availableHeight)
    {
        var image = Image;
        if (image is null) return TextLayout.Measure([new TextRun(Alt)], availableWidth, TextWrap.Wrap);

        var fixedWidth = Style.Width.IsAuto ? null : availableWidth;
        var fixedHeight = Style.Height.IsAuto ? null : availableHeight;
        var cellWidth = Element.Graphics?.CellPixelWidth ?? ImagePainter.CellPixelWidth;
        var cellHeight = Element.Graphics?.CellPixelHeight ?? ImagePainter.CellPixelHeight;
        return ImagePainter.Fit(image, fixedWidth, fixedHeight, availableWidth, cellWidth, cellHeight);
    }

    public override int MinContentWidth() =>
        Image is null ? TextLayout.MinContentWidth([new TextRun(Alt)], TextWrap.Wrap) : 1;

    public void Paint(CellBuffer buffer, Rect rect)
    {
        var image = Image;
        if (image is null)
        {
            buffer.PutText(rect.X, rect.Y, Alt, Style.Color, Style.Background, Style.TextStyle);
            return;
        }
        ImagePainter.Paint(buffer, rect, image, Element.Graphics);
    }
}

/// <summary>A <c>canvas</c> element's node, painted by the element's <see cref="HostElement.Painter"/>.</summary>
public sealed class CanvasLayoutNode : ElementLayoutNode, ICustomPaint
{
    public CanvasLayoutNode(HostElement element) : base(element) { }

    public override bool IsLeaf => true;

    public void Paint(CellBuffer buffer, Rect rect) => Element.Paint(buffer, rect);
}
