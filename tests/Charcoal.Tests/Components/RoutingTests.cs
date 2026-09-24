using Microsoft.AspNetCore.Components;
using Charcoal.Components;
using Charcoal.Layout;
using Charcoal.Rendering;
using Charcoal.Routing;
using Charcoal.Terminal;
using Charcoal.Tests.Components.Fixtures;

namespace Charcoal.Tests.Components;

public class RoutingTests : IAsyncDisposable
{
    private readonly HeadlessTerminal _terminal = new(60, 12);
    private readonly TuiApp _app;
    private readonly Task _run;

    public RoutingTests()
    {
        _app = new TuiApp(_terminal, new TuiAppOptions { FrameInterval = TimeSpan.Zero });
        _run = AppThread.Start<RoutingFixture>(_app);
        Until(() => _terminal.Writes.Count > 0, "the first frame");
    }

    private void Until(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (_run.IsFaulted) throw new InvalidOperationException($"The app ended while waiting for {what}.", _run.Exception!.GetBaseException());
            if (DateTime.UtcNow > deadline) throw new TimeoutException($"Timed out waiting for {what}.");
            Thread.Sleep(5);
        }
    }

    /// <summary>The element with an id, once the tree has one.</summary>
    private HostElement? Element(string id) =>
        _app.Renderer.Root.Descendants().OfType<HostElement>().FirstOrDefault(e => e.Id == id);

    /// <summary>What the routed page is showing, from the tree rather than the painted diff.</summary>
    private string Page => Text(Element("page"));

    private static string Text(HostElement? element) =>
        element is null ? "" : string.Concat(element.Descendants().OfType<HostTextNode>().Select(t => t.Text)).Trim();

    private void UntilPage(string expected) => Until(() => Page == expected, $"the page to be '{expected}' (it is '{Page}')");

    public async ValueTask DisposeAsync()
    {
        _app.Exit();
        await _run;
    }

    [Fact]
    public void The_route_for_the_starting_location_is_what_renders()
    {
        UntilPage("home page");
        Assert.Equal(TerminalNavigationManager.DefaultBaseUri, _app.Navigation.Uri);
    }

    [Fact]
    public async Task NavigateTo_changes_the_page()
    {
        UntilPage("home page");
        await _app.InvokeAsync(() => _app.Navigation.NavigateTo("/settings"));
        UntilPage("settings page");
    }

    [Fact]
    public async Task A_route_parameter_reaches_the_component_typed()
    {
        await _app.InvokeAsync(() => _app.Navigation.NavigateTo("/thing/7"));
        UntilPage("thing 7");
    }

    [Fact]
    public async Task An_unrouted_location_falls_to_NotFound()
    {
        UntilPage("home page");
        await _app.InvokeAsync(() => _app.Navigation.NavigateTo("/nowhere"));
        UntilPage("no route");
    }

    [Fact]
    public void A_link_with_an_href_is_focusable_and_in_the_tab_order_without_a_tabindex()
    {
        UntilPage("home page");
        var link = Element("settings")!;
        Assert.True(link.IsLink);
        Assert.True(link.Focusable);
        Assert.True(link.Tabbable);
        Assert.True(link.Node.Style.TextStyle.HasFlag(TextStyle.Underline), "a link is underlined by the user-agent sheet");

        // An anchor with no href is not a link, exactly as on a page.
        var plain = Element("nolink")!;
        Assert.False(plain.IsLink);
        Assert.False(plain.Focusable);
        Assert.False(plain.Node.Style.TextStyle.HasFlag(TextStyle.Underline), "an anchor with no href is not underlined");
    }

    [Fact]
    public async Task Enter_on_a_focused_link_follows_it()
    {
        UntilPage("home page");
        await _app.InvokeAsync(() => _app.Focus.FocusAsync(Element("thing")!));
        _terminal.Inject("\r");
        UntilPage("thing 7");
    }

    [Fact]
    public void A_click_in_a_link_follows_it()
    {
        UntilPage("home page");
        var link = Element("settings")!;
        // The inline link has no box of its own; the anonymous box the flex
        // container made for it does.
        var bar = link.AncestorElements().First();
        var rect = bar.LayoutChildren.OfType<AnonymousTextNode>()
            .First(a => ReferenceEquals(a.BlockifiedElement, link)).Layout;
        Assert.False(rect.IsEmpty, "the link has no box to click");
        // SGR press, 1-based, inside the link's box.
        _terminal.Inject($"\e[<0;{rect.X + 1};{rect.Y + 1}M");
        UntilPage("settings page");
    }

    [Fact]
    public async Task An_href_this_app_cannot_route_to_is_reported_and_left_alone()
    {
        UntilPage("home page");
        HostElement? followed = null;
        _app.LinkFollowed += link => followed = link;

        await _app.InvokeAsync(() => _app.Focus.FocusAsync(Element("outside")!));
        _terminal.Inject("\r");
        Until(() => followed is not null, "the link to report itself");

        Assert.Equal("https://example.com", followed!.Href);
        // The location did not move: nothing here decides to open a browser.
        Thread.Sleep(50);
        Assert.Equal("home page", Page);
    }

    [Fact]
    public async Task Back_returns_to_the_previous_location()
    {
        UntilPage("home page");
        await _app.InvokeAsync(() => _app.Navigation.NavigateTo("/settings"));
        UntilPage("settings page");
        await _app.InvokeAsync(() => _app.Navigation.NavigateTo("/thing/7"));
        UntilPage("thing 7");

        await _app.InvokeAsync(() => Assert.True(_app.Navigation.Back()));
        UntilPage("settings page");
        await _app.InvokeAsync(() => Assert.True(_app.Navigation.Back()));
        UntilPage("home page");
        await _app.InvokeAsync(() => Assert.False(_app.Navigation.Back()));
    }

    [Fact]
    public async Task Replacing_the_history_entry_leaves_nothing_to_go_back_to()
    {
        UntilPage("home page");
        await _app.InvokeAsync(() => _app.Navigation.NavigateTo("/settings", new NavigationOptions { ReplaceHistoryEntry = true }));
        UntilPage("settings page");
        Assert.False(_app.Navigation.CanGoBack);
    }
}
