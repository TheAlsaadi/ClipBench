using System.Text;

namespace ClipBench.Platform.Windows.Formats;

/// <summary>
/// A plain-text version of an HTML fragment, for the rare case where a program puts HTML on the
/// clipboard without a text alternative. It removes tags and decodes the common entities. It is not
/// an HTML parser: the result is only used to search and to show a preview, never to paste.
/// </summary>
internal static class HtmlText
{
    private const int LongestEntity = 12;

    private static readonly string[] _breakingTags =
    [
        "br", "p", "div", "li", "ul", "ol", "tr", "table", "hr", "blockquote", "pre", "h1", "h2", "h3", "h4", "h5", "h6",
    ];

    /// <summary>Removes the markup from an HTML fragment.</summary>
    /// <param name="html">The fragment.</param>
    /// <returns>The text. Block elements and line breaks become one line break; unfinished markup at the end is dropped.</returns>
    public static string Strip(string html)
    {
        ArgumentNullException.ThrowIfNull(html);

        var text = new StringBuilder(html.Length);
        var span = html.AsSpan();
        var index = 0;
        while (index < span.Length)
        {
            var remaining = span[index..];
            if (remaining[0] == '<' && remaining.Length > 1 && IsTagStart(remaining[1]))
            {
                // Each branch either consumes the whole tag or, when it is unfinished, the rest of the text.
                // Nothing is scanned twice, so the work stays linear.
                var length = remaining.StartsWith("<!--", StringComparison.Ordinal) ? CommentLength(remaining) : TagLength(remaining);
                if (length < 0)
                {
                    break;
                }

                if (IsBreakingTag(remaining[..length]))
                {
                    AppendLineBreak(text);
                }

                index += length;
            }
            else if (remaining[0] == '&' && TryDecodeEntity(remaining, out var decoded, out var consumed))
            {
                text.Append(decoded);
                index += consumed;
            }
            else
            {
                index += AppendCharacter(text, remaining);
            }
        }

        while (text.Length > 0 && text[^1] == '\n')
        {
            text.Length--;
        }

        return text.ToString();
    }

    // "<" starts a tag only before a letter, "/", "!" or "?", so "a < b" and "<3" stay text.
    private static bool IsTagStart(char next) => char.IsAsciiLetter(next) || next is '/' or '!' or '?';

    // Length of a tag up to and including ">", or -1 when there is none.
    private static int TagLength(ReadOnlySpan<char> tag)
    {
        var close = tag.IndexOf('>');
        return close < 0 ? -1 : close + 1;
    }

    private static int CommentLength(ReadOnlySpan<char> comment)
    {
        var close = comment[4..].IndexOf("-->", StringComparison.Ordinal);
        return close < 0 ? -1 : 4 + close + 3;
    }

    private static bool IsBreakingTag(ReadOnlySpan<char> tag)
    {
        var name = tag[1..];
        if (name.StartsWith("/", StringComparison.Ordinal))
        {
            name = name[1..];
        }

        var length = 0;
        while (length < name.Length && char.IsAsciiLetterOrDigit(name[length]))
        {
            length++;
        }

        foreach (var breaking in _breakingTags)
        {
            if (name[..length].Equals(breaking, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static void AppendLineBreak(StringBuilder text)
    {
        if (text.Length > 0 && text[^1] != '\n')
        {
            text.Append('\n');
        }
    }

    // Appends one character (or a surrogate pair) and returns how many it used. A lone surrogate is not valid
    // text, and storage would refuse it, so it becomes the replacement character.
    private static int AppendCharacter(StringBuilder text, ReadOnlySpan<char> remaining)
    {
        var first = remaining[0];
        if (char.IsHighSurrogate(first) && remaining.Length > 1 && char.IsLowSurrogate(remaining[1]))
        {
            text.Append(first).Append(remaining[1]);
            return 2;
        }

        text.Append(char.IsSurrogate(first) ? '\uFFFD' : first);
        return 1;
    }

    private static bool TryDecodeEntity(ReadOnlySpan<char> remaining, out string decoded, out int consumed)
    {
        decoded = string.Empty;
        consumed = 0;

        var close = remaining[..Math.Min(remaining.Length, LongestEntity)].IndexOf(';');
        if (close < 2)
        {
            return false;
        }

        var name = remaining[1..close];
        consumed = close + 1;
        if (name[0] == '#')
        {
            return TryDecodeNumber(name[1..], out decoded);
        }

        decoded = name switch
        {
            "amp" => "&",
            "lt" => "<",
            "gt" => ">",
            "quot" => "\"",
            "apos" => "'",
            "nbsp" => " ",
            _ => string.Empty,
        };
        return decoded.Length > 0;
    }

    private static bool TryDecodeNumber(ReadOnlySpan<char> digits, out string decoded)
    {
        decoded = string.Empty;
        var hex = digits.Length > 0 && digits[0] is 'x' or 'X';
        if (hex)
        {
            digits = digits[1..];
        }

        if (digits.IsEmpty || digits.Length > 7)
        {
            return false;
        }

        var value = 0;
        foreach (var digit in digits)
        {
            var part = hex ? ParseHex(digit) : (char.IsAsciiDigit(digit) ? digit - '0' : -1);
            if (part < 0)
            {
                return false;
            }

            value = (value * (hex ? 16 : 10)) + part;
        }

        // 0 is not text, and the surrogate range cannot be a character on its own.
        if (value is 0 or > 0x10FFFF or (>= 0xD800 and <= 0xDFFF))
        {
            return false;
        }

        decoded = char.ConvertFromUtf32(value);
        return true;
    }

    private static int ParseHex(char digit) =>
        char.IsAsciiDigit(digit) ? digit - '0' : char.IsAsciiHexDigit(digit) ? char.ToLowerInvariant(digit) - 'a' + 10 : -1;
}
