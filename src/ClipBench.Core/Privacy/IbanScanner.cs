namespace ClipBench.Core.Privacy;

/// <summary>
/// Finds international bank account numbers: two letters, two check digits and up to 30 letters or digits,
/// written together or in groups of four, with a valid ISO 7064 mod 97 check. Linear in the text length.
/// </summary>
internal static class IbanScanner
{
    private const int MinLength = 15;
    private const int MaxLength = 34;

    public static bool Contains(ReadOnlySpan<char> text)
    {
        for (var start = 0; start + 4 <= text.Length; start++)
        {
            if (!Ascii.IsLetter(text[start])
                || !Ascii.IsLetter(text[start + 1])
                || !Ascii.IsDigit(text[start + 2])
                || !Ascii.IsDigit(text[start + 3])
                || (start > 0 && char.IsLetterOrDigit(text[start - 1])))
            {
                continue;
            }

            if (IsIbanAt(text, start))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsIbanAt(ReadOnlySpan<char> text, int start)
    {
        // Written without spaces.
        var end = start;
        while (end < text.Length && end - start <= MaxLength && Ascii.IsLetterOrDigit(text[end]))
        {
            end++;
        }

        var length = end - start;
        if (length is >= MinLength and <= MaxLength
            && (end == text.Length || !char.IsLetterOrDigit(text[end]))
            && HasValidChecksum(text.Slice(start, length)))
        {
            return true;
        }

        // Written in groups of four separated by spaces. Each group end is a candidate, because the text
        // after the number may itself look like another group ("... 7034 today").
        Span<char> compact = stackalloc char[MaxLength];
        var compactLength = 0;
        var index = start;
        while (true)
        {
            var group = 0;
            while (index + group < text.Length && group < 4 && Ascii.IsLetterOrDigit(text[index + group]))
            {
                group++;
            }

            if (group == 0 || compactLength + group > MaxLength)
            {
                return false;
            }

            // A group that runs on into more letters or digits was not a group of four.
            if (index + group < text.Length && char.IsLetterOrDigit(text[index + group]))
            {
                return false;
            }

            text.Slice(index, group).CopyTo(compact[compactLength..]);
            compactLength += group;
            index += group;

            if (compactLength >= MinLength && HasValidChecksum(compact[..compactLength]))
            {
                return true;
            }

            var continues = group == 4 && index + 1 < text.Length && text[index] == ' ' && Ascii.IsLetterOrDigit(text[index + 1]);
            if (!continues)
            {
                return false;
            }

            index++;
        }
    }

    private static bool HasValidChecksum(ReadOnlySpan<char> iban)
    {
        // The check digits are never 00, 01 or 99.
        var checkDigits = ((iban[2] - '0') * 10) + (iban[3] - '0');
        if (checkDigits is < 2 or > 98)
        {
            return false;
        }

        // Move the first four characters to the end, read letters as 10..35, and the number mod 97 must be 1.
        var remainder = 0;
        for (var i = 0; i < iban.Length; i++)
        {
            var c = iban[(i + 4) % iban.Length];
            if (Ascii.IsDigit(c))
            {
                remainder = ((remainder * 10) + (c - '0')) % 97;
            }
            else
            {
                remainder = ((remainder * 100) + (char.ToUpperInvariant(c) - 'A' + 10)) % 97;
            }
        }

        return remainder == 1;
    }
}
