using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.Extensions.Logging;
using SlopTui.Layout;
using SlopTui.Styling;

namespace SlopTui.Components;

/// <summary>
/// A Blazor renderer that applies render batches to a <see cref="HostElement"/>
/// tree, following <c>BrowserRenderer.ts</c>. It only marks the tree dirty;
/// the app loop paints.
/// </summary>
public sealed class TerminalRenderer : Renderer
{
    private readonly Dictionary<int, HostContainer> _containers = [];
    private readonly Dictionary<ulong, HostElement> _handlerOwners = [];
    private readonly Action<Exception> _onException;
    private readonly CanvasRegistry? _canvases;
    private readonly StyleContext? _styles;

    public TerminalRenderer(IServiceProvider services, ILoggerFactory loggerFactory, TerminalDispatcher dispatcher, Action<Exception> onException, StyleContext? styles = null)
        : base(services, loggerFactory)
    {
        Dispatcher = dispatcher;
        _onException = onException;
        _canvases = services.GetService(typeof(CanvasRegistry)) as CanvasRegistry;
        _styles = styles;
        Root = new HostElement("box", _canvases, _styles);
        Root.SetAttribute("flex-direction", "column", 0);
    }

    public StyleContext? Styles => _styles;

    /// <summary>Resolves every element's style again.</summary>
    public void RestyleAll()
    {
        Root.Restyle();
        Root.RestyleDescendants();
        Dirty = true;
    }

    /// <summary>
    /// Restyles what <c>:focus</c> and <c>:focus-within</c> can have changed:
    /// both elements, their ancestors, and whatever lies below them.
    /// </summary>
    public void FocusChanged(HostElement? previous, HostElement? current)
    {
        if (_styles is null || !_styles.DependsOnFocus) return;
        foreach (var element in new[] { previous, current })
        {
            if (element is not null) RestyleFocusPath(element);
        }
        Dirty = true;
    }

    private void RestyleFocusPath(HostElement element)
    {
        // Outermost first, so inherited properties are resolved top down.
        var path = element.AncestorElements().Reverse().Append(element).ToList();
        foreach (var node in path)
        {
            node.Restyle();
        }

        var subtree = _styles!.FocusAffectsDescendants ? path[0] : element;
        subtree.RestyleDescendants();
    }

    public override Dispatcher Dispatcher { get; }

    /// <summary>The box every root component renders into.</summary>
    public HostElement Root { get; }

    /// <summary>Set by each applied batch and cleared by the app when it paints.</summary>
    public bool Dirty { get; set; }

    /// <summary>Raised on the dispatcher thread after a batch is applied.</summary>
    public event Action? BatchApplied;

    public void SetViewport(Size size)
    {
        Root.SetAttribute("width", size.Width, 0);
        Root.SetAttribute("height", size.Height, 0);
    }

    public Task AddRootComponentAsync(Type componentType, ParameterView parameters)
    {
        var component = InstantiateComponent(componentType);
        var componentId = AssignRootComponentId(component);
        var container = new HostContainer { ComponentId = componentId };
        Root.InsertChild(Root.Children.Count, container);
        _containers[componentId] = container;
        return RenderRootComponentAsync(componentId, parameters);
    }

    /// <summary>The element that owns an event handler, or null once the handler is gone.</summary>
    public HostElement? OwnerOf(ulong eventHandlerId) => _handlerOwners.GetValueOrDefault(eventHandlerId);

    /// <summary>Raises the element's handler for an event, returning false when it has none.</summary>
    public async Task<bool> RaiseAsync(HostElement element, string eventName, EventArgs args)
    {
        if (element.HandlerFor(eventName) is not { } id) return false;
        await DispatchEventAsync(id, null, args);
        return true;
    }

    protected override void HandleException(Exception exception) => _onException(exception);

