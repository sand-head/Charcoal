using System.Runtime.InteropServices;

namespace SlopTui.Terminal;

/// <summary>The libc calls behind the Unix terminal.</summary>
/// <remarks>
/// The termios struct differs between Linux and macOS, so it is kept as an
/// opaque buffer that only <c>tcgetattr</c>, <c>cfmakeraw</c> and
/// <c>tcsetattr</c> touch.
/// </remarks>
internal static class Unix
{
    public const int StdinFd = 0;
    public const int StdoutFd = 1;

    private const int TermiosBufferSize = 256;
    private const int TCSANOW = 0;
    private const short POLLIN = 0x001;
    private const int O_RDWR = 2;
    private const int EINTR = 4;
    private const int EAGAIN = 11;

    private static readonly nuint TIOCGWINSZ = OperatingSystem.IsLinux() ? 0x5413u : 0x40087468u;

    [StructLayout(LayoutKind.Sequential)]
    private struct PollFd
    {
        public int Fd;
        public short Events;
        public short Revents;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinSize
    {
        public ushort Rows;
        public ushort Cols;
        public ushort XPixel;
        public ushort YPixel;
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int isatty(int fd);

    [DllImport("libc", SetLastError = true, CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    private static extern int open([MarshalAs(UnmanagedType.LPStr)] string path, int flags);

    [DllImport("libc", SetLastError = true)]
    private static extern int close(int fd);

    [DllImport("libc", SetLastError = true)]
    private static extern nint read(int fd, ref byte buffer, nint count);

    [DllImport("libc", SetLastError = true)]
    private static extern nint write(int fd, in byte buffer, nint count);

    [DllImport("libc", SetLastError = true)]
    private static extern int poll(ref PollFd fds, nuint count, int timeoutMs);

    [DllImport("libc", SetLastError = true)]
    private static extern int tcgetattr(int fd, byte[] termios);

    [DllImport("libc", SetLastError = true)]
    private static extern int tcsetattr(int fd, int actions, byte[] termios);

    [DllImport("libc")]
    private static extern void cfmakeraw(byte[] termios);

    [DllImport("libc", SetLastError = true)]
    private static extern int ioctl(int fd, nuint request, out WinSize size);

    public static bool IsTty(int fd) => isatty(fd) == 1;

    /// <summary>Opens the controlling terminal, or returns -1.</summary>
    public static int OpenControllingTty() => open("/dev/tty", O_RDWR);

    public static void Close(int fd) => close(fd);

    /// <summary>The descriptor's termios, or null when it is not a terminal.</summary>
    public static byte[]? SaveTermios(int fd)
    {
        var block = new byte[TermiosBufferSize];
        return tcgetattr(fd, block) == 0 ? block : null;
    }

    public static bool EnterRaw(int fd, byte[] saved)
    {
        var raw = (byte[])saved.Clone();
        cfmakeraw(raw);
        return tcsetattr(fd, TCSANOW, raw) == 0;
    }

    public static bool RestoreTermios(int fd, byte[] saved) => tcsetattr(fd, TCSANOW, saved) == 0;

    public static (int Cols, int Rows)? WindowSize(int fd)
    {
        if (ioctl(fd, TIOCGWINSZ, out var size) != 0 || size.Cols == 0 || size.Rows == 0) return null;
        return (size.Cols, size.Rows);
    }

    /// <summary>Waits until the descriptor is readable or the timeout passes.</summary>
    public static bool WaitReadable(int fd, int timeoutMs)
    {
        var pfd = new PollFd { Fd = fd, Events = POLLIN };
        while (true)
        {
            var n = poll(ref pfd, 1, timeoutMs);
            if (n > 0) return pfd.Revents != 0;
            if (n == 0) return false;
            if (Marshal.GetLastPInvokeError() != EINTR) return false;
        }
    }

    /// <summary>Reads once, returning the byte count, 0 when nothing was read, or -1 on error.</summary>
    public static int Read(int fd, Span<byte> buffer)
    {
        while (true)
        {
            var n = read(fd, ref MemoryMarshal.GetReference(buffer), buffer.Length);
            if (n >= 0) return (int)n;
            var errno = Marshal.GetLastPInvokeError();
            if (errno == EINTR) continue;
            return errno == EAGAIN ? 0 : -1;
        }
    }

    /// <summary>Writes all the bytes, retrying partial and interrupted writes.</summary>
    public static void WriteAll(int fd, ReadOnlySpan<byte> bytes)
    {
        var offset = 0;
        while (offset < bytes.Length)
        {
            var rest = bytes[offset..];
            var n = write(fd, in MemoryMarshal.GetReference(rest), rest.Length);
            if (n < 0)
            {
                var errno = Marshal.GetLastPInvokeError();
                if (errno is EINTR or EAGAIN) continue;
                return;
            }
            offset += (int)n;
        }
    }
}
