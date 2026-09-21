using System.Runtime.InteropServices;

namespace SlopTui.Terminal;

/// <summary>The kernel32 calls behind the Windows terminal.</summary>
/// <remarks>
/// In VT input mode the console delivers input as the same bytes a Unix
/// terminal would. Reads block in <c>ReadFile</c> rather than peeking first,
/// because the event count includes key-up records that produce no bytes;
/// <c>CancelIoEx</c> unblocks the reader on shutdown.
/// </remarks>
internal static class Windows
{
    private const int STD_INPUT_HANDLE = -10;
    private const int STD_OUTPUT_HANDLE = -11;

    public const uint ENABLE_PROCESSED_INPUT = 0x0001;
    public const uint ENABLE_LINE_INPUT = 0x0002;
    public const uint ENABLE_ECHO_INPUT = 0x0004;
    public const uint ENABLE_VIRTUAL_TERMINAL_INPUT = 0x0200;

    public const uint ENABLE_PROCESSED_OUTPUT = 0x0001;
    public const uint ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004;
    public const uint DISABLE_NEWLINE_AUTO_RETURN = 0x0008;

    private const uint Utf8CodePage = 65001;
    private const int ERROR_OPERATION_ABORTED = 995;

    [StructLayout(LayoutKind.Sequential)]
    private struct Coord
    {
        public short X;
        public short Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SmallRect
    {
        public short Left;
        public short Top;
        public short Right;
        public short Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ConsoleScreenBufferInfo
    {
        public Coord Size;
        public Coord CursorPosition;
        public ushort Attributes;
        public SmallRect Window;
        public Coord MaximumWindowSize;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GetStdHandle(int which);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetConsoleMode(nint handle, out uint mode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetConsoleMode(nint handle, uint mode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetConsoleCP();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetConsoleOutputCP();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetConsoleCP(uint codePage);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetConsoleOutputCP(uint codePage);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadFile(nint handle, ref byte buffer, uint count, out uint read, nint overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WriteFile(nint handle, in byte buffer, uint count, out uint written, nint overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CancelIoEx(nint handle, nint overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetConsoleScreenBufferInfo(nint handle, out ConsoleScreenBufferInfo info);

    public static nint InputHandle => GetStdHandle(STD_INPUT_HANDLE);
    public static nint OutputHandle => GetStdHandle(STD_OUTPUT_HANDLE);

    /// <summary>Whether the handle is a console rather than a pipe or file.</summary>
    public static bool IsConsole(nint handle) => handle != 0 && handle != -1 && GetConsoleMode(handle, out _);

    public static uint? GetMode(nint handle) => GetConsoleMode(handle, out var mode) ? mode : null;

    public static bool SetMode(nint handle, uint mode) => SetConsoleMode(handle, mode);

    /// <summary>Switches both code pages to UTF-8 and returns the previous ones.</summary>
    public static (uint Input, uint Output) EnterUtf8()
    {
        var previous = (GetConsoleCP(), GetConsoleOutputCP());
        SetConsoleCP(Utf8CodePage);
        SetConsoleOutputCP(Utf8CodePage);
        return previous;
    }

    public static void RestoreCodePages((uint Input, uint Output) previous)
    {
        if (previous.Input != 0) SetConsoleCP(previous.Input);
        if (previous.Output != 0) SetConsoleOutputCP(previous.Output);
    }

    /// <summary>Blocks until bytes arrive; returns 0 when cancelled or at the end, -1 on error.</summary>
    public static int Read(nint handle, Span<byte> buffer)
    {
        if (ReadFile(handle, ref MemoryMarshal.GetReference(buffer), (uint)buffer.Length, out var read, 0))
        {
            return (int)read;
        }
        return Marshal.GetLastPInvokeError() == ERROR_OPERATION_ABORTED ? 0 : -1;
    }

    /// <summary>Aborts a <see cref="Read"/> blocked on another thread.</summary>
    public static void CancelRead(nint handle) => CancelIoEx(handle, 0);

    /// <summary>Writes all the bytes, retrying partial writes.</summary>
    public static void WriteAll(nint handle, ReadOnlySpan<byte> bytes)
    {
        var offset = 0;
        while (offset < bytes.Length)
        {
            var rest = bytes[offset..];
            var succeeded = WriteFile(handle, in MemoryMarshal.GetReference(rest), (uint)rest.Length, out var written, 0);
            if (!succeeded || written == 0) return;
            offset += (int)written;
        }
    }

    /// <summary>The visible window's size in cells, or null.</summary>
    public static (int Cols, int Rows)? WindowSize(nint handle)
    {
        if (!GetConsoleScreenBufferInfo(handle, out var info)) return null;
        var cols = info.Window.Right - info.Window.Left + 1;
        var rows = info.Window.Bottom - info.Window.Top + 1;
        return cols > 0 && rows > 0 ? (cols, rows) : null;
    }
}
