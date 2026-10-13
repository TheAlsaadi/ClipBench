namespace ClipBench.Core.Clips;

/// <summary>The kind of data a clip holds.</summary>
public enum ClipFormat
{
    /// <summary>Plain text.</summary>
    Text,

    /// <summary>HTML, with a plain text alternative.</summary>
    Html,

    /// <summary>Rich Text Format, with a plain text alternative.</summary>
    Rtf,

    /// <summary>An image, stored as PNG bytes.</summary>
    Image,

    /// <summary>A list of file paths. Only the paths are stored, never the files.</summary>
    Files,
}
