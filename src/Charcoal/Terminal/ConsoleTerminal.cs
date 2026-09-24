using System.Buffers;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Charcoal.Layout;

namespace Charcoal.Terminal;

/// <summary>The process's own terminal, in raw mode, read on a dedicated thread.</summary>
/// <remarks>
/// <see cref="Stop"/> also runs on process exit, on an unhandled exception and
/// on SIGTERM or SIGHUP, so the shell gets its terminal back. On Windows,
/// resizes are polled because VT input mode drops the window-size record.
/// </remarks>
public sealed class ConsoleTerminal : ITerminal
{
    private const int ReadBufferSize = 4096;
    private const int PollTimeoutMs = 100;
    private static readonly TimeSpan WindowsResizePoll = TimeSpan.FromMilliseconds(250);

    private readonly object _gate = new();
    private readonly Encoding _utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private volatile bool _running;
    private bool _started;
    private Thread? _inputThread;
    private TerminalOptions _options = TerminalOptions.Default;
    private Size _size;

    // Unix state
    private int _inputFd = Unix.StdinFd;
    private bool _ownsInputFd;
    private byte[]? _savedTermios;
    private readonly List<PosixSignalRegistration> _signals = [];

    // Windows state
    private nint _winInput;
    private nint _winOutput;
    private uint? _winInputMode;
    private uint? _winOutputMode;
    private (uint Input, uint Output)? _winCodePages;
    private Timer? _winResizeTimer;

    public ConsoleTerminal()
    {
        IsInteractive = OperatingSystem.IsWindows()
            ? Windows.IsConsole(Windows.InputHandle) && Windows.IsConsole(Windows.OutputHandle)
            : Unix.IsTty(Unix.StdinFd) && Unix.IsTty(Unix.StdoutFd);
        _size = QuerySize() ?? new Size(80, 24);
    }

    /// <inheritdoc/>
    public Size Size => _size;

    /// <inheritdoc/>
    public bool IsInteractive { get; }

    /// <inheritdoc/>
    public event Action<InputChunk>? InputReceived;

    /// <inheritdoc/>
    public event Action<Size>? Resized;

    /// <inheritdoc/>
    public void Start(TerminalOptions options)
    {
        lock (_gate)
        {
            if (_started) return;
            _started = true;
            _options = options;

            if (OperatingSystem.IsWindows()) StartWindows();
            else StartUnix();

            _size = QuerySize() ?? _size;

            Write(Ansi.Enter(options));

            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

            _running = true;
            _inputThread = new Thread(ReadLoop) { IsBackground = true, Name = "charcoal input" };
            _inputThread.Start();
        }
    }

    /// <inheritdoc/>
    public void Stop()
    {
        lock (_gate)
        {
            if (!_started) return;
            _started = false;
            _running = false;

            AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
            AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;

            Write(Ansi.Leave(_options));

            if (OperatingSystem.IsWindows()) StopWindows();
            else StopUnix();
        }

        // Joined outside the lock, since an input handler may be waiting on a lock of the app's.
        var thread = _inputThread;
        _inputThread = null;
        if (thread is not null && thread != Thread.CurrentThread) thread.Join(TimeSpan.FromSeconds(1));
    }

