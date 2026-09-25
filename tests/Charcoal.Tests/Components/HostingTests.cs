using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Charcoal.Components;
using Charcoal.Terminal;

namespace Charcoal.Tests.Components;

public class HostingTests
{
    private sealed class Service
    {
    }

    private sealed class Root : ComponentBase
    {
        public static Service? Injected;
        public static TuiApp? App;

        [Inject]
        public Service Service { get; set; } = null!;

        [Inject]
        public TuiApp TuiApp { get; set; } = null!;

        protected override void OnInitialized()
        {
            Injected = Service;
            App = TuiApp;
        }

        protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddContent(0, "Hosted");
    }

    [Fact]
    public async Task AddCharcoal_runs_the_root_with_host_services_and_stops_the_host_when_the_app_exits()
    {
        Root.Injected = null;
        Root.App = null;
        var terminal = new HeadlessTerminal();
        var service = new Service();
        var builder = Host.CreateEmptyApplicationBuilder(null);
        builder.Services.AddSingleton<ITerminal>(terminal);
        builder.Services.AddSingleton(service);
        builder.Services.AddCharcoal<Root>(new TuiAppOptions { FrameInterval = TimeSpan.Zero });
        using var host = builder.Build();

        var run = host.RunAsync();
        WaitUntil(() => Root.Injected is not null, "the root component");

        Assert.Same(service, Root.Injected);
        Assert.Same(host.Services.GetRequiredService<TuiApp>(), Root.App);
        host.Services.GetRequiredService<TuiApp>().Exit(7);

        await run;
        Assert.False(terminal.IsStarted);
    }

    private static void WaitUntil(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException($"Timed out waiting for {what}.");
            Thread.Sleep(5);
        }
    }
}
