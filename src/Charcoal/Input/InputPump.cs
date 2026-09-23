using System.Collections.Concurrent;
using Charcoal.Terminal;

namespace Charcoal.Input;

/// <summary>
/// Carries input chunks from the reader thread to the app loop, which feeds
/// them to the parser one chunk at a time so each keeps its own timestamp.
/// </summary>
public sealed class InputPump
{
    private readonly ConcurrentQueue<InputChunk> _chunks = new();
    private readonly AnsiKeyParser _parser = new();

    /// <summary>Raised on the enqueuing thread; handlers should only signal the loop.</summary>
    public event Action? ChunkQueued;

    /// <summary>When a pending lone ESC becomes the Esc key, or null.</summary>
    public long? PendingEscapeDeadlineTicks => _parser.EscDeadlineTicks;

    public bool HasPending => !_chunks.IsEmpty;

    /// <summary>Queues a chunk from any thread.</summary>
    public void Enqueue(InputChunk chunk)
    {
        _chunks.Enqueue(chunk);
        ChunkQueued?.Invoke();
    }

    /// <summary>Parses every queued chunk, and a lone ESC whose gap has passed.</summary>
    public List<InputEvent> Drain(long nowTicks)
    {
        var events = new List<InputEvent>();
        var unrecognised = new List<string>();

        while (_chunks.TryDequeue(out var chunk))
        {
            var result = _parser.Feed(chunk.Text, chunk.TimestampTicks);
            events.AddRange(result.Events);
            if (!result.Unrecognised.IsEmpty) unrecognised.Add(result.Unrecognised.ToString());
        }

        if (_parser.EscPending)
        {
            events.AddRange(_parser.Feed(string.Empty, nowTicks).Events);
        }

        if (unrecognised.Count > 0) Unrecognised?.Invoke(string.Concat(unrecognised));
        return events;
    }

    /// <summary>Raised during <see cref="Drain"/> with input the parser did not recognise.</summary>
    public event Action<string>? Unrecognised;
}
