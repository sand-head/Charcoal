using Charcoal.Layout;

namespace Charcoal.Terminal;

/// <summary>A terminal for tests that records writes and takes injected input.</summary>
public sealed class HeadlessTerminal : ITerminal
{
    private Size _size;

    public HeadlessTerminal(int width = 80, int height = 24)
    {
        _size = new Size(width, height);
    }

    /// <inheritdoc/>
    public Size Size => _size;

    /// <inheritdoc/>
    public bool IsInteractive => true;

    /// <summary>One entry per <see cref="Write"/> call.</summary>
    public List<string> Writes { get; } = [];

    public string Output => string.Concat(Writes);

    public TerminalOptions? StartedWith { get; private set; }

    public bool IsStarted { get; private set; }

    public int StartCount { get; private set; }
    public int StopCount { get; private set; }

    /// <inheritdoc/>
    public event Action<InputChunk>? InputReceived;

    /// <inheritdoc/>
    public event Action<Size>? Resized;

    /// <inheritdoc/>
    public void Start(TerminalOptions options)
    {
        StartedWith = options;
        IsStarted = true;
        StartCount++;
    }

    /// <inheritdoc/>
    public void Stop()
    {
        if (!IsStarted) return;
        IsStarted = false;
        StopCount++;
    }

    /// <inheritdoc/>
    public void Write(ReadOnlySpan<char> text) => Writes.Add(text.ToString());

    /// <summary>Delivers input as if the terminal had just sent it.</summary>
    public void Inject(string text, long? timestampTicks = null) =>
        InputReceived?.Invoke(new InputChunk(text, timestampTicks ?? DateTime.UtcNow.Ticks));

    public void Resize(int width, int height)
    {
        _size = new Size(width, height);
        Resized?.Invoke(_size);
    }

    public void ClearWrites() => Writes.Clear();

    public void Dispose() => Stop();

    public override string ToString() => $"{_size.Width}x{_size.Height}, {Writes.Count} writes";
}
