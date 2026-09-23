using Charcoal.Input;
using Charcoal.Terminal;

namespace Charcoal.Tests.Input;

public class InputPumpTests
{
    private const long Ms = TimeSpan.TicksPerMillisecond;

    [Fact]
    public void Chunks_drain_in_order_as_events()
    {
        var pump = new InputPump();
        pump.Enqueue(new InputChunk("ab", 0));
        pump.Enqueue(new InputChunk("\e[A", 1 * Ms));

        var events = pump.Drain(2 * Ms);

        Assert.Equal(3, events.Count);
        Assert.Equal((Key)'A', Assert.IsType<KeyEvent>(events[0]).Key);
        Assert.Equal((Key)'B', Assert.IsType<KeyEvent>(events[1]).Key);
        Assert.Equal(Key.Up, Assert.IsType<KeyEvent>(events[2]).Key);
        Assert.False(pump.HasPending);
    }

    [Fact]
    public void A_sequence_split_across_chunks_is_one_key()
    {
        var pump = new InputPump();
        pump.Enqueue(new InputChunk("\e[1;", 0));
        pump.Enqueue(new InputChunk("5A", 1 * Ms));

        var key = Assert.IsType<KeyEvent>(Assert.Single(pump.Drain(2 * Ms)));
        Assert.Equal(Key.Up, key.Key);
        Assert.Equal(KeyModifiers.Ctrl, key.Modifiers);
    }

    [Fact]
    public void The_ESC_gap_reads_the_chunk_stamps_not_the_drain_time()
    {
        // ESC in one chunk, "x" 6 ms later in another: Alt+x, however late
        // the loop drained them — the reads were inside the gap.
        var pump = new InputPump();
        pump.Enqueue(new InputChunk("\e", 0));
        pump.Enqueue(new InputChunk("x", 6 * Ms));

        var key = Assert.IsType<KeyEvent>(Assert.Single(pump.Drain(500 * Ms)));
        Assert.Equal((Key)'X', key.Key);
        Assert.Equal(KeyModifiers.Alt, key.Modifiers);
    }

    [Fact]
    public void A_lone_ESC_expires_on_a_later_drain_and_reports_its_deadline()
    {
        var pump = new InputPump();
        pump.Enqueue(new InputChunk("\e", 100 * Ms));

        Assert.Empty(pump.Drain(103 * Ms));
        Assert.Equal(100 * Ms + AnsiKeyParser.EscGapTicks, pump.PendingEscapeDeadlineTicks);

        Assert.Empty(pump.Drain(107 * Ms));

        var esc = Assert.IsType<KeyEvent>(Assert.Single(pump.Drain(109 * Ms)));
        Assert.Equal(Key.Escape, esc.Key);
        Assert.Null(pump.PendingEscapeDeadlineTicks);
    }

    [Fact]
    public void Enqueue_raises_the_wake_signal_on_the_caller_thread()
    {
        var pump = new InputPump();
        var wakes = 0;
        var threadId = 0;
        pump.ChunkQueued += () => { wakes++; threadId = Environment.CurrentManagedThreadId; };

        pump.Enqueue(new InputChunk("a", 0));

        Assert.Equal(1, wakes);
        Assert.Equal(Environment.CurrentManagedThreadId, threadId);
        Assert.True(pump.HasPending);
    }

    [Fact]
    public void Unrecognised_bytes_are_reported_not_dropped_silently()
    {
        var pump = new InputPump();
        var seen = "";
        pump.Unrecognised += text => seen += text;
        pump.Enqueue(new InputChunk("\e[10~x", 0));

        var key = Assert.IsType<KeyEvent>(Assert.Single(pump.Drain(1 * Ms)));
        Assert.Equal((Key)'X', key.Key);
        Assert.Equal("\e[10~", seen);
    }

    [Fact]
    public void Enqueue_is_safe_from_many_threads()
    {
        var pump = new InputPump();
        var threads = Enumerable.Range(0, 8).Select(i => new Thread(() =>
        {
            for (var n = 0; n < 100; n++) pump.Enqueue(new InputChunk("a", n));
        })).ToList();
        foreach (var t in threads) t.Start();
        foreach (var t in threads) t.Join();

        Assert.Equal(800, pump.Drain(1000 * Ms).Count);
    }
}
