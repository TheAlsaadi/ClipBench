namespace ClipBench.Core.Detection;

/// <summary>
/// Recognises one phone number: seven to fifteen digits, written with a leading plus or at least one separator (space, dash,
/// dot or a pair of brackets), so that a bare run of digits, a date, an IP address or a card number is not taken for one.
/// Hand-written and capped.
/// </summary>
public static class PhoneDetector
{
    /// <summary>The longest text that is looked at, after trimming.</summary>
    public const int MaxLength = 40;

    private const int MinDigits = 7;
    private const int MaxDigits = 15;

    /// <summary>Tells whether the text is exactly one phone number.</summary>
    /// <param name="text">The text.</param>
    /// <returns>True when it is.</returns>
    public static bool IsPhone(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var number = text.Trim();
        if (number.Length == 0 || number.Length > MaxLength)
        {
            return false;
        }

        var hasPlus = number[0] == '+';
        var digits = 0;
        var separators = 0;
        var open = 0;
        var close = 0;
        for (var i = hasPlus ? 1 : 0; i < number.Length; i++)
        {
            var character = number[i];
            if (char.IsAsciiDigit(character))
            {
                digits++;
            }
            else if (character is ' ' or '-' or '.')
            {
                separators++;
            }
            else if (character == '(')
            {
                open++;
                separators++;
            }
            else if (character == ')')
            {
                close++;
                separators++;
            }
            else
            {
                return false;
            }
        }

        if (digits is < MinDigits or > MaxDigits || open != close || open > 1 || (!hasPlus && separators == 0))
        {
            return false;
        }

        return !LooksLikeADate(number) && !LooksLikeAnAddress(number);
    }

    // 2026-11-05, 2026.11.05, 05.11.2026 and 11-05-2026: three groups of digits, one separator character.
    private static bool LooksLikeADate(string text)
    {
        var separator = text.Contains('-', StringComparison.Ordinal) ? '-' : '.';
        var groups = text.Split(separator);
        if (groups.Length != 3 || !groups.All(group => group.All(char.IsAsciiDigit)))
        {
            return false;
        }

        var shape = (groups[0].Length, groups[1].Length, groups[2].Length);
        return shape is (4, 2, 2) or (2, 2, 4);
    }

    // 192.168.1.1: four groups of one to three digits with dots.
    private static bool LooksLikeAnAddress(string text)
    {
        var groups = text.Split('.');
        return groups.Length == 4 && groups.All(group => group.Length is >= 1 and <= 3 && group.All(char.IsAsciiDigit));
    }
}
