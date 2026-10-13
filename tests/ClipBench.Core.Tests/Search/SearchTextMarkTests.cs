using ClipBench.Core.Search;
using ClipBench.Core.Tests.Support;

namespace ClipBench.Core.Tests.Search;

/// <summary>
/// Pins down exactly which invisible marks search ignores, and that the characters next to those ranges
/// are kept, so the ranges cannot quietly grow or shrink.
/// </summary>
public class SearchTextMarkTests
{
    private readonly ClipSearch _search = new();

    private string[] Find(string query, string clipText) =>
        [.. _search.Search([TestClips.Text(clipText)], query).Select(result => result.Clip.Content.PlainText!)];

    [Theory]
    [InlineData("\u061C")]      // Arabic letter mark
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
    [InlineData("\uFE20")]      // combining ligature left half, start of the range
    [InlineData("\uFE2F")]      // combining half mark, end of the range
    [InlineData("\uFEFF")]      // zero width no-break space
    public void Invisible_marks_do_not_stop_a_match(string mark)
    {
        var withMark = "he" + mark + "llo";

        Find("hello", withMark).ShouldBe([withMark]);
        Find(withMark, "hello").ShouldBe(["hello"]);
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
    [InlineData("\uFE30")]      // presentation form, just above the half marks
    public void Characters_next_to_the_dropped_ranges_are_kept(string letter)
    {
        // If the character were wrongly dropped, the query "x?y" would collapse to "xy" and match it.
        var query = "x" + letter + "y";

        Find(query, "xy").ShouldBeEmpty();
        Find(query, query).Length.ShouldBe(1);
    }
}
