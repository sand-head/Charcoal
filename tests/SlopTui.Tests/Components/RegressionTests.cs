using Engine = SlopTui.Layout.FlexLayout;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using SlopTui.Components;
using SlopTui.Terminal;

namespace SlopTui.Tests.Components;

public class RegressionTests
{
    private sealed class Cards : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "box"); b.AddAttribute(1, "class", "page");
            b.OpenElement(2, "text"); b.AddAttribute(3, "class", "title"); b.AddContent(4, "styled with a stylesheet"); b.CloseElement();
            b.OpenElement(5, "text"); b.AddAttribute(6, "class", "muted"); b.AddContent(7, "Tab moves focus between the cards; the focused card's border and heading turn green through :focus rules. q quits."); b.CloseElement();
            b.OpenElement(8, "box"); b.AddAttribute(9, "class", "cards");
            for (var i = 0; i < 3; i++)
            {
                b.OpenElement(10, "box"); b.AddAttribute(11, "class", "card"); b.AddAttribute(12, "id", "card" + i);
                b.OpenElement(13, "text"); b.AddAttribute(14, "class", "heading"); b.AddContent(15, "plain card"); b.CloseElement();
                b.OpenElement(16, "text"); b.AddContent(17, "Its colour is white because .card says so and text inherits it; its wrapping is wrap because \".card text\" says so."); b.CloseElement();
                b.CloseElement();
            }
            b.CloseElement();
            b.OpenElement(18, "text"); b.AddAttribute(19, "id", "footer"); b.AddContent(20, "#footer is an id selector, and italic comes from it."); b.CloseElement();
            b.CloseElement();
        }
    }

    [Fact]
    public async Task Cards_keep_their_content_height()
    {
        var terminal = new HeadlessTerminal(96, 16);
        var app = new TuiApp(terminal, new TuiAppOptions { FrameInterval = TimeSpan.Zero });
        app.AddStylesheet("""
            box.page { padding: 1 2; gap: 1; flex-direction: column; }
            .cards { gap: 2; }
            .card { flex-direction: column; border: round; padding: 0 1; width: 26; color: white; }
            .card .heading { bold: true; }
            .card text { wrap: wrap; }
            """);
        var run = Task.Run(() => app.Run<Cards>());
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (terminal.Writes.Count == 0 && DateTime.UtcNow < deadline) Thread.Sleep(5);
        Thread.Sleep(100);
        var els = app.Renderer.Root.Descendants().OfType<HostElement>().ToList();
        var card = els.First(e => e.Id == "card0");
        var cards = els.First(e => e.Classes.Contains("cards"));
        var page = els.First(e => e.Classes.Contains("page"));
        var body = card.Children.OfType<HostElement>().Last();
        var info = $"page {page.Node.Layout} cards {cards.Node.Layout} card {card.Node.Layout} body {body.Node.Layout} bodyMeasure {Engine.Measure(body.Node, 24, null)} cardMin {Engine.AutomaticMinimum(card.Node, false, 92, null)} cardsMin {Engine.AutomaticMinimum(cards.Node, false, 92, null)} pageMin {Engine.AutomaticMinimum(page.Node, false, 96, null)}";
        app.Exit(); await run;
        Assert.True(card.Node.Layout.Height >= 9, info);
    }

    private sealed class FocusesOnAfterRender : ComponentBase
    {
        [Inject] public TuiApp Tui { get; set; } = default!;
        public static string Log = "";

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "box"); b.AddAttribute(1, "focusable", true);
            b.OpenComponent<ScrollBox>(2);
            b.AddComponentParameter(3, nameof(ScrollBox.ChildContent), (RenderFragment)(inner =>
            {
                for (var i = 1; i <= 30; i++) { inner.OpenElement(0, "text"); inner.SetKey(i); inner.AddContent(1, $"line {i}"); inner.CloseElement(); }
            }));
            b.CloseComponent();
            b.CloseElement();
        }

        protected override void OnAfterRender(bool firstRender)
        {
            if (!firstRender) return;
            var element = Tui.Renderer.Root.Descendants().OfType<HostElement>().FirstOrDefault(e => e.Id is { } id && id.StartsWith("scrollbox-"));
            Log += element is null ? "no element;" : "found;";
            if (element is not null)
            {
                var t = Tui.Focus.FocusAsync(element);
                t.ContinueWith(x => Log += x.IsFaulted ? "faulted:" + x.Exception!.GetBaseException().Message : "focused;", TaskScheduler.Default);
            }
        }
    }

    [Fact]
    public async Task Focusing_from_OnAfterRender_sticks()
    {
        var terminal = new HeadlessTerminal(40, 10);
        var app = new TuiApp(terminal, new TuiAppOptions { FrameInterval = TimeSpan.Zero });
        var run = Task.Run(() => app.Run<FocusesOnAfterRender>());
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (terminal.Writes.Count < 2 && DateTime.UtcNow < deadline) Thread.Sleep(5);
        Thread.Sleep(200);
        var focused = app.Focus.Focused?.Id;
        app.Exit(); await run;
        Assert.True(focused is not null, $"nothing focused; log: {FocusesOnAfterRender.Log}");
    }

    private sealed class GrowingList : ComponentBase
    {
        [Inject] public TuiApp Tui { get; set; } = default!;
        public static GrowingList? Last;
        public int Lines = 30;
        protected override void OnInitialized() => Last = this;

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "box"); b.AddAttribute(1, "flex-direction", "column");
            b.OpenElement(2, "text"); b.AddContent(3, $"{Lines} lines"); b.CloseElement();
            b.OpenComponent<ScrollBox>(4);
            b.AddComponentParameter(5, nameof(ScrollBox.Autofocus), true);
            b.AddComponentParameter(6, nameof(ScrollBox.StickToBottom), true);
            b.AddComponentParameter(7, nameof(ScrollBox.ScrollTopChanged), EventCallback.Factory.Create<int>(this, _ => { }));
            b.AddComponentParameter(8, nameof(ScrollBox.ChildContent), (RenderFragment)(inner =>
            {
                for (var i = 1; i <= Lines; i++) { inner.OpenElement(0, "text"); inner.SetKey(i); inner.AddContent(1, $"line {i}"); inner.CloseElement(); }
            }));
            b.CloseComponent();
            b.CloseElement();
        }

        public void Grow() { Lines++; StateHasChanged(); }
    }

    [Fact]
    public async Task Focus_survives_the_parent_re_rendering_with_a_new_line()
    {
        var terminal = new HeadlessTerminal(40, 10);
        var app = new TuiApp(terminal, new TuiAppOptions { FrameInterval = TimeSpan.Zero });
        var log = new List<string>();
        var run = Task.Run(() => app.Run<GrowingList>());
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while ((terminal.Writes.Count < 1 || app.Focus.Focused is null) && DateTime.UtcNow < deadline) Thread.Sleep(5);
        app.Focus.Changed += (f, t) => log.Add($"{f?.Id ?? "none"} → {t?.Id ?? "none"}");
        var before = app.Focus.Focused?.Id;
        var frames = terminal.Writes.Count;
        await app.InvokeAsync(() => GrowingList.Last!.Grow());
        deadline = DateTime.UtcNow.AddSeconds(3);
        while (terminal.Writes.Count == frames && DateTime.UtcNow < deadline) Thread.Sleep(5);
        Thread.Sleep(100);
        var after = app.Focus.Focused?.Id;
        app.Exit(); await run;
        Assert.True(before is not null && after == before, $"before {before}, after {after ?? "none"}; changes: {string.Join(", ", log)}");
    }
}
