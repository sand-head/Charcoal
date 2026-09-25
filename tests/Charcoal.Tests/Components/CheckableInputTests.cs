using Charcoal.Components;
using Charcoal.Terminal;
using Charcoal.Tests.Components.Fixtures;

namespace Charcoal.Tests.Components;

public class CheckableInputTests
{
    [Fact]
    public async Task A_checkbox_binds_immediately_after_Space_and_a_click()
    {
        var terminal = new HeadlessTerminal(20, 4);
        var app = new TuiApp(terminal, new TuiAppOptions { FrameInterval = TimeSpan.Zero });
        var run = AppThread.Start<CheckableFixture>(app);
        Until(() => terminal.Writes.Count > 0 && app.Focus.Focused?.Id == "done", "the initial focused checkbox", run);

        terminal.Inject(" ");
        Until(() => CheckableFixture.Last!.Done, "Space to update the binding", run);

        terminal.Inject("\e[<0;1;1M");
        Until(() => !CheckableFixture.Last!.Done, "a click to update the binding", run);

        app.Exit();
        await run;
    }

    private static void Until(Func<bool> condition, string what, Task run)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (run.IsFaulted) throw new InvalidOperationException($"The app ended while waiting for {what}.", run.Exception!.GetBaseException());
            if (DateTime.UtcNow > deadline) throw new TimeoutException($"Timed out waiting for {what}.");
            Thread.Sleep(5);
        }
    }
}
