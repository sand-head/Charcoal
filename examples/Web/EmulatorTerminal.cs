using System.Text;
using Charcoal.Layout;
using Charcoal.Terminal;
using SlopTerm.Core.Screen;

namespace Web;

/// <summary>
/// A terminal made of slopterm's emulator: frames are fed to it, and the keys
/// it encodes and the answers it gives to queries come back as input.
/// </summary>
public sealed class EmulatorTerminal : ITerminal
{
    private readonly TerminalEmulator _emulator;
    private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();
    private TerminalOptions _options = TerminalOptions.Default;
    private bool _started;

    public EmulatorTerminal(TerminalEmulator emulator)
    {
        _emulator = emulator;
        _emulator.Respond += Receive;
    }

    /// <inheritdoc/>
    public Size Size => new(_emulator.Cols, _emulator.Rows);

    /// <inheritdoc/>
    public bool IsInteractive => true;

    /// <inheritdoc/>
    public event Action<InputChunk>? InputReceived;

    /// <inheritdoc/>
    public event Action<Size>? Resized;

    /// <inheritdoc/>
    public void Start(TerminalOptions options)
    {
        if (_started) return;
        _started = true;
        _options = options;
        Write(Ansi.Enter(options));
    }

    /// <inheritdoc/>
    public void Stop()
    {
        if (!_started) return;
        _started = false;
        Write(Ansi.Leave(_options));
    }

    /// <inheritdoc/>
    public void Write(ReadOnlySpan<char> text)
    {
        if (text.IsEmpty) return;
        _emulator.Feed(Encoding.UTF8.GetBytes(text.ToArray()));
    }

    /// <summary>Bytes the emulator sends toward the app: typed keys, mouse reports, query replies.</summary>
    public void Receive(byte[] data)
    {
        // A decoder, since a character may be split across two sends.
        var chars = new char[_decoder.GetCharCount(data, flush: false)];
        var count = _decoder.GetChars(data, chars, flush: false);
        if (count == 0) return;
        InputReceived?.Invoke(new InputChunk(new string(chars, 0, count), DateTime.UtcNow.Ticks));
    }

    /// <summary>Tells the app the emulator's grid changed size.</summary>
    public void NotifyResized() => Resized?.Invoke(Size);

    public void Dispose()
    {
        Stop();
        _emulator.Respond -= Receive;
    }
}
