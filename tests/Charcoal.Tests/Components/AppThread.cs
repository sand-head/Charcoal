using Microsoft.AspNetCore.Components;
using Charcoal.Components;

namespace Charcoal.Tests.Components;

/// <summary>Runs an app on a thread of its own, as a console app does.</summary>
internal static class AppThread
{
    // A loop holds its thread until the app exits. On the thread pool, the
    // suite's running apps starve it, and first frames time out.
    public static Task<int> Start<TRoot>(TuiApp app) where TRoot : IComponent =>
        Task.Factory.StartNew(() => app.Run<TRoot>(), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
}
