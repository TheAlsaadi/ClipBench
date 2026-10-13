namespace ClipBench.Platform.Windows.Clipboard;

/// <summary>The clipboard formats ClipBench reads and writes.</summary>
internal static class ClipboardFormats
{
    /// <summary>CF_DIB: a device-independent bitmap.</summary>
    public const uint Dib = 8;

    /// <summary>CF_UNICODETEXT: UTF-16 text ending with a NUL.</summary>
    public const uint UnicodeText = 13;

    /// <summary>CF_HDROP: a list of file paths.</summary>
    public const uint Hdrop = 15;

    /// <summary>CF_DIBV5: a device-independent bitmap with a version 5 header, which can carry alpha.</summary>
    public const uint DibV5 = 17;

    /// <summary>The registered format that holds HTML with a header of offsets.</summary>
    public const string Html = "HTML Format";

    /// <summary>The registered format that holds Rich Text Format.</summary>
    public const string Rtf = "Rich Text Format";

    /// <summary>The registered format that holds a PNG file, which keeps alpha for programs that read it.</summary>
    public const string Png = "PNG";

    /// <summary>The data of ClipBench's own-write marker. The value does not matter: a format needs some data, and its presence is the signal.</summary>
    public static ReadOnlySpan<byte> OwnWriteMarkerData => [1, 0, 0, 0];

    /// <summary>
    /// Whether the data of a format is a block of global memory, which can be sized, locked and copied. The
    /// others hold a handle to a graphics object (CF_BITMAP, the metafile and palette formats and their display
    /// forms, the GDI range), no data at all (CF_OWNERDISPLAY) or something private to a program (the private
    /// range). Windows' global-memory functions must never be called on those: they treat the handle as a
    /// pointer and corrupt the heap.
    /// </summary>
    /// <param name="format">The format number.</param>
    /// <returns>True when the data is a block of memory.</returns>
    public static bool IsMemoryBlock(uint format) =>
        !(format is 2 or 3 or 9 or 14 or 0x0080 or 0x0082 or 0x0083 or 0x008E || format is >= 0x0200 and <= 0x03FF);
}

/// <summary>A format on the clipboard.</summary>
/// <param name="Id">The format number.</param>
/// <param name="Name">The name of a registered format, or null for a standard one.</param>
internal readonly record struct ClipboardFormat(uint Id, string? Name);
