using System.Text;
using ClipBench.Core.Clips;

namespace ClipBench.Core.Tests.Clips;

public class ClipPreviewTests
{
    // Man, woman, girl and boy joined by zero width joiners: eleven UTF-16 units, one visible character.
    private const string Family = "\U0001F468\u200D\U0001F469\u200D\U0001F467\u200D\U0001F466";

    private static string Preview(string text, int maxLength) => new TextContent(text).GetPreview(maxLength);

    [Fact]
    public void Short_text_is_returned_unchanged()
    {
        Preview("hello world", 50).ShouldBe("hello world");
    }

    [Fact]
    public void Line_breaks_tabs_and_runs_of_spaces_collapse_to_one_space_and_the_ends_are_trimmed()
    {
        Preview("  first line\r\n\tsecond   line\n\n third  ", 100).ShouldBe("first line second line third");
    }

    [Fact]
    public void Text_that_is_only_whitespace_has_an_empty_preview()
    {
        Preview(" \r\n\t ", 10).ShouldBeEmpty();
        Preview("", 10).ShouldBeEmpty();
    }

    [Fact]
    public void Text_that_fits_exactly_is_not_truncated()
    {
        Preview("abcde", 5).ShouldBe("abcde");
    }

    [Fact]
    public void Long_text_is_truncated_with_an_ellipsis_and_never_exceeds_the_limit()
    {
        var preview = Preview("abcdefghij", 5);

        preview.ShouldBe("abcd\u2026");
        preview.Length.ShouldBe(5);
    }

    [Fact]
    public void The_ellipsis_never_follows_a_space()
    {
        Preview("abcd efgh", 6).ShouldBe("abcd\u2026");
    }

    [Fact]
    public void A_limit_of_one_gives_the_ellipsis_alone_when_the_text_is_longer()
    {
        Preview("ab", 1).ShouldBe("\u2026");
        Preview("a", 1).ShouldBe("a");
    }

    [Fact]
    public void A_surrogate_pair_is_never_split()
    {
        // Each emoji is two UTF-16 units. A limit of 4 leaves room for 3 units plus the ellipsis.
        var preview = Preview("a\U0001F600\U0001F600\U0001F600", 4);

        preview.ShouldBe("a\U0001F600\u2026");
        preview.EnumerateRunes().ShouldAllBe(rune => rune != Rune.ReplacementChar);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void Truncating_emoji_at_any_limit_leaves_only_whole_characters(int limit)
    {
        var preview = Preview(string.Concat(Enumerable.Repeat("\U0001F600", 6)), limit);

        preview.Length.ShouldBeLessThanOrEqualTo(limit);
        preview.EnumerateRunes().ShouldAllBe(rune => rune != Rune.ReplacementChar);
    }

    [Fact]
    public void A_character_with_combining_marks_is_never_split()
    {
        // "e" followed by a combining acute accent is one visible character of two UTF-16 units.
        // A limit of 4 leaves room for 3 units before the ellipsis, which fits one such character.
        var preview = Preview("e\u0301e\u0301e\u0301e\u0301", 4);

        preview.ShouldBe("e\u0301\u2026");
    }

    [Fact]
    public void Arabic_letters_keep_their_diacritics_when_truncated()
    {
        // Every letter carries a mark, so each visible character is two UTF-16 units:
        // meem+fatha, reh+sukun, hah+fatha, beh+fathatan, then alef. A limit of 6 leaves room for 5 units.
        const string text = "\u0645\u064E\u0631\u0652\u062D\u064E\u0628\u064B\u0627 \u0628\u0627\u0644\u0639\u0627\u0644\u0645";

        var preview = Preview(text, 6);

        preview.ShouldBe("\u0645\u064E\u0631\u0652\u2026");
        preview.EnumerateRunes().ShouldAllBe(rune => rune != Rune.ReplacementChar);
    }

    [Fact]
    public void A_joined_emoji_sequence_is_never_cut_in_the_middle()
    {
        Family.Length.ShouldBe(11);

        Preview(Family + Family, 5).ShouldBe("\u2026");
        Preview(Family + Family, 12).ShouldBe(Family + "\u2026");
        Preview(Family, Family.Length).ShouldBe(Family);
    }

    [Fact]
    public void A_lone_surrogate_in_the_text_becomes_the_replacement_character()
    {
        // A lone surrogate is malformed UTF-16. It must not reach the encrypted preview as is.
        Preview("ab\uD83Dcd", 10).ShouldBe("ab\uFFFDcd");
    }

    [Fact]
    public void A_huge_text_is_cut_without_reading_all_of_it_into_the_preview()
    {
        var preview = Preview(new string('x', 5_000_000), 20);

        preview.ShouldBe(new string('x', 19) + "\u2026");
    }

    [Fact]
    public void An_image_has_no_text_preview()
    {
        new ImageContent(new byte[] { 1, 2, 3 }).GetPreview(10).ShouldBeEmpty();
    }

    [Fact]
    public void A_file_list_previews_as_its_paths_on_one_line()
    {
        new FilesContent([@"C:\a.txt", @"C:\b.txt"]).GetPreview(100).ShouldBe(@"C:\a.txt C:\b.txt");
    }

    [Fact]
    public void Rich_text_previews_as_its_plain_text_alternative()
    {
        new HtmlContent("<p>hello</p>", "hello").GetPreview(10).ShouldBe("hello");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_limit_below_one_is_a_bug(int limit)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new TextContent("x").GetPreview(limit));
    }

    [Fact]
    public void The_default_limit_keeps_previews_short()
    {
        new TextContent(new string('y', 1000)).GetPreview().Length.ShouldBe(ClipContent.DefaultPreviewLength);
    }
}
