using Charcoal.Components;
using Charcoal.Terminal;
using Charcoal.Tests.Components.Fixtures;

namespace Charcoal.Tests.Components;

public class ButtonTests
{
    [Fact]
    public async Task A_button_is_tabbable_and_Enter_or_Space_activates_its_click_handler()
    {
        var terminal = new HeadlessTerminal(20, 4);
        var app = new TuiApp(terminal, new TuiAppOptions { FrameInterval = TimeSpan.Zero });
        var run = AppThread.Start<ButtonFixture>(app);
        Until(() => terminal.Writes.Count > 0, "the first frame", run);

        terminal.Inject("\t\r");
        Until(() => ButtonFixture.Last!.Count == 1, "Enter to activate the button", run);
        terminal.Inject(" ");
        Until(() => ButtonFixture.Last!.Count == 2, "Space to activate the button", run);

        terminal.Inject("\t\r");
        Until(() => ButtonFixture.Last!.Count == 3, "Enter to activate the input button", run);

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
