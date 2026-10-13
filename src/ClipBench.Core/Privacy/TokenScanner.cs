namespace ClipBench.Core.Privacy;

/// <summary>
/// Finds long random-looking tokens such as API keys. A token is a run of 32 to 256 characters from the base64
/// alphabets that has both letters and digits, at most a few slashes, and a high Shannon entropy.
/// </summary>
/// <remarks>
/// The entropy bar of 4.3 bits per character does the real work. Hex strings (commit hashes, MD5, SHA-256)
/// and UUIDs cannot reach it, because 16 hex digits carry at most 4 bits each. English words, class names
/// and URL slugs stay below it too. Random base64 or base62 text of this length is around 4.7 or more.
/// Linear in the text length.
/// </remarks>
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
        var slashes = 0;

        foreach (var c in token)
        {
            counts[c]++;
            hasDigit |= Ascii.IsDigit(c);
            hasLetter |= Ascii.IsLetter(c);
            if (c == '/')
            {
                slashes++;
            }
        }

        // Several slashes mean a path or a URL, not a token. Digits and letters both rule out words and numbers.
        if (!hasDigit || !hasLetter || slashes > MaxSlashes)
        {
            return false;
        }

        return Entropy(counts, token.Length) >= MinEntropyBitsPerCharacter;
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
