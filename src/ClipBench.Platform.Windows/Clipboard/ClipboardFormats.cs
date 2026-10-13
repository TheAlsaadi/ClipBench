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
}

/// <summary>A format on the clipboard.</summary>
/// <param name="Id">The format number.</param>
/// <param name="Name">The name of a registered format, or null for a standard one.</param>
internal readonly record struct ClipboardFormat(uint Id, string? Name);
