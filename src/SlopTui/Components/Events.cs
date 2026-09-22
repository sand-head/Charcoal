using Microsoft.AspNetCore.Components;
using SlopTui.Input;

namespace SlopTui.Components;

/// <summary>A key press, delivered to the focused element and then its ancestors.</summary>
public sealed class KeyPressEventArgs : EventArgs
{
    public KeyPressEventArgs(KeyEvent key) => Key = key;

    public KeyEvent Key { get; }

    /// <summary>Stops the key bubbling further and suppresses the default action.</summary>
    public bool Handled { get; set; }
}

/// <summary>A mouse action over an element.</summary>
public sealed class MouseEventArgs : EventArgs
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

    public bool Handled { get; set; }
}

/// <summary>An element gained or lost focus.</summary>
public sealed class FocusEventArgs : EventArgs
{
    public FocusEventArgs(bool gained) => Gained = gained;

    public bool Gained { get; }
}

/// <summary>A paste arrived while an element was focused.</summary>
public sealed class PasteEventArgs : EventArgs
{
    public PasteEventArgs(string text) => Text = text;

    public string Text { get; }

    public bool Handled { get; set; }
}

/// <summary>Declares the element events and their argument types to the Razor compiler.</summary>
[EventHandler("onkeypress", typeof(KeyPressEventArgs), true, true)]
[EventHandler("onclick", typeof(MouseEventArgs), true, true)]
[EventHandler("onmouse", typeof(MouseEventArgs), true, true)]
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
