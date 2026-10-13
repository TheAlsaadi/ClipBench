namespace ClipBench.Platform.Windows.Clipboard;

/// <summary>
/// The part of Windows that ClipBench's clipboard code talks to, as a seam: the real one calls the Win32
/// clipboard functions, and tests use an in-memory stand-in. Nothing here knows about clips or privacy.
/// </summary>
internal interface IClipboardApi
{
    /// <summary>The system-wide clipboard sequence number. It grows with every change by any program.</summary>
    long SequenceNumber { get; }

    /// <summary>
    /// Tries once to open the clipboard. The clipboard is held by one program at a time, so this fails
    /// while another program has it open.
    /// </summary>
    /// <returns>The open clipboard, or null when another program has it.</returns>
    IClipboardSession? TryOpen();

    /// <summary>Finds or creates the number of a named format.</summary>
    /// <param name="name">The format name.</param>
    /// <returns>The format number, or 0 when Windows would not register it.</returns>
    uint RegisterFormat(string name);

    /// <summary>Whether a format is on the clipboard. It does not open the clipboard or fetch any data.</summary>
    /// <param name="format">The format number.</param>
    /// <returns>True when the format is present.</returns>
    bool IsFormatAvailable(uint format);

    /// <summary>The executable name of the program that owns the clipboard content.</summary>
    /// <returns>For example <c>chrome.exe</c>, or null when it cannot be found. Never a full path or a window title.</returns>
    string? OwnerProcessName();
}

/// <summary>The clipboard while ClipBench has it open. Disposing closes it. Other programs wait until then.</summary>
internal interface IClipboardSession : IDisposable
{
    /// <summary>The formats on the clipboard, with the names of registered ones.</summary>
    /// <returns>Every format present. No data is read.</returns>
    IReadOnlyList<ClipboardFormat> Formats();

    /// <summary>The size in bytes of a format's data.</summary>
    /// <param name="format">The format number.</param>
    /// <returns>The size, or null when the format is missing or its data cannot be had.</returns>
    int? SizeOf(uint format);

    /// <summary>Copies a format's data.</summary>
    /// <param name="format">The format number.</param>
    /// <param name="maxBytes">The most bytes to copy. Larger data is not copied.</param>
    /// <returns>The bytes, or null when the format is missing, unavailable or larger than the limit.</returns>
    byte[]? Read(uint format, int maxBytes);

    /// <summary>Empties the clipboard and takes ownership of it.</summary>
    /// <returns>True on success.</returns>
    bool Empty();

    /// <summary>Puts data on the clipboard under a format.</summary>
    /// <param name="format">The format number.</param>
    /// <param name="data">The bytes.</param>
    /// <returns>True on success.</returns>
    bool Set(uint format, ReadOnlySpan<byte> data);
}

/// <summary>The Windows calls that lead from the clipboard to the program that owns it.</summary>
internal interface IProcessLookup
{
    /// <summary>The window that owns the clipboard content.</summary>
    /// <returns>The window handle, or 0 when there is none.</returns>
    nint GetClipboardOwnerWindow();

    /// <summary>The process that owns a window.</summary>
    /// <param name="window">The window handle.</param>
    /// <returns>The process id, or 0 when the window is gone.</returns>
    uint GetWindowProcessId(nint window);

    /// <summary>The path of the program a process runs. Asking never needs more rights than querying limited information.</summary>
    /// <param name="processId">The process id.</param>
    /// <returns>The path, or null when Windows refuses (a protected or higher-privileged process) or the process is gone.</returns>
    string? GetProcessImagePath(uint processId);
}
