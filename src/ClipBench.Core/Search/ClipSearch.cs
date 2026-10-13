using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using ClipBench.Core.Clips;

namespace ClipBench.Core.Search;

/// <summary>
/// Finds clips by substring and fuzzy match, ranked, over an in-memory index. The index holds the
/// searchable form of each clip's text and lives only as long as the clip does. Safe to use from many threads.
/// </summary>
public sealed class ClipSearch
{
    /// <summary>The number of results returned when the caller does not say.</summary>
    public const int DefaultLimit = 50;

    /// <summary>Only this many characters at the start of a clip's text are searched.</summary>
    public const int MaxIndexedCharacters = 20_000;

    /// <summary>A query longer than this is cut.</summary>
    public const int MaxQueryLength = 256;

    // Fuzzy matching looks for the query's letters in order inside a short stretch of the first
    // characters. Without those bounds almost any long text contains "pwd" as scattered letters.
    private const int FuzzyWindow = 256;
    private const int MaxFuzzyQueryLength = 32;

    // Tiers are far enough apart that the position bonus (0..999) never lifts a match into the next tier.
    private const int ExactScore = 5000;
    private const int PrefixScore = 4000;
    private const int WordPrefixScore = 3000;
    private const int SubstringScore = 2000;
    private const int FuzzyScore = 1000;

    private readonly ConditionalWeakTable<ClipContent, string> _index = new();

    /// <summary>Searches clips.</summary>
    /// <param name="clips">The clips to search, most recent first.</param>
    /// <param name="query">What the user typed.</param>
    /// <param name="limit">The most results to return.</param>
    /// <returns>
    /// The matches, best first; equal matches keep the order the clips were given in. An empty query
    /// returns the first <paramref name="limit"/> clips unranked.
    /// </returns>
    public ImmutableArray<SearchResult> Search(IEnumerable<Clip> clips, string query, int limit = DefaultLimit)
    {
        ArgumentNullException.ThrowIfNull(clips);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);

        var needle = SearchText.Normalize(query, MaxQueryLength);
        if (needle.Length == 0)
        {
            return [.. clips.Take(limit).Select(clip => new SearchResult(clip, 0))];
        }

        var matches = new List<(SearchResult Result, int Order)>();
        var order = 0;
        foreach (var clip in clips)
        {
            var text = TextOf(clip);
            var score = text is null ? 0 : Score(text, needle);
            if (score > 0)
            {
                matches.Add((new SearchResult(clip, score), order));
            }

            order++;
        }

        return
        [
            .. matches
                .OrderByDescending(match => match.Result.Score)
                .ThenBy(match => match.Order)
                .Take(limit)
                .Select(match => match.Result),
        ];
    }

    private static int Score(string text, string needle)
    {
        if (text.Length == needle.Length && text.Equals(needle, StringComparison.Ordinal))
        {
            return ExactScore;
        }

        if (text.StartsWith(needle, StringComparison.Ordinal))
        {
            return PrefixScore;
        }

        var first = text.IndexOf(needle, StringComparison.Ordinal);
        if (first < 0)
        {
            return FuzzyMatch(text, needle);
        }

        var wordStart = FindWordStart(text, needle, first);
        return wordStart >= 0 ? WordPrefixScore + EarlierIsBetter(wordStart) : SubstringScore + EarlierIsBetter(first);
    }

    private static int EarlierIsBetter(int position) => 999 - Math.Min(position, 999);

    // The first occurrence that begins a word: preceded by something that is not a letter or digit.
    private static int FindWordStart(string text, string needle, int firstOccurrence)
    {
        for (var index = firstOccurrence; index >= 0; index = text.IndexOf(needle, index + 1, StringComparison.Ordinal))
        {
            if (index == 0 || !char.IsLetterOrDigit(text[index - 1]))
            {
                return index;
            }
        }

        return -1;
    }

    private static int FuzzyMatch(string text, string needle)
    {
        if (needle.Length < 2 || needle.Length > MaxFuzzyQueryLength)
        {
            return 0;
        }

        var window = text.AsSpan(0, Math.Min(text.Length, FuzzyWindow));
        var maxSpan = Math.Max(needle.Length * 4, needle.Length + 8);
        var tightest = int.MaxValue;

        for (var start = 0; start < window.Length; start++)
        {
            if (window[start] != needle[0])
            {
                continue;
            }

            var matched = 1;
            var end = start;
            for (var index = start + 1; index < window.Length && index - start < maxSpan && matched < needle.Length; index++)
            {
                if (window[index] == needle[matched])
                {
                    matched++;
                    end = index;
                }
            }

            if (matched == needle.Length)
            {
                tightest = Math.Min(tightest, end - start + 1);
            }
        }

        // Fewer skipped letters is better. The skipped count is at most 3 * 32, so the bonus stays positive.
        return tightest == int.MaxValue ? 0 : FuzzyScore + 100 - (tightest - needle.Length);
    }

    private string? TextOf(Clip clip)
    {
        if (clip.Content.PlainText is null)
        {
            return null;
        }

        return _index.GetValue(clip.Content, content => SearchText.Normalize(content.PlainText!, MaxIndexedCharacters));
    }
}
