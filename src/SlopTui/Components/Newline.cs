using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace SlopTui.Components;

/// <summary>
/// A line break inside a <see cref="Text"/>. Line breaks in markup are
/// formatting, so this is an element instead.
/// </summary>
public sealed class Newline : ComponentBase
{
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "text");
        builder.AddAttribute(1, "newline", true);
        builder.CloseElement();
    }
}
