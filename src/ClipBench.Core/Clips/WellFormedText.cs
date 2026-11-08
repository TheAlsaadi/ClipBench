namespace ClipBench.Core.Clips;

/// <summary>
/// Puts the replacement character in place of half of a surrogate pair. Storage writes text as UTF-8, which has
/// no way to hold a lone surrogate and replaces it the same way, so doing it when content is made keeps the clip in
/// memory equal to the clip on disk: two texts that would be stored as the same bytes are also equal in memory.
/// </summary>
internal static class WellFormedText
{
    private const char ReplacementCharacter = (char)0xFFFD;

    /// <summary>Returns the text with every lone surrogate replaced, or the same instance when it has none.</summary>
    /// <param name="text">The text.</param>
    /// <returns>Well-formed UTF-16 text.</returns>
    public static string Repair(string text)
    {
        var first = IndexOfLoneSurrogate(text, 0);
        if (first < 0)
        {
            return text;
        }

        return string.Create(text.Length, (text, first), static (span, state) =>
        {
            var (source, at) = state;
            source.AsSpan().CopyTo(span);
            while (at >= 0)
            {
                span[at] = ReplacementCharacter;
                at = IndexOfLoneSurrogate(source, at + 1);
            }
        });
    }

    private static int IndexOfLoneSurrogate(string text, int start)
    {
        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];
            if (!char.IsSurrogate(c))
            {
                continue;
            }

            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                i++;
                continue;
            }

            return i;
        }

        return -1;
    }
}
