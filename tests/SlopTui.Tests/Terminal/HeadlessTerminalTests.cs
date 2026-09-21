using SlopTui.Layout;
using SlopTui.Terminal;

namespace SlopTui.Tests.Terminal;

public class HeadlessTerminalTests
{
    [Fact]
    public void Each_write_is_one_entry()
    {
        var terminal = new HeadlessTerminal(20, 5);
        terminal.Write("ab");
        terminal.Write("cd");

        Assert.Equal(["ab", "cd"], terminal.Writes);
        Assert.Equal("abcd", terminal.Output);
        terminal.ClearWrites();
        Assert.Empty(terminal.Writes);
    }

    [Fact]
    public void Injected_input_arrives_with_a_stamp()
    {
        var terminal = new HeadlessTerminal();
        InputChunk? received = null;
        terminal.InputReceived += chunk => received = chunk;

        terminal.Inject("x", 42);

        Assert.Equal("x", received!.Value.Text);
        Assert.Equal(42, received.Value.TimestampTicks);

        terminal.Inject("y");
        Assert.True(received!.Value.TimestampTicks > 42);
    }

    [Fact]
    public void Resize_changes_the_size_and_tells_subscribers()
    {
        var terminal = new HeadlessTerminal(80, 24);
        Size? seen = null;
        terminal.Resized += size => seen = size;

        terminal.Resize(100, 40);

        Assert.Equal(new Size(100, 40), terminal.Size);
        Assert.Equal(new Size(100, 40), seen);
    }

    [Fact]
    public void Start_and_stop_are_recorded_and_stop_is_idempotent()
    {
        var terminal = new HeadlessTerminal();
        var options = new TerminalOptions { Mouse = false };

        terminal.Start(options);
        Assert.True(terminal.IsStarted);
        Assert.Same(options, terminal.StartedWith);

        terminal.Stop();
        terminal.Stop();
        Assert.False(terminal.IsStarted);
        Assert.Equal(1, terminal.StartCount);
        Assert.Equal(1, terminal.StopCount);
    }
}
