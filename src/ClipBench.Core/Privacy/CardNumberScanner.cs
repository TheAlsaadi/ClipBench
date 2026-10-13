using System.Globalization;

namespace ClipBench.Core.Privacy;

/// <summary>
/// Finds payment card numbers: 13 to 19 digits, grouped like a card, with a valid Luhn check digit and the
/// prefix and length of a known card brand. The brand rule matters: one random number in ten passes the Luhn
/// check, so without it order numbers and timestamps would be flagged. Linear in the text length.
/// </summary>
internal static class CardNumberScanner
{
    private const int MinDigits = 13;
    private const int MaxDigits = 19;
    private const int MaxGroups = 8;

    public static bool Contains(ReadOnlySpan<char> text)
    {
        for (var start = 0; start < text.Length; start++)
        {
            // A number starts at a digit that is not glued to a letter or another digit.
            if (DigitValue(text[start]) < 0 || (start > 0 && char.IsLetterOrDigit(text[start - 1])))
            {
                continue;
            }

            if (IsCardAt(text, start))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsCardAt(ReadOnlySpan<char> text, int start)
    {
        Span<int> digits = stackalloc int[MaxDigits];
        Span<int> groups = stackalloc int[MaxGroups];
        var count = 0;
        var groupCount = 0;
        var groupLength = 0;
        var index = start;

        // Digits, with one space or hyphen allowed between groups of digits.
        while (index < text.Length)
        {
            var digit = DigitValue(text[index]);
            if (digit >= 0)
            {
                if (count == MaxDigits)
                {
                    return false;
                }

                digits[count++] = digit;
                groupLength++;
                index++;
            }
            else if (text[index] is ' ' or '-' && groupLength > 0 && index + 1 < text.Length && DigitValue(text[index + 1]) >= 0)
            {
                if (groupCount == MaxGroups - 1)
                {
                    return false;
                }

                groups[groupCount++] = groupLength;
                groupLength = 0;
                index++;
            }
            else
            {
                break;
            }
        }

        groups[groupCount++] = groupLength;

        if (index < text.Length && char.IsLetterOrDigit(text[index]))
        {
            return false;
        }

        var number = digits[..count];
        return count >= MinDigits && HasCardGrouping(groups[..groupCount]) && PassesLuhn(number) && IsKnownBrand(number);
    }

    // The digit's value for any script's decimal digits (so Arabic-Indic digits count), or -1.
    private static int DigitValue(char c) => char.IsDigit(c) ? (int)char.GetNumericValue(c) : -1;

    // Written without spaces, in groups of four with a shorter last group, or in the American Express and
    // Diners Club shapes 4-6-5 and 4-6-4. Anything else, such as a list of two-digit numbers, is not a card.
    private static bool HasCardGrouping(ReadOnlySpan<int> groups)
    {
        if (groups.Length == 1)
        {
            return true;
        }

        if (groups.Length == 3 && groups[0] == 4 && groups[1] == 6 && groups[2] is 4 or 5)
        {
            return true;
        }

        for (var i = 0; i < groups.Length - 1; i++)
        {
            if (groups[i] != 4)
            {
                return false;
            }
        }

        return groups[^1] is >= 1 and <= 4;
    }

    private static bool PassesLuhn(ReadOnlySpan<int> digits)
    {
        var sum = 0;
        for (var i = 0; i < digits.Length; i++)
        {
            var digit = digits[digits.Length - 1 - i];
            if (i % 2 == 1)
            {
                digit *= 2;
                if (digit > 9)
                {
                    digit -= 9;
                }
            }

            sum += digit;
        }

        return sum % 10 == 0;
    }

    private static bool IsKnownBrand(ReadOnlySpan<int> d)
    {
        var length = d.Length;
        var p2 = (d[0] * 10) + d[1];
        var p3 = (p2 * 10) + d[2];
        var p4 = (p3 * 10) + d[3];

        return (d[0] == 4 && length is 13 or 16 or 19)                                          // Visa
            || ((p2 is >= 51 and <= 55 || p4 is >= 2221 and <= 2720) && length == 16)           // Mastercard
            || (p2 is 34 or 37 && length == 15)                                                  // American Express
            || ((p4 == 6011 || p2 == 65 || p3 is >= 644 and <= 649) && length is >= 16 and <= 19) // Discover
            || (p4 is >= 3528 and <= 3589 && length is >= 16 and <= 19)                          // JCB
            || ((p3 is >= 300 and <= 305 || p2 is 36 or 38 or 39) && length is >= 14 and <= 19)  // Diners Club
            || (p2 == 62 && length is >= 16 and <= 19);                                          // UnionPay
    }
}