    protected override Task UpdateDisplayAsync(in RenderBatch batch)
    {
        var frames = batch.ReferenceFrames;
        var updated = batch.UpdatedComponents;
        for (var i = 0; i < updated.Count; i++)
        {
            var diff = updated.Array[i];
            if (!_containers.TryGetValue(diff.ComponentId, out var container))
            {
                throw new InvalidOperationException($"Render batch for component {diff.ComponentId}, which has no container.");
            }
            ApplyEdits(container, 0, diff.Edits, frames);
        }

        var disposedHandlers = batch.DisposedEventHandlerIDs;
        for (var i = 0; i < disposedHandlers.Count; i++)
        {
            _handlerOwners.Remove(disposedHandlers.Array[i]);
        }

        var disposedComponents = batch.DisposedComponentIDs;
        for (var i = 0; i < disposedComponents.Count; i++)
        {
            _containers.Remove(disposedComponents.Array[i]);
        }

        Dirty = true;
        BatchApplied?.Invoke();
        return Task.CompletedTask;
    }

    private void ApplyEdits(HostNode parent, int childIndex, ArrayBuilderSegment<RenderTreeEdit> edits, ArrayRange<RenderTreeFrame> frames)
    {
        var depth = 0;
        var childIndexAtDepth = childIndex;
        List<(int From, int To)>? permutation = null;
        var array = edits.Array;

        for (var i = edits.Offset; i < edits.Offset + edits.Count; i++)
        {
            var edit = array[i];
            switch (edit.Type)
            {
                case RenderTreeEditType.PrependFrame:
                    InsertFrame(parent, childIndexAtDepth + edit.SiblingIndex, frames, edit.ReferenceFrameIndex);
                    break;

                case RenderTreeEditType.RemoveFrame:
                    RemoveChild(parent, childIndexAtDepth + edit.SiblingIndex);
                    break;

                case RenderTreeEditType.SetAttribute:
                {
                    var frame = frames.Array[edit.ReferenceFrameIndex];
                    var element = ElementAt(parent, childIndexAtDepth + edit.SiblingIndex);
                    ApplyAttribute(element, frame);
                    break;
                }

                case RenderTreeEditType.RemoveAttribute:
                {
                    var element = ElementAt(parent, childIndexAtDepth + edit.SiblingIndex);
                    element.RemoveAttribute(edit.RemovedAttributeName!);
                    break;
                }

                case RenderTreeEditType.UpdateText:
                {
                    var frame = frames.Array[edit.ReferenceFrameIndex];
                    if (parent.Children[childIndexAtDepth + edit.SiblingIndex] is not HostTextNode text)
                    {
                        throw new InvalidOperationException("Cannot set text content on a non-text child.");
                    }
                    text.Text = frame.TextContent;
                    break;
                }

                case RenderTreeEditType.UpdateMarkup:
                {
                    var frame = frames.Array[edit.ReferenceFrameIndex];
                    var index = childIndexAtDepth + edit.SiblingIndex;
                    RemoveChild(parent, index);
                    parent.InsertChild(index, Markup(frame.MarkupContent));
                    break;
                }

                case RenderTreeEditType.StepIn:
                    parent = parent.Children[childIndexAtDepth + edit.SiblingIndex];
                    depth++;
                    childIndexAtDepth = 0;
                    break;

                case RenderTreeEditType.StepOut:
                    parent = parent.Parent ?? throw new InvalidOperationException("StepOut above the root.");
                    depth--;
                    childIndexAtDepth = depth == 0 ? childIndex : 0;
                    break;

                case RenderTreeEditType.PermutationListEntry:
                    permutation ??= [];
                    permutation.Add((childIndexAtDepth + edit.SiblingIndex, childIndexAtDepth + edit.MoveToSiblingIndex));
                    break;

                case RenderTreeEditType.PermutationListEnd:
                    parent.Permute(permutation ?? []);
                    permutation = null;
                    break;

                default:
                    throw new InvalidOperationException($"Unknown edit type {edit.Type}.");
            }
        }
    }

    private static HostElement ElementAt(HostNode parent, int index) =>
        parent.Children[index] as HostElement ?? throw new InvalidOperationException("Expected an element child.");

