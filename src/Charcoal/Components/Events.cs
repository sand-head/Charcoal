using Microsoft.AspNetCore.Components;
using Charcoal.Input;

namespace Charcoal.Components;

/// <summary>A key press, delivered to the focused element and then its ancestors.</summary>
/// <remarks>
/// There is no <c>keyup</c>, because terminals only report keys going down
/// unless the kitty protocol's release mode is on.
/// </remarks>
public sealed class KeyboardEventArgs : EventArgs
{
    public KeyboardEventArgs(KeyEvent key) => Key = key;

    public KeyEvent Key { get; }

    /// <summary>Stops the key bubbling further and suppresses the default action.</summary>
    public bool Handled { get; set; }
}

/// <summary>A mouse action over an element.</summary>
public class MouseEventArgs : EventArgs
{
    public MouseEventArgs(MouseEvent mouse, HostElement target)
    {
        Mouse = mouse;
        Target = target;
    }

    public MouseEvent Mouse { get; }

    /// <summary>The deepest element under the pointer.</summary>
    public HostElement Target { get; }

    public int LocalX => Mouse.X - Target.Node.Layout.X;
    public int LocalY => Mouse.Y - Target.Node.Layout.Y;

    public MouseButton Button => Mouse.Button;

    public bool Handled { get; set; }
}

/// <summary>A turn of the mouse wheel over an element.</summary>
public sealed class WheelEventArgs : MouseEventArgs
{
    public WheelEventArgs(MouseEvent mouse, HostElement target) : base(mouse, target) { }

    /// <summary>The rows to scroll: negative up, positive down.</summary>
    public int DeltaY => Mouse.Action == MouseAction.WheelUp ? -1 : 1;
}

/// <summary>An element gained focus (<c>focus</c>) or lost it (<c>blur</c>).</summary>
public sealed class FocusEventArgs : EventArgs
{
}

/// <summary>A scroll container's offset changed.</summary>
public sealed class ScrollEventArgs : EventArgs
{
    public ScrollEventArgs(HostElement target) => Target = target;

    public HostElement Target { get; }

    /// <summary>The new offset in rows.</summary>
    public int ScrollTop => Target.ScrollTop;
}

/// <summary>A paste arrived while an element was focused.</summary>
public sealed class PasteEventArgs : EventArgs
{
    public PasteEventArgs(string text) => Text = text;

    public string Text { get; }

    public bool Handled { get; set; }
}

/// <summary>Declares the element events and their argument types to the Razor compiler.</summary>
[EventHandler("onkeydown", typeof(KeyboardEventArgs), true, true)]
[EventHandler("onclick", typeof(MouseEventArgs), true, true)]
[EventHandler("onmousedown", typeof(MouseEventArgs), true, true)]
[EventHandler("onmouseup", typeof(MouseEventArgs), true, true)]
[EventHandler("onmousemove", typeof(MouseEventArgs), true, true)]
[EventHandler("onwheel", typeof(WheelEventArgs), true, true)]
[EventHandler("onscroll", typeof(ScrollEventArgs), true, true)]
[EventHandler("onfocus", typeof(FocusEventArgs), true, true)]
[EventHandler("onblur", typeof(FocusEventArgs), true, true)]
[EventHandler("onpaste", typeof(PasteEventArgs), true, true)]
[EventHandler("oninput", typeof(ChangeEventArgs), true, true)]
[EventHandler("onchange", typeof(ChangeEventArgs), true, true)]
public static class EventHandlers
{
}

/// <summary>
/// <c>@bind</c> on form controls, as <c>Microsoft.AspNetCore.Components.Web</c>
/// declares it for the DOM: <c>value</c> carries the text in and
/// <c>onchange</c> carries it out.
/// </summary>
[BindElement("input", null, "value", "onchange")]
[BindElement("textarea", null, "value", "onchange")]
public static class BindAttributes
{
}
