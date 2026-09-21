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
        // Selectors with combinators and inherited colours need the ancestors.
        if (child is HostElement element) element.Restyle();
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
/// element is a leaf whose nested <c>text</c> elements are styled runs. HTML
/// inline tags such as <c>strong</c> and <c>br</c> are text elements with a
/// preset style, and any other name is a box.
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
    private readonly StyleContext? _styles;
    private static readonly IReadOnlySet<string> NoClasses = new HashSet<string>();

    /// <summary>
    /// The text style each HTML inline tag starts from. Stylesheets and
    /// attributes apply on top, as over a browser's default stylesheet.
    /// </summary>
    private static readonly Dictionary<string, TextStyle> HtmlInline = new(StringComparer.Ordinal)
    {
        ["strong"] = TextStyle.Bold,
        ["b"] = TextStyle.Bold,
        ["em"] = TextStyle.Italic,
        ["i"] = TextStyle.Italic,
        ["u"] = TextStyle.Underline,
        ["s"] = TextStyle.Strikethrough,
        ["del"] = TextStyle.Strikethrough,
        ["strike"] = TextStyle.Strikethrough,
        ["mark"] = TextStyle.Inverse,
        ["span"] = TextStyle.None,
        ["br"] = TextStyle.None,
    };

    private static readonly string[] BlockTags =
        ["div", "p", "section", "article", "main", "header", "footer", "nav", "aside", "ul", "ol", "li", "pre", "blockquote"];

    private static readonly string[] HeadingTags = ["h1", "h2", "h3", "h4", "h5", "h6"];

    /// <summary>HTML block tags are boxes whose children stack, where a <c>box</c> is a row. Headings are bold.</summary>
    private static readonly Dictionary<string, Style> HtmlBlock = BlockStyles();

    private static Dictionary<string, Style> BlockStyles()
    {
        var column = Style.Default with { FlexDirection = FlexDirection.Column };
        var heading = column with { TextStyle = TextStyle.Bold };
        var styles = new Dictionary<string, Style>(StringComparer.Ordinal);
        foreach (var tag in BlockTags) styles[tag] = column;
        foreach (var tag in HeadingTags) styles[tag] = heading;
        return styles;
    }

    public HostElement(string name, CanvasRegistry? canvases = null, StyleContext? styles = null)
    {
        Name = name;
        IsText = name == "text" || HtmlInline.ContainsKey(name);
        IsInline = IsText && name != "text";
        IsBreak = name == "br";
        IsImage = name == "img";
        BaseStyle = PresetStyle(name);
        Node = ElementLayoutNode.For(this);
        _canvases = canvases;
        _styles = styles;
        if (_styles is not null && _styles.Sheets.Count > 0) Rebuild(null);
    }

    private static Style PresetStyle(string name)
    {
        if (HtmlInline.TryGetValue(name, out var flags) && flags != TextStyle.None) return Style.Default with { TextStyle = flags };
        if (HtmlBlock.TryGetValue(name, out var block)) return block;
        return Style.Default;
    }

    /// <summary>The classes in the <c>class</c> attribute.</summary>
    public IReadOnlySet<string> Classes { get; private set; } = NoClasses;

    public string? Id { get; private set; }

    /// <summary>The element name as written, which type selectors match.</summary>
    public string Name { get; }

    public ElementLayoutNode Node { get; }

    /// <summary>Whether this is <c>text</c> or an HTML inline tag.</summary>
    public bool IsText { get; }

    /// <summary>
    /// Whether this is an HTML inline tag such as <c>strong</c> or <c>span</c>.
    /// It is always a styled run: under a box it joins the text beside it in
    /// an anonymous text leaf.
    /// </summary>
    public bool IsInline { get; }

    /// <summary>Whether this is a <c>&lt;br&gt;</c>.</summary>
    public bool IsBreak { get; }

    public bool IsImage { get; }

    /// <summary>The style the cascade starts from: the tag's preset, if it has one.</summary>
    internal Style BaseStyle { get; }

    public bool IsCanvas => Name == "canvas";

    /// <summary>
    /// Whether this text element is a run within something else: an inline
    /// tag, or a <c>text</c> nested in another.
    /// </summary>
    public bool IsInlineText => IsText && (IsInline || Parent?.ClosestElement is { IsText: true });

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
        Rebuild(name);
    }

    internal void RemoveAttribute(string name)
    {
        if (_handlers.Remove(name)) return;
        if (_attributes.Remove(name)) Rebuild(name);
    }

    /// <summary>
    /// Resolves the style again, and the descendants' too when a class, an id
    /// or an inherited property changed.
    /// </summary>
    internal void Restyle() => Rebuild(null);

    private void Rebuild(string? changed)
    {
        var previous = Node.Style;
        ReadIdentity();
        Node.Style = _styles is null
            ? StyleResolver.Inherit(this, ApplyOwnAttributes(BaseStyle))
            : StyleResolver.Resolve(this, _styles.Sheets, _styles.Focused);

        var lookChanged = previous.Color != Node.Style.Color
            || previous.Background != Node.Style.Background
            || previous.TextStyle != Node.Style.TextStyle;

        // Text runs carry their colours and flags, so a new look invalidates them.
        if (IsInlineText)
        {
            Parent?.ClosestElement?.DescendantsChanged(structural: false);
        }
        else if (IsText && lookChanged)
        {
            DescendantsChanged(structural: false);
        }
        else if (!IsText)
        {
            // Anonymous text takes its look from this box and its ancestors,
            // and a restyle may have come from any of them.
            RestyleAnonymousText();
        }

        if (Node is ImageLayoutNode image && changed is "src" or "alt") image.Reload();

        var affectsDescendants = changed is "class" or "id"
            || previous.Color != Node.Style.Color
            || previous.TextStyle != Node.Style.TextStyle;
        if (affectsDescendants) RestyleDescendants();
    }

    internal void RestyleDescendants()
    {
        foreach (var element in Descendants().OfType<HostElement>())
        {
            element.Rebuild(null);
        }
    }

    private void ReadIdentity()
    {
        Id = _attributes.TryGetValue("id", out var id) ? id?.ToString() : null;
        Classes = _attributes.TryGetValue("class", out var classes) ? ParseClasses(classes) : NoClasses;
    }

    /// <summary>
    /// Applies this element's attributes over <paramref name="style"/>, the
    /// inline <c>style</c> last, and reads <c>focusable</c> and <c>cursor</c>.
    /// </summary>
    internal Style ApplyOwnAttributes(Style style)
    {
        Focusable = false;
        Cursor = null;
        string? inline = null;
        foreach (var (name, value) in _attributes)
        {
            switch (name)
            {
                case "style" when value is string css:
                    inline = css;
                    break;
                case "focusable":
                    Focusable = IsTrue(value);
                    break;
                case "cursor":
                    Cursor = ParseCursor(value);
                    break;
                case "class":
                case "id":
                case "paint":
                case "key":
                    break;
                default:
                    if (StyleParser.IsStyleAttribute(name)) style = StyleParser.Apply(style, name, value);
                    break;
            }
        }
        if (inline is not null) style = StyleParser.ApplyInline(style, inline);
        return style;
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

    /// <summary>
    /// The colour and flags a text directly under this element would inherit:
    /// the nearest colour set here or above, and every flag.
    /// </summary>
    internal (Color Color, TextStyle Flags) InheritedText()
    {
        var color = Color.Default;
        var flags = TextStyle.None;
        for (var element = this; element is not null; element = element.Parent?.ClosestElement)
        {
            var style = element.Node.Style;
            if (color.Kind == ColorKind.Default && style.Color.Kind != ColorKind.Default)
            {
                color = style.Color;
            }
            flags |= style.TextStyle;
        }
        return (color, flags);
    }

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
        if (IsInlineText) Parent?.ClosestElement?.DescendantsChanged(structural);
    }

    /// <summary>
    /// Descendant elements with containers flattened out. Bare text and inline
    /// tags between them are grouped into anonymous text leaves, as CSS wraps
    /// the text in a block in anonymous boxes.
    /// </summary>
    internal IReadOnlyList<LayoutNode> LayoutChildren
    {
        get
        {
            if (_layoutChildren is not null) return _layoutChildren;
            var children = new List<LayoutNode>();
            if (!IsText && !IsCanvas && !IsImage)
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
        foreach (var child in node.Children)
        {
            switch (child)
            {
                case HostElement { IsInline: true } inline:
                    inlineRun.Add(inline);
                    break;
                case HostElement element:
                    EndInlineRun(into, inlineRun);
                    into.Add(element.Node);
                    break;
                case HostTextNode text:
                    if (text.Text.Length > 0 && !IsMarkupWhitespace(text.Text)) inlineRun.Add(text);
                    break;
                case HostContainer container:
                    Collect(container, into, inlineRun);
                    break;
            }
        }
    }

    /// <summary>Turns the pending inline run into an anonymous text leaf, unless it is only spaces.</summary>
    private void EndInlineRun(List<LayoutNode> into, List<HostNode> inlineRun)
    {
        if (inlineRun.Count == 0) return;
        var hasContent = inlineRun.Any(part => part is HostElement || part is HostTextNode { Text: var text } && !string.IsNullOrWhiteSpace(text));
        if (hasContent) into.Add(new AnonymousTextNode(this, [.. inlineRun]));
        inlineRun.Clear();
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

    private static void CollectRuns(HostNode node, Color fg, Color bg, TextStyle style, List<TextRun> into) =>
        CollectRuns(node.Children, fg, bg, style, into);

    /// <summary>The runs of text nodes and inline elements, each inline element in its own look.</summary>
    internal static void CollectRuns(IEnumerable<HostNode> nodes, Color fg, Color bg, TextStyle style, List<TextRun> into)
    {
        foreach (var child in nodes)
        {
            switch (child)
            {
                case HostTextNode text:
                    if (text.Text.Length > 0 && !IsMarkupWhitespace(text.Text))
                    {
                        into.Add(new TextRun(text.Text, fg, bg, style));
                    }
                    break;
                case HostElement { IsText: true } inline when inline.IsBreak || inline.Attributes.ContainsKey("newline"):
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

    public static ElementLayoutNode For(HostElement element)
    {
        if (element.IsText) return new TextLayoutNode(element);
        if (element.IsCanvas) return new CanvasLayoutNode(element);
        if (element.IsImage) return new ImageLayoutNode(element);
        return new ElementLayoutNode(element);
    }
}

/// <summary>A <c>text</c> element's node, measured by wrapping its runs.</summary>
public sealed class TextLayoutNode : ElementLayoutNode, ITextContent
{
    public TextLayoutNode(HostElement element) : base(element) { }

    public override bool IsLeaf => true;

    public IReadOnlyList<TextRun> Runs => Element.Runs;

    public override Size MeasureContent(int? availableWidth, int? availableHeight) =>
        TextLayout.Measure(Element.Runs, availableWidth, Style.Wrap);

    public override int MinContentWidth() => TextLayout.MinContentWidth(Element.Runs, Style.Wrap);
}

/// <summary>
/// The anonymous text leaf CSS creates for bare text and inline tags directly
/// under a box. It inherits its look from the box, as a text child would.
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
            HostElement.CollectRuns(_parts, Color.Default, Color.Default, TextStyle.None, runs);
            TrimEdges(runs);
            _runs = runs;
            return runs;
        }
    }

    /// <summary>
    /// Drops the spaces at the edges, as a browser drops the whitespace where
    /// a block's text meets its blocks. A <c>text</c> element keeps its spaces.
    /// </summary>
    private static void TrimEdges(List<TextRun> runs)
    {
        while (runs.Count > 0)
        {
            var trimmed = runs[0].Text.TrimStart(' ');
            if (trimmed.Length > 0)
            {
                runs[0] = runs[0] with { Text = trimmed };
                break;
            }
            runs.RemoveAt(0);
        }
        while (runs.Count > 0)
        {
            var trimmed = runs[^1].Text.TrimEnd(' ');
            if (trimmed.Length > 0)
            {
                runs[^1] = runs[^1] with { Text = trimmed };
                break;
            }
            runs.RemoveAt(runs.Count - 1);
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

    internal void Restyle() => Style = StyleFor(Owner);

    private static Style StyleFor(HostElement owner)
    {
        var (color, flags) = owner.InheritedText();
        return Style.Default with { Color = color, TextStyle = flags, Wrap = owner.Node.Style.Wrap };
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
        return ImagePainter.Fit(image, fixedWidth, fixedHeight, availableWidth);
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
        ImagePainter.Paint(buffer, rect, image);
    }
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
