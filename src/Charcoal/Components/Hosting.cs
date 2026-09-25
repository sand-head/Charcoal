using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Charcoal.Routing;
using Charcoal.Terminal;

namespace Charcoal.Components;

/// <summary>Registers a terminal app with the .NET generic host.</summary>
public static class CharcoalHostingServiceCollectionExtensions
{
    /// <summary>Registers <typeparamref name="TRoot"/> and its terminal app with the generic host.</summary>
    public static IServiceCollection AddCharcoal<TRoot>(this IServiceCollection services, TuiAppOptions? options = null)
        where TRoot : IComponent
    {
        services.TryAddSingleton<ITerminal, ConsoleTerminal>();
        services.TryAddSingleton<TuiApp>(provider => new TuiApp(
            provider.GetRequiredService<ITerminal>(),
            options,
            provider.GetService<ILoggerFactory>()));
        services.TryAddSingleton(provider => provider.GetRequiredService<TuiApp>().Graphics);
        services.TryAddSingleton(provider => options?.Navigation ?? new TerminalNavigationManager());
        services.TryAddSingleton<NavigationManager>(provider => provider.GetRequiredService<TerminalNavigationManager>());
        services.TryAddSingleton<INavigationInterception, TerminalNavigationManager.Interception>();
        services.TryAddSingleton<IScrollToLocationHash, TerminalNavigationManager.NoHashScrolling>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, TuiAppHostedService<TRoot>>());
        return services;
    }
}

internal sealed class TuiAppHostedService<TRoot> : IHostedService where TRoot : IComponent
{
    private readonly TuiApp _app;
    private readonly IServiceProvider _services;
    private readonly IHostApplicationLifetime _lifetime;
    private Task<int>? _run;

    public TuiAppHostedService(TuiApp app, IServiceProvider services, IHostApplicationLifetime lifetime)
    {
        _app = app;
        _services = services;
        _lifetime = lifetime;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _run ??= Task.Factory.StartNew(
            () => _app.Run(typeof(TRoot), null, _services),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
        _ = _run.ContinueWith(
            _ => _lifetime.StopApplication(),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _app.Exit();
        return _run is null ? Task.CompletedTask : _run.WaitAsync(cancellationToken);
    }
}
