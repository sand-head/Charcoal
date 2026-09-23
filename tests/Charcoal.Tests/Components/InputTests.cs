using Charcoal.Components;
using Charcoal.Terminal;
using Charcoal.Tests.Components.Fixtures;

namespace Charcoal.Tests.Components;

public class InputTests
{
    private sealed class Running : IDisposable
    {
        public readonly HeadlessTerminal Terminal = new(40, 8);
        public readonly TuiApp App;
        public readonly Task<int> Run;

        public Running()
        {
            App = new TuiApp(Terminal, new TuiAppOptions { FrameInterval = TimeSpan.Zero });
            Run = Task.Run(() => App.Run<FormFixture>());
            Until(() => Terminal.Writes.Count > 0, "the first frame");
        }

        public void Type(string input) => Terminal.Inject(input);

        public void Until(Func<bool> condition, string what)
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!condition())
            {
                if (Run.IsFaulted) throw new InvalidOperationException($"The app ended while waiting for {what}.", Run.Exception!.GetBaseException());
                if (DateTime.UtcNow > deadline) throw new TimeoutException($"Timed out waiting for {what}.");
                Thread.Sleep(5);
            }
        }

        public string? FocusedId => App.Focus.Focused?.Id;

        /// <summary>The echo line's text, read on the loop, which owns the tree.</summary>
        public async Task<string> EchoAsync()
        {
            var text = "";
            await App.InvokeAsync(() => text = string.Concat(App.Renderer.Root.Descendants().OfType<HostElement>().First(e => e.Id == "echo").Runs.Select(r => r.Text)));
            return text;
        }

        public void Dispose()
        {
            App.Exit();
            Run.Wait(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task Autofocus_takes_the_keyboard_and_a_live_binding_sees_every_key_without_losing_the_field()
    {
        using var running = new Running();
        running.Until(() => running.FocusedId == "name", "autofocus on the name field");

        running.Type("ab");
        running.Until(() => FormFixture.Last!.Name == "ab", "the binding to see ab");
        // The binding re-rendered the component; the field kept its text and caret.
        running.Type("c");
        running.Until(() => FormFixture.Last!.Name == "abc", "the binding to see abc");
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!(await running.EchoAsync()).StartsWith("abc|"))
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("The echo never rendered abc.");
            await Task.Delay(5);
        }
    }

    [Fact]
    public void A_plain_binding_sees_the_value_on_enter_and_on_blur_and_tab_moves_between_fields()
    {
        using var running = new Running();
        running.Until(() => running.FocusedId == "name", "autofocus");

        running.Type("\t");
        running.Until(() => running.FocusedId == "secret", "Tab to the password");
        running.Type("pw");
        Thread.Sleep(50);
        Assert.Equal("", FormFixture.Last!.Secret);   // onchange has not fired yet
        running.Type("\r");
        running.Until(() => FormFixture.Last!.Secret == "pw", "Enter to commit the password");
        Assert.Contains("••", running.Terminal.Output);

        running.Type("\t");
        running.Until(() => running.FocusedId == "notes", "Tab to the textarea");
        running.Type("x\ry");
        Thread.Sleep(50);
        Assert.Equal("", FormFixture.Last!.Notes);    // Enter made a line, committed nothing
        running.Type("\e[Z");                          // Shift+Tab: leaving commits
        running.Until(() => FormFixture.Last!.Notes == "x\ny", "blur to commit the notes");
        Assert.Equal("secret", running.FocusedId);
    }

    [Fact]
    public async Task Ctrl_c_in_a_field_still_exits()
    {
        var running = new Running();
        running.Until(() => running.FocusedId == "name", "autofocus");
        running.Type("\x03");
        Assert.Equal(0, await running.Run.WaitAsync(TimeSpan.FromSeconds(5)));
    }
}
