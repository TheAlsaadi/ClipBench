using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Memory;
using Windows.Win32.System.Threading;

namespace ClipBench.Platform.Windows.Clipboard;

/// <summary>
/// The real Windows clipboard. A thin layer over the Win32 functions: it makes no decisions, so everything
/// that matters (what to read, what to refuse, what to wait for) is tested against the in-memory stand-in.
/// </summary>
internal sealed unsafe class Win32ClipboardApi : IClipboardApi, IProcessLookup
{
    // Windows numbers registered formats from 0xC000. A clipboard never holds thousands, so a count past this is a fault.
    private const int MaxFormats = 1_000;
    private const uint FirstRegisteredFormat = 0xC000;
    private const int MaxFormatNameLength = 256;
    private const int MaxImagePathLength = 1_024;

    private readonly HWND _owner;

    /// <summary>Creates the clipboard binding.</summary>
    /// <param name="ownerWindow">
    /// The window that becomes the clipboard owner when this program empties it, as a handle. Required for
    /// writing: Windows refuses to set data on a clipboard that has no owner window. Zero is fine for reading.
    /// </param>
    public Win32ClipboardApi(nint ownerWindow) => _owner = new HWND(ownerWindow);

    /// <inheritdoc/>
    public long SequenceNumber => PInvoke.GetClipboardSequenceNumber();

    /// <inheritdoc/>
    public IClipboardSession? TryOpen() => PInvoke.OpenClipboard(_owner) ? new Session() : null;

    /// <inheritdoc/>
    public uint RegisterFormat(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        return PInvoke.RegisterClipboardFormat(name);
    }

    /// <inheritdoc/>
    public bool IsFormatAvailable(uint format) => PInvoke.IsClipboardFormatAvailable(format);

    /// <inheritdoc/>
    public string? OwnerProcessName() => OwnerProcess.Resolve(this);

    /// <inheritdoc/>
    nint IProcessLookup.GetClipboardOwnerWindow() => (nint)PInvoke.GetClipboardOwner().Value;

    /// <inheritdoc/>
    uint IProcessLookup.GetWindowProcessId(nint window)
    {
        uint process = 0;
        var thread = PInvoke.GetWindowThreadProcessId(new HWND(window), &process);
        return thread == 0 ? 0 : process;
    }

    /// <inheritdoc/>
    string? IProcessLookup.GetProcessImagePath(uint processId)
    {
        // Query-limited rights are all this asks for. A protected or higher-privileged process refuses
        // them, and the answer is then "unknown", never a retry with more.
        var process = PInvoke.OpenProcess(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (process.IsNull)
        {
            return null;
        }

        try
        {
            Span<char> path = stackalloc char[MaxImagePathLength];
            var length = (uint)path.Length;
            fixed (char* buffer = path)
            {
                return PInvoke.QueryFullProcessImageName(process, PROCESS_NAME_FORMAT.PROCESS_NAME_WIN32, new PWSTR(buffer), &length) ? new string(path[..(int)length]) : null;
            }
        }
        finally
        {
            PInvoke.CloseHandle(process);
        }
    }

    private sealed class Session : IClipboardSession
    {
        public IReadOnlyList<ClipboardFormat> Formats()
        {
            List<ClipboardFormat> formats = [];
            var format = 0u;
            while (formats.Count < MaxFormats && (format = PInvoke.EnumClipboardFormats(format)) != 0)
            {
                formats.Add(new ClipboardFormat(format, format >= FirstRegisteredFormat ? NameOf(format) : null));
            }

            return formats;
        }

        public int? SizeOf(uint format)
        {
            var memory = DataOf(format);
            if (memory.IsNull)
            {
                return null;
            }

            var size = PInvoke.GlobalSize(memory);
            return size > int.MaxValue ? null : (int)size;
        }

        public byte[]? Read(uint format, int maxBytes)
        {
            var memory = DataOf(format);
            if (memory.IsNull || PInvoke.GlobalSize(memory) > (nuint)maxBytes)
            {
                return null;
            }

            var size = (int)PInvoke.GlobalSize(memory);
            var pointer = PInvoke.GlobalLock(memory);
            if (pointer is null)
            {
                return null;
            }

            try
            {
                return new ReadOnlySpan<byte>(pointer, size).ToArray();
            }
            finally
            {
                PInvoke.GlobalUnlock(memory);
            }
        }

        public bool Empty() => PInvoke.EmptyClipboard();

        public bool Set(uint format, ReadOnlySpan<byte> data)
        {
            // A block cannot be empty, and Windows owns it once the clipboard accepts it.
            var memory = PInvoke.GlobalAlloc(GLOBAL_ALLOC_FLAGS.GMEM_MOVEABLE, (nuint)Math.Max(data.Length, 1));
            if (memory.IsNull)
            {
                return false;
            }

            var pointer = PInvoke.GlobalLock(memory);
            if (pointer is null)
            {
                PInvoke.GlobalFree(memory);
                return false;
            }

            data.CopyTo(new Span<byte>(pointer, data.Length));
            PInvoke.GlobalUnlock(memory);

            if (PInvoke.SetClipboardData(format, new HANDLE(memory.Value)).IsNull)
            {
                PInvoke.GlobalFree(memory);
                return false;
            }

            return true;
        }

        public void Dispose() => PInvoke.CloseClipboard();

        // The data of a format as a memory block, or null when the format is missing or its owner could not produce it.
        private static HGLOBAL DataOf(uint format) => new(PInvoke.GetClipboardData(format).Value);

        private static string? NameOf(uint format)
        {
            Span<char> name = stackalloc char[MaxFormatNameLength];
            fixed (char* buffer = name)
            {
                var length = PInvoke.GetClipboardFormatName(format, buffer, name.Length);
                return length > 0 ? new string(name[..length]) : null;
            }
        }
    }
}
