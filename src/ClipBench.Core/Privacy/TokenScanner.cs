namespace ClipBench.Core.Privacy;

/// <summary>
/// Finds long random-looking tokens such as API keys. A token is a run of 32 to 256 characters from the base64
/// alphabets that has both letters and digits and a high Shannon entropy. Hashes (all hex digits), UUIDs and
/// paths are excluded, and the entropy bar is set above what English words and identifiers reach, so a long class
/// name or a URL slug is not flagged. Linear in the text length.
/// </summary>
internal static class TokenScanner
{
    private const int MinLength = 32;
    private const int MaxLength = 256;
    private const int MaxSlashes = 3;
    private const double MinEntropyBitsPerCharacter = 4.3;

    public static bool Contains(ReadOnlySpan<char> text)
    {
        var index = 0;
        while (index < text.Length)
        {
            if (!IsTokenCharacter(text[index]))
            {
                index++;
                continue;
            }

            var start = index;
            while (index < text.Length && IsTokenCharacter(text[index]))
            {
                index++;
            }

            var token = text[start..index];
            if (token.Length is >= MinLength and <= MaxLength && LooksLikeSecret(token))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsTokenCharacter(char c) => Ascii.IsLetterOrDigit(c) || c is '_' or '-' or '+' or '/' or '=';

    private static bool LooksLikeSecret(ReadOnlySpan<char> token)
    {
        Span<int> counts = stackalloc int[128];
        var hasDigit = false;
        var hasLetter = false;
        var allHex = true;
        var slashes = 0;

        foreach (var c in token)
        {
            counts[c]++;
            hasDigit |= Ascii.IsDigit(c);
            hasLetter |= Ascii.IsLetter(c);
            allHex &= Ascii.IsHexDigit(c);
            if (c == '/')
            {
                slashes++;
            }
        }

        if (!hasDigit || !hasLetter || allHex || slashes > MaxSlashes || IsUuid(token))
        {
            return false;
        }

        return Entropy(counts, token.Length) >= MinEntropyBitsPerCharacter;
    }

    private static bool IsUuid(ReadOnlySpan<char> token)
    {
        if (token.Length != 36)
        {
            return false;
        }

        for (var i = 0; i < token.Length; i++)
        {
            var isDash = i is 8 or 13 or 18 or 23;
            if (isDash ? token[i] != '-' : !Ascii.IsHexDigit(token[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static double Entropy(ReadOnlySpan<int> counts, int length)
    {
        var entropy = 0.0;
        foreach (var count in counts)
        {
            if (count > 0)
            {
                var probability = (double)count / length;
                entropy -= probability * Math.Log2(probability);
            }
        }

        return entropy;
    }
}
