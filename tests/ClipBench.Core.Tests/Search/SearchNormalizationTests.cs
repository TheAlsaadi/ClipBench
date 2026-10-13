using ClipBench.Core.Search;
using ClipBench.Core.Tests.Support;

namespace ClipBench.Core.Tests.Search;

/// <summary>
/// Text and query that differ only in how they are written must match as an exact match, not merely as a
/// fuzzy one. Fuzzy matching would find many of these anyway, so these tests compare scores, which tell the tiers apart.
/// </summary>
public class SearchNormalizationTests
{
    private readonly ClipSearch _search = new();

    private int ScoreOf(string query, string clipText)
    {
        var results = _search.Search([TestClips.Text(clipText)], query);
        return results.IsEmpty ? 0 : results[0].Score;
    }

    [Theory]
    // Arabic vowel marks, in either direction
    [InlineData("\u0645\u0631\u062D\u0628\u0627", "\u0645\u064E\u0631\u0652\u062D\u064E\u0628\u064B\u0627")]
    [InlineData("\u0645\u064E\u0631\u0652\u062D\u064E\u0628\u064B\u0627", "\u0645\u0631\u062D\u0628\u0627")]
    // Arabic tatweel
    [InlineData("\u0645\u0631\u062D\u0628\u0627", "\u0645\u0631\u062D\u0628\u0640\u0640\u0627")]
    // Alef with hamza above, hamza below and madda match the plain alef
    [InlineData("\u0627\u062D\u0645\u062F", "\u0623\u062D\u0645\u062F")]
    [InlineData("\u0627\u062D\u0645\u062F", "\u0625\u062D\u0645\u062F")]
    [InlineData("\u0627\u062D\u0645\u062F", "\u0622\u062D\u0645\u062F")]
    // Digits of other scripts
    [InlineData("123", "\u0661\u0662\u0663")]
    [InlineData("\u0661\u0662\u0663", "123")]
    [InlineData("123", "\u06F1\u06F2\u06F3")]
    // Hebrew points
    [InlineData("\u05E9\u05DC\u05D5\u05DD", "\u05E9\u05B8\u05C1\u05DC\u05D5\u05B9\u05DD")]
    // Latin accents, case and the Turkish dotted capital I
    [InlineData("cafe", "Caf\u00E9")]
    [InlineData("caf\u00E9", "cafe")]
    [InlineData("ecole", "\u00C9COLE")]
    [InlineData("istanbul", "\u0130stanbul")]
    [InlineData("hello", "HELLO")]
    // The emoji presentation selector
    [InlineData("\u2764", "\u2764\uFE0F")]
    // Compatibility forms: a ligature, full width letters and the Arabic ligature for the word Allah
    [InlineData("fine", "\uFB01ne")]
    [InlineData("abc", "\uFF21\uFF22\uFF23")]
    [InlineData("\u0627\u0644\u0644\u0647", "\uFDF2")]
    // Different kinds of space
    [InlineData("a b", "a\u00A0b")]
    [InlineData("a b", "a\u2003\t b")]
    public void Text_and_query_that_differ_only_in_presentation_are_an_exact_match(string query, string clipText)
    {
        var exact = ScoreOf(query, query);

        exact.ShouldBeGreaterThan(0);
        ScoreOf(query, clipText).ShouldBe(exact);
    }

    [Theory]
    [InlineData("cafe", "caff")]
    [InlineData("\u0645\u0631\u062D\u0628\u0627", "\u0645\u0631\u062D\u0628")]
    [InlineData("123", "124")]
    [InlineData("\u05E9\u05DC\u05D5\u05DD", "\u05E9\u05DC\u05D5")]
    public void Different_text_is_not_made_equal_by_the_normalisation(string query, string clipText)
    {
        ScoreOf(query, clipText).ShouldBeLessThan(ScoreOf(query, query));
    }
}
