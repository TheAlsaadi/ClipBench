using System.Globalization;
using System.Text;

namespace ClipBench.Core.Clips;

/// <summary>What a clip holds. Immutable, with value equality.</summary>
public abstract record ClipContent
{
    /// <summary>The default maximum length of a preview, in UTF-16 units.</summary>
    public const int DefaultPreviewLength = 200;

    private const string Ellipsis = "\u2026";

    private protected ClipContent()
    {
    }

    /// <summary>The kind of data.</summary>
    public abstract ClipFormat Format { get; }

    /// <summary>The size of the data in bytes (text counts as UTF-8). Used for size caps.</summary>
    public abstract long SizeInBytes { get; }

    /// <summary>The text form of the content, or null when it has none (an image).</summary>
    public abstract string? PlainText { get; }

    /// <summary>
    /// A short single-line text for lists. Line breaks and runs of whitespace become one space, and
    /// the text is cut on a character boundary: a surrogate pair, a letter with its combining marks
    /// or a joined emoji sequence is never split. A cut ends with an ellipsis.
    /// </summary>
    /// <param name="maxLength">The most UTF-16 units the preview may have, ellipsis included.</param>
    /// <returns>The preview, or an empty string when the content has no text.</returns>
    public string GetPreview(int maxLength = DefaultPreviewLength)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLength, 1);

        var text = PlainText;
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var whole = Collapse(text, maxLength, out var truncated);
        if (!truncated)
        {
            return whole;
        }

        // Only the first pass knew the text was too long. Take less to leave room for the ellipsis.
        return Collapse(text, maxLength - 1, out _) + Ellipsis;
    }

    /// <summary>Printing never includes the content: clips end up in logs by accident.</summary>
    /// <returns>The kind and size of the content.</returns>
    public sealed override string ToString() => $"{Format} content ({SizeInBytes} bytes)";

    private static string Collapse(string text, int budget, out bool truncated)
    {
        var result = new StringBuilder(Math.Min(budget, text.Length));
        var pendingSpace = false;
        var remaining = text.AsSpan();
        truncated = false;

        while (!remaining.IsEmpty)
        {
            var length = StringInfo.GetNextTextElementLength(remaining);
            var element = remaining[..length];
            remaining = remaining[length..];

            if (IsWhiteSpace(element))
            {
                pendingSpace = result.Length > 0;
                continue;
            }

            if (result.Length + element.Length + (pendingSpace ? 1 : 0) > budget)
            {
                truncated = true;
                break;
            }

            if (pendingSpace)
            {
                result.Append(' ');
                pendingSpace = false;
            }

            // A lone surrogate is malformed UTF-16 and would not survive encoding. Replace it.
            if (element.Length == 1 && char.IsSurrogate(element[0]))
            {
                result.Append('\uFFFD');
            }
            else
            {
                result.Append(element);
            }
        }

        return result.ToString();
    }

    private static bool IsWhiteSpace(ReadOnlySpan<char> element)
    {
        foreach (var character in element)
        {
            if (!char.IsWhiteSpace(character))
            {
                return false;
            }
        }

        return true;
    }
}
