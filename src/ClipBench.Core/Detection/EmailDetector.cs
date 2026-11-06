using System.Buffers;

namespace ClipBench.Core.Detection;

/// <summary>
/// Recognises one email address in the plain form people write: letters, digits and <c>. _ + -</c> before the @, and a
/// dotted domain after it. Hand-written and capped, so it is linear in the length of the text. It is deliberately
/// narrower than the standard, which allows forms nobody types.
/// </summary>
public static class EmailDetector
{
    /// <summary>The longest address, in characters (the standard's limit).</summary>
    public const int MaxLength = 254;

    /// <summary>The longest part before the @.</summary>
    public const int MaxLocalLength = 64;

    private static readonly SearchValues<char> _letters = SearchValues.Create("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ");

    /// <summary>Tells whether the text is exactly one email address.</summary>
    /// <param name="text">The text.</param>
    /// <returns>True when it is.</returns>
    public static bool IsEmail(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var address = text.Trim();
        if (address.Length == 0 || address.Length > MaxLength)
        {
            return false;
        }

        var at = address.IndexOf('@', StringComparison.Ordinal);
        if (at <= 0 || at != address.LastIndexOf('@') || at > MaxLocalLength)
        {
            return false;
        }

        return IsLocalPart(address.AsSpan(0, at)) && IsDomain(address.AsSpan(at + 1));
    }

    private static bool IsLocalPart(ReadOnlySpan<char> local)
    {
        if (local[0] == '.' || local[^1] == '.')
        {
            return false;
        }

        var previousDot = false;
        foreach (var character in local)
        {
            var dot = character == '.';
            if ((dot && previousDot) || !(char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '+' or '-'))
            {
                return false;
            }

            previousDot = dot;
        }

        return true;
    }

    private static bool IsDomain(ReadOnlySpan<char> domain)
    {
        var labels = 0;
        var last = default(ReadOnlySpan<char>);
        while (true)
        {
            var dot = domain.IndexOf('.');
            var label = dot < 0 ? domain : domain[..dot];
            if (!IsLabel(label))
            {
                return false;
            }

            labels++;
            last = label;
            if (dot < 0)
            {
                break;
            }

            domain = domain[(dot + 1)..];
        }

        // The last label is the top-level domain: letters only, at least two.
        return labels >= 2 && last.Length >= 2 && !last.ContainsAnyExcept(_letters);
    }

    private static bool IsLabel(ReadOnlySpan<char> label)
    {
        if (label.Length == 0 || label[0] == '-' || label[^1] == '-')
        {
            return false;
        }

        foreach (var character in label)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character != '-')
            {
                return false;
            }
        }

        return true;
    }
}