    private void RemoveChild(HostNode parent, int index)
    {
        var removed = parent.RemoveChildAt(index);
        foreach (var node in removed.Descendants().Prepend(removed))
        {
            if (node is HostContainer { ComponentId: { } id })
            {
                _containers.Remove(id);
            }
            else if (node is HostElement element)
            {
                foreach (var handler in element.Handlers.Values) _handlerOwners.Remove(handler);
            }
        }
    }

    /// <summary>Inserts a frame as a child and returns how many children it made.</summary>
    private int InsertFrame(HostNode parent, int childIndex, ArrayRange<RenderTreeFrame> frames, int frameIndex)
    {
        var frame = frames.Array[frameIndex];
        switch (frame.FrameType)
        {
            case RenderTreeFrameType.Element:
                InsertElement(parent, childIndex, frames, frameIndex);
                return 1;

            case RenderTreeFrameType.Text:
                parent.InsertChild(childIndex, new HostTextNode { Text = frame.TextContent });
                return 1;

            case RenderTreeFrameType.Markup:
                parent.InsertChild(childIndex, Markup(frame.MarkupContent));
                return 1;

            case RenderTreeFrameType.Component:
            {
                var container = new HostContainer { ComponentId = frame.ComponentId };
                parent.InsertChild(childIndex, container);
                _containers[frame.ComponentId] = container;
                return 1;
            }

            case RenderTreeFrameType.Region:
                return InsertFrameRange(parent, childIndex, frames, frameIndex + 1, frameIndex + frame.RegionSubtreeLength);

            case RenderTreeFrameType.Attribute:
                throw new InvalidOperationException("Attribute frames belong to an element.");

            case RenderTreeFrameType.ElementReferenceCapture:
            case RenderTreeFrameType.ComponentReferenceCapture:
            case RenderTreeFrameType.NamedEvent:
            case RenderTreeFrameType.ComponentRenderMode:
            case RenderTreeFrameType.None:
            default:
                return 0;
        }
    }

    private int InsertFrameRange(HostNode parent, int childIndex, ArrayRange<RenderTreeFrame> frames, int start, int endExclusive)
    {
        var origin = childIndex;
        for (var index = start; index < endExclusive; index++)
        {
            childIndex += InsertFrame(parent, childIndex, frames, index);
            index += SubtreeLength(frames.Array[index]) - 1;
        }
        return childIndex - origin;
    }

    private static int SubtreeLength(in RenderTreeFrame frame) => frame.FrameType switch
    {
        RenderTreeFrameType.Element => frame.ElementSubtreeLength,
        RenderTreeFrameType.Component => frame.ComponentSubtreeLength,
        RenderTreeFrameType.Region => frame.RegionSubtreeLength,
        _ => 1,
    };

    /// <summary>
    /// Parses a markup frame into a container, since the frame counts as one
    /// child but can hold several nodes.
    /// </summary>
    private HostContainer Markup(string markup)
    {
        var container = new HostContainer();
        var nodes = MarkupParser.Parse(markup, name => new HostElement(name, _canvases, _styles));
        for (var i = 0; i < nodes.Count; i++)
        {
            container.InsertChild(i, nodes[i]);
        }
        return container;
    }

    private void InsertElement(HostNode parent, int childIndex, ArrayRange<RenderTreeFrame> frames, int frameIndex)
    {
        var frame = frames.Array[frameIndex];
        var element = new HostElement(frame.ElementName, _canvases, _styles);
        var end = frameIndex + frame.ElementSubtreeLength;
        var descendant = frameIndex + 1;
        for (; descendant < end; descendant++)
        {
            var child = frames.Array[descendant];
            if (child.FrameType != RenderTreeFrameType.Attribute) break;
            ApplyAttribute(element, child);
        }
        parent.InsertChild(childIndex, element);
        if (descendant < end) InsertFrameRange(element, 0, frames, descendant, end);
    }

    private void ApplyAttribute(HostElement element, in RenderTreeFrame frame)
    {
        if (frame.AttributeEventHandlerId != 0) _handlerOwners[frame.AttributeEventHandlerId] = element;
        element.SetAttribute(frame.AttributeName, frame.AttributeValue, frame.AttributeEventHandlerId);
    }
}
