using ClipBench.Core.Search;
using ClipBench.Core.Tests.Support;

namespace ClipBench.Core.Tests.Search;

/// <summary>
/// Pins down exactly which invisible marks search ignores, and that the characters next to those ranges
/// are kept, so the ranges cannot quietly grow or shrink. Scores are compared because fuzzy matching would
/// otherwise find many of these cases even if the mark were not ignored.
/// </summary>
public class SearchTextMarkTests
{
    private readonly ClipSearch _search = new();

    private int ScoreOf(string query, string clipText)
    {
        var results = _search.Search([TestClips.Text(clipText)], query);
        return results.IsEmpty ? 0 : results[0].Score;
    }

    [Theory]
    [InlineData("\u0300")]      // combining grave accent, start of the accents
    [InlineData("\u036F")]      // combining latin small letter x, end of the accents
    [InlineData("\u061C")]      // Arabic letter mark
    [InlineData("\u064B")]      // Arabic fathatan, start of the vowel marks
    [InlineData("\u065F")]      // Arabic wavy hamza below, end of the vowel marks
    [InlineData("\u0670")]      // Arabic superscript alef
    [InlineData("\u06D6")]      // Quranic annotation mark, start of the range
    [InlineData("\u06ED")]      // Quranic annotation mark, end of the range
    [InlineData("\u0591")]      // Hebrew accent, start of the range
    [InlineData("\u05C7")]      // Hebrew point, end of the range
    [InlineData("\u200B")]      // zero width space
    [InlineData("\u200C")]      // zero width non-joiner
    [InlineData("\u200D")]      // zero width joiner
    [InlineData("\u200E")]      // left-to-right mark
    [InlineData("\u200F")]      // right-to-left mark
    [InlineData("\u202A")]      // left-to-right embedding, start of the range
    [InlineData("\u202E")]      // right-to-left override, end of the range
    [InlineData("\u2066")]      // left-to-right isolate, start of the range
    [InlineData("\u2069")]      // pop directional isolate, end of the range
    [InlineData("\uFE00")]      // variation selector 1, start of the range
    [InlineData("\uFE0F")]      // variation selector 16, end of the range
    [InlineData("\uFE20")]      // combining ligature left half, start of the range
    [InlineData("\uFE2F")]      // combining half mark, end of the range
    [InlineData("\uFEFF")]      // zero width no-break space
    public void Invisible_marks_do_not_stop_an_exact_match(string mark)
    {
        var withMark = "he" + mark + "llo";
        var exact = ScoreOf("hello", "hello");

        ScoreOf("hello", withMark).ShouldBe(exact);
        ScoreOf(withMark, "hello").ShouldBe(exact);
    }

    [Fact]
    public void The_tatweel_is_dropped_but_the_letters_around_it_are_kept()
    {
        // U+0640 is the stretching stroke; U+0641 (feh) and U+063F (a letter) sit either side of it.
        var exact = ScoreOf("xy", "xy");

        ScoreOf("xy", "x\u0640y").ShouldBe(exact);
        ScoreOf("xy", "x\u0641y").ShouldBeLessThan(exact);
        ScoreOf("xy", "x\u063Fy").ShouldBeLessThan(exact);
    }

    [Theory]
    [InlineData("\u02FF")]      // just below the combining accents
    [InlineData("\u0370")]      // just above the combining accents
    [InlineData("\u064A")]      // Arabic yeh, just below the vowel marks
    [InlineData("\u0671")]      // Arabic alef wasla, just above the superscript alef
    [InlineData("\u06D5")]      // Arabic ae, just below the Quranic marks
    [InlineData("\u06EE")]      // Arabic dal with inverted v, just above the Quranic marks
    [InlineData("\u05D0")]      // Hebrew alef, just above the points
    [InlineData("\u2060")]      // word joiner is not one of the dropped marks
    [InlineData("\uFDFE")]      // just below the variation selectors
    [InlineData("\uFE30")]      // presentation form, just above the half marks
    public void Characters_next_to_the_dropped_ranges_are_kept(string letter)
    {
        // If the character were wrongly dropped, the query "x?y" would collapse to "xy" and match it exactly.
        var query = "x" + letter + "y";

        ScoreOf(query, "xy").ShouldBe(0);
        ScoreOf(query, query).ShouldBeGreaterThan(0);
    }
}