    /// <inheritdoc/>
    public void Write(ReadOnlySpan<char> text)
    {
        if (text.IsEmpty) return;
        var byteCount = _utf8.GetByteCount(text);
        var rented = byteCount <= 256 ? null : ArrayPool<byte>.Shared.Rent(byteCount);
        Span<byte> bytes = rented is null ? stackalloc byte[byteCount] : rented.AsSpan(0, byteCount);
        try
        {
            var written = _utf8.GetBytes(text, bytes);
            WriteBytes(bytes[..written]);
        }
        finally
        {
            if (rented is not null) ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private void WriteBytes(ReadOnlySpan<byte> bytes)
    {
        if (OperatingSystem.IsWindows())
        {
            Windows.WriteAll(WindowsOutput, bytes);
        }
        else
        {
            Unix.WriteAll(Unix.StdoutFd, bytes);
        }
    }

    private nint WindowsOutput => _winOutput != 0 ? _winOutput : Windows.OutputHandle;

    public void Dispose() => Stop();

    [UnsupportedOSPlatform("windows")]
    private void StartUnix()
    {
        // With stdin redirected, the keyboard is still on the controlling terminal.
        if (!Unix.IsTty(Unix.StdinFd))
        {
            var tty = Unix.OpenControllingTty();
            if (tty >= 0)
            {
                _inputFd = tty;
                _ownsInputFd = true;
            }
        }

        _savedTermios = Unix.SaveTermios(_inputFd);
        if (_savedTermios is not null) Unix.EnterRaw(_inputFd, _savedTermios);

        try
        {
            _signals.Add(PosixSignalRegistration.Create(PosixSignal.SIGWINCH, _ => OnResize()));
            _signals.Add(PosixSignalRegistration.Create(PosixSignal.SIGTERM, _ => Stop()));
            _signals.Add(PosixSignalRegistration.Create(PosixSignal.SIGHUP, _ => Stop()));
            _signals.Add(PosixSignalRegistration.Create(PosixSignal.SIGCONT, _ => OnResize()));
        }
        catch (PlatformNotSupportedException)
        {
            // Without signals, the process-exit hook still restores the terminal.
        }
    }

    [UnsupportedOSPlatform("windows")]
    private void StopUnix()
    {
        foreach (var signal in _signals) signal.Dispose();
        _signals.Clear();

        if (_savedTermios is not null)
        {
            Unix.RestoreTermios(_inputFd, _savedTermios);
            _savedTermios = null;
        }
        if (_ownsInputFd)
        {
            Unix.Close(_inputFd);
            _inputFd = Unix.StdinFd;
            _ownsInputFd = false;
        }
    }

    private void StartWindows()
    {
        _winInput = Windows.InputHandle;
        _winOutput = Windows.OutputHandle;

        _winInputMode = Windows.GetMode(_winInput);
        if (_winInputMode is { } inMode)
        {
            var raw = inMode & ~(Windows.ENABLE_LINE_INPUT | Windows.ENABLE_ECHO_INPUT | Windows.ENABLE_PROCESSED_INPUT);
            Windows.SetMode(_winInput, raw | Windows.ENABLE_VIRTUAL_TERMINAL_INPUT);
        }

        _winOutputMode = Windows.GetMode(_winOutput);
        if (_winOutputMode is { } outMode)
        {
            Windows.SetMode(_winOutput,
                outMode | Windows.ENABLE_PROCESSED_OUTPUT | Windows.ENABLE_VIRTUAL_TERMINAL_PROCESSING | Windows.DISABLE_NEWLINE_AUTO_RETURN);
        }

        _winCodePages = Windows.EnterUtf8();
        _winResizeTimer = new Timer(_ => OnResize(), null, WindowsResizePoll, WindowsResizePoll);
    }

    private void StopWindows()
    {
        _winResizeTimer?.Dispose();
        _winResizeTimer = null;

        Windows.CancelRead(_winInput);

        if (_winInputMode is { } inMode) Windows.SetMode(_winInput, inMode);
        if (_winOutputMode is { } outMode) Windows.SetMode(_winOutput, outMode);
        if (_winCodePages is { } pages) Windows.RestoreCodePages(pages);
        _winInputMode = null;
        _winOutputMode = null;
        _winCodePages = null;
    }

    private void ReadLoop()
    {
        var buffer = new byte[ReadBufferSize];
        var chars = new char[_utf8.GetMaxCharCount(ReadBufferSize)];
        var decoder = _utf8.GetDecoder();

        while (_running)
        {
            int n;
            if (OperatingSystem.IsWindows())
            {
                n = Windows.Read(_winInput, buffer);
                if (n == 0 && !_running) break;
            }
            else
            {
                if (!Unix.WaitReadable(_inputFd, PollTimeoutMs)) continue;
                n = Unix.Read(_inputFd, buffer);
            }
            if (n < 0) break;
            if (n == 0) continue;

            var stamp = DateTime.UtcNow.Ticks;
            var count = decoder.GetChars(buffer, 0, n, chars, 0, flush: false);
            if (count == 0) continue;
            var handlers = InputReceived;
            if (handlers is null) continue;
            try
            {
                handlers(new InputChunk(new string(chars, 0, count), stamp));
            }
            catch
            {
                // A failing handler must not stop the reader.
            }
        }
    }

    private Size? QuerySize()
    {
        var native = OperatingSystem.IsWindows()
            ? Windows.WindowSize(WindowsOutput)
            : Unix.WindowSize(Unix.StdoutFd) ?? Unix.WindowSize(_inputFd);
        if (native is { } size) return new Size(size.Cols, size.Rows);

        try
        {
            var width = Console.WindowWidth;
            var height = Console.WindowHeight;
            return width > 0 && height > 0 ? new Size(width, height) : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private void OnResize()
    {
        var size = QuerySize();
        if (size is null || size == _size) return;
        _size = size.Value;
        Resized?.Invoke(_size);
    }

    private void OnProcessExit(object? sender, EventArgs e) => Stop();

    private void OnUnhandledException(object? sender, UnhandledExceptionEventArgs e) => Stop();
}
