namespace SlopTui.Components;

/// <summary>
/// Tracks which element has the keyboard. Elements with <c>focusable="true"</c>
/// take part, in tree order. Focus is dropped when its element leaves the tree.
/// </summary>
public sealed class FocusManager
{
    private readonly HostElement _root;
    private readonly Func<HostElement, bool, Task> _notify;

    internal FocusManager(HostElement root, Func<HostElement, bool, Task> notify)
    {
        _root = root;
        _notify = notify;
    }

    public HostElement? Focused { get; private set; }

    /// <summary>Raised after focus moves, with the previous and the new element.</summary>
    public event Action<HostElement?, HostElement?>? Changed;

    /// <summary>The focusable elements in tree order.</summary>
    public List<HostElement> Order() =>
        _root.Descendants().OfType<HostElement>().Where(e => e.Focusable && IsShown(e)).ToList();

    private static bool IsShown(HostElement element) =>
        element.Node.Style.Display != Layout.Display.None
        && element.AncestorElements().All(a => a.Node.Style.Display != Layout.Display.None);

    /// <summary>Moves focus to an element, or clears it, raising blur and then focus.</summary>
    public async Task FocusAsync(HostElement? element)
    {
        if (ReferenceEquals(Focused, element)) return;
        var previous = Focused;
        Focused = element;
        if (previous is not null) await _notify(previous, false);
        if (element is not null) await _notify(element, true);
        Changed?.Invoke(previous, element);
    }

    /// <summary>Focuses the next focusable element, wrapping around.</summary>
    public Task NextAsync() => StepAsync(1);

    /// <summary>Focuses the previous focusable element, wrapping around.</summary>
    public Task PreviousAsync() => StepAsync(-1);

    private Task StepAsync(int direction)
    {
        var order = Order();
        if (order.Count == 0) return FocusAsync(null);
        var current = Focused is null ? -1 : order.IndexOf(Focused);
        if (current < 0)
        {
            var edge = direction > 0 ? 0 : order.Count - 1;
            return FocusAsync(order[edge]);
        }

        var next = (current + direction + order.Count) % order.Count;
        return FocusAsync(order[next]);
    }

    /// <summary>Drops focus, without a blur event, when the focused element has left the tree.</summary>
    internal void DropIfDetached()
    {
        if (Focused is null) return;
        for (HostNode? node = Focused; node is not null; node = node.Parent)
        {
            if (ReferenceEquals(node, _root)) return;
        }
        var previous = Focused;
        Focused = null;
        Changed?.Invoke(previous, null);
    }
}
