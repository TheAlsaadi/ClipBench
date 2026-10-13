using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Globalization;
using ClipBench.Core.Clips;

namespace ClipBench.Presentation.Rows;

/// <summary>How long ago a clip was copied, in words for a list row.</summary>
public static class RelativeTime
{
    /// <summary>Describes the time between a clip being copied and now.</summary>
    /// <param name="capturedAt">When the clip was copied.</param>
    /// <param name="now">The current time.</param>
    /// <returns>For example "Just now", "5 min ago", "3 h ago", "Yesterday", "4 d ago", or the date after a month.</returns>
    public static string Format(DateTimeOffset capturedAt, DateTimeOffset now)
    {
        var age = now - capturedAt;

        if (age < TimeSpan.FromSeconds(45))
        {
            return "Just now";
        }

        if (age < TimeSpan.FromHours(1))
        {
            var minutes = (int)Math.Round(age.TotalMinutes, MidpointRounding.AwayFromZero);
            return $"{Math.Min(minutes, 59)} min ago";
        }

        if (age < TimeSpan.FromDays(1))
        {
            return $"{(int)age.TotalHours} h ago";
        }

        if (age < TimeSpan.FromDays(2))
        {
            return "Yesterday";
        }

        return age < TimeSpan.FromDays(30)
            ? $"{(int)age.TotalDays} d ago"
            : capturedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
}

/// <summary>A short preview of a clip's text for a list row.</summary>
public static class PreviewText
{
    /// <summary>The most lines a row shows by default.</summary>
    public const int DefaultMaxLines = 2;

    /// <summary>The longest a shown line gets by default, in UTF-16 units.</summary>
    public const int DefaultMaxLineLength = 100;

    private const string Ellipsis = "…";

    // Only this many characters of a line are looked at, so a huge single-line text is not copied for every row.
    private const int LookAheadFactor = 4;

    /// <summary>What a row shows for a clip.</summary>
    /// <param name="content">The clip content.</param>
    /// <param name="maxLines">The most lines to show.</param>
    /// <param name="maxLineLength">The longest a line may be before it is cut with an ellipsis.</param>
    /// <returns>The preview, with lines separated by a line feed. Never null; never the whole of a long text.</returns>
    public static string For(ClipContent content, int maxLines = DefaultMaxLines, int maxLineLength = DefaultMaxLineLength)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLines, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLineLength, 2);

        return content switch
        {
            FilesContent files => FileNames(files, maxLines),
            ImageContent image => PngInfo.ReadSize(image.Png) is { } size ? $"Image {size.Width} × {size.Height}" : "Image",
            _ => TextLines(content.PlainText ?? string.Empty, maxLines, maxLineLength),
        };
    }

    private static string TextLines(string text, int maxLines, int maxLineLength)
    {
        var shown = new List<string>(maxLines);
        var more = false;

        foreach (var line in text.AsSpan().EnumerateLines())
        {
            var trimmed = line.Trim();
            if (trimmed.IsEmpty)
            {
                continue;
            }

            if (shown.Count == maxLines)
            {
                more = true;
                break;
            }

            shown.Add(Cut(trimmed, maxLineLength));
        }

        if (shown.Count == 0)
        {
            return "(empty)";
        }

        return more ? string.Join('\n', shown) + "\n" + Ellipsis : string.Join('\n', shown);
    }

    private static string Cut(ReadOnlySpan<char> line, int maxLineLength)
    {
        var lookAhead = maxLineLength * LookAheadFactor;
        var cutShort = line.Length > lookAhead;
        var head = cutShort ? line[..lookAhead] : line;
        var preview = new TextContent(head.ToString()).GetPreview(maxLineLength);

        return cutShort && !preview.EndsWith(Ellipsis, StringComparison.Ordinal) ? preview + Ellipsis : preview;
    }

    private static string FileNames(FilesContent files, int maxLines)
    {
        var names = files.Paths.Take(maxLines).Select(NameOf).ToList();
        var extra = files.Paths.Length - names.Count;

        return extra > 0
            ? string.Join('\n', names) + "\n+" + extra.ToString(CultureInfo.InvariantCulture) + " more"
            : string.Join('\n', names);
    }

    private static string NameOf(string path)
    {
        var trimmed = path.TrimEnd('\\', '/');
        var start = trimmed.AsSpan().LastIndexOfAny('\\', '/') + 1;
        return start < trimmed.Length ? trimmed[start..] : path;
    }
}

/// <summary>A readable name for the program a clip was copied from.</summary>
public static class SourceName
{
    /// <summary>Turns an executable name into a name to show.</summary>
    /// <param name="sourceApp">The executable name, such as "notepad.exe", or null.</param>
    /// <returns>For example "Notepad", or null when the source is unknown.</returns>
    public static string? Friendly(string? sourceApp)
    {
        if (string.IsNullOrWhiteSpace(sourceApp))
        {
            return null;
        }

        var name = sourceApp.Trim();
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4].TrimEnd();
        }

        if (name.Length == 0)
        {
            return null;
        }

        return name.Any(char.IsUpper) ? name : char.ToUpperInvariant(name[0]) + name[1..];
    }
}

/// <summary>Reads the size of a PNG picture without decoding it.</summary>
public static class PngInfo
{
    // Wider than any screen or camera, and far below the point where a size could overflow a pixel count.
    private const int MaxDimension = 1_000_000;

    private const int HeaderLength = 24;

    private static ReadOnlySpan<byte> Signature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Reads the width and height from the header.</summary>
    /// <param name="png">The PNG data.</param>
    /// <returns>The size, or null when the data does not start with a valid PNG header.</returns>
    public static (int Width, int Height)? ReadSize(ImmutableArray<byte> png)
    {
        var bytes = png.AsSpan();
        if (bytes.Length < HeaderLength
            || !bytes[..8].SequenceEqual(Signature)
            || BinaryPrimitives.ReadUInt32BigEndian(bytes[8..]) != 13
            || !bytes.Slice(12, 4).SequenceEqual("IHDR"u8))
        {
            return null;
        }

        var width = BinaryPrimitives.ReadUInt32BigEndian(bytes[16..]);
        var height = BinaryPrimitives.ReadUInt32BigEndian(bytes[20..]);

        return width is >= 1 and <= MaxDimension && height is >= 1 and <= MaxDimension
            ? ((int)width, (int)height)
            : null;
    }
}
