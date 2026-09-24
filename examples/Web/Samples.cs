using Charcoal.Components;

namespace Web;

/// <summary>An example project, as the page starts it.</summary>
public sealed record Sample(string Name, string Summary, Type Root, Action<TuiApp>? Configure = null);

public static class Samples
{
    public static readonly Sample[] All =
    [
        new("Hello", "HTML tags, a scoped stylesheet and a picture. q restarts it.", typeof(Hello.App)),
        new("Layouts", "Flex, wrap, grid, minimum sizes and scrolling. ← → turn the page.", typeof(Layouts.App)),
        new("Styled", "Cards styled by one global sheet. Tab moves focus.", typeof(Styled.App), app => app.AddStylesheet(Styled.Sheet.Css)),
        new("Counter", "State, a timer, keys and clicks. Tab, then + and −.", typeof(Counter.App)),
        new("Picker", "A list for the keyboard or the mouse. ↑ ↓ and Enter, or click.", typeof(Picker.App)),
        new("Form", "input and textarea with @bind. Type, and Tab between fields.", typeof(Form.App)),
        new("Routing", "Blazor's Router with a route parameter. Tab and Enter follow a link.", typeof(Routing.App)),
        new("Transcript", "Two thousand lines, streaming, with a composer. F1 pauses.", typeof(Transcript.App)),
    ];
}
