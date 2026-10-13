using System.Collections.Immutable;
using ClipBench.Core.Clips;

namespace ClipBench.Core.Capture;

/// <summary>
/// What is known about a clipboard change before its content is read: enough to decide whether
/// to record it without ever touching the data.
/// </summary>
public sealed record CaptureRequest
{
    /// <summary>The kind of content that would be read.</summary>
    public required ClipFormat Format { get; init; }

    /// <summary>The size in bytes of the content that would be read. Not negative.</summary>
    public required long SizeInBytes
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            field = value;
        }
    }

    /// <summary>The names of every format on the clipboard, which is how privacy markers show up.</summary>
    public ImmutableArray<string> FormatNames { get; init; } = [];

    /// <summary>The executable that owns the clipboard content, when known.</summary>
    public string? SourceProcess { get; init; }

    /// <summary>The value of the CanIncludeInClipboardHistory format, when the clipboard has it and it could be read.</summary>
    public int? CanIncludeInClipboardHistory { get; init; }

    /// <summary>True when ClipBench itself put this content on the clipboard.</summary>
    public bool IsOwnWrite => HasFormat(ClipboardFormatNames.OwnWrite);

    /// <summary>Whether the clipboard carries a format with this name. Format names are not case sensitive.</summary>
    /// <param name="name">The format name.</param>
    /// <returns>True when the format is present.</returns>
    public bool HasFormat(string name) =>
        !FormatNames.IsDefaultOrEmpty && FormatNames.Contains(name, StringComparer.OrdinalIgnoreCase);
}
