using System.Buffers.Binary;
using System.Collections.Immutable;
using ClipBench.Core.Clips;
using ClipBench.Presentation.Rows;

namespace ClipBench.Presentation.Tests.Rows;

public class RowTextTests
{
    private static readonly DateTimeOffset _now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    // --- relative time ---

    [Theory]
    [InlineData(0, "Just now")]
    [InlineData(44, "Just now")]
    [InlineData(45, "1 min ago")]
    [InlineData(89, "1 min ago")]
    [InlineData(90, "2 min ago")]
    [InlineData(59 * 60, "59 min ago")]
    [InlineData(59 * 60 + 40, "59 min ago")]
    [InlineData(60 * 60, "1 h ago")]
    [InlineData(5 * 3600, "5 h ago")]
    [InlineData(23 * 3600 + 59 * 60, "23 h ago")]
    [InlineData(24 * 3600, "Yesterday")]
    [InlineData(47 * 3600, "Yesterday")]
    [InlineData(48 * 3600, "2 d ago")]
    [InlineData(29 * 86400, "29 d ago")]
    public void Time_since_a_copy_is_told_in_the_largest_fitting_unit(int secondsAgo, string expected)
    {
        RelativeTime.Format(_now.AddSeconds(-secondsAgo), _now).ShouldBe(expected);
    }

    [Fact]
    public void The_thirtieth_day_is_the_first_one_told_by_its_date()
    {
        RelativeTime.Format(_now.AddDays(-30), _now).ShouldBe("2026-09-14");
    }

    [Fact]
    public void A_copy_older_than_a_month_is_told_by_its_date()
    {
        RelativeTime.Format(new DateTimeOffset(2026, 8, 1, 8, 30, 0, TimeSpan.Zero), _now).ShouldBe("2026-08-01");
    }

    [Fact]
    public void A_copy_dated_after_now_counts_as_just_now()
    {
        RelativeTime.Format(_now.AddMinutes(5), _now).ShouldBe("Just now");
    }

    // --- text previews ---

    [Fact]
    public void A_short_text_is_shown_as_it_is()
    {
        PreviewText.For(new TextContent("Meeting notes")).ShouldBe("Meeting notes");
    }

    [Fact]
    public void Only_the_first_lines_are_shown_and_the_rest_is_marked()
    {
        var text = new TextContent("one\r\ntwo\nthree\rfour");

        PreviewText.For(text, maxLines: 2).ShouldBe("one\ntwo\n\u2026");
    }

    [Fact]
    public void Exactly_as_many_lines_as_allowed_have_no_marker()
    {
        PreviewText.For(new TextContent("one\ntwo"), maxLines: 2).ShouldBe("one\ntwo");
    }

    [Fact]
    public void Blank_lines_and_edge_whitespace_are_skipped()
    {
        PreviewText.For(new TextContent("\n\n   first   \n\n  second  \n\n"), maxLines: 3).ShouldBe("first\nsecond");
    }

    [Fact]
    public void A_long_line_is_cut_with_an_ellipsis_without_splitting_a_character()
    {
        var text = new TextContent(new string('a', 20) + "\uD83D\uDE00\uD83D\uDE00");

        var shown = PreviewText.For(text, maxLines: 1, maxLineLength: 21);

        shown.ShouldBe(new string('a', 20) + "\u2026");
    }

    [Fact]
    public void A_line_that_is_cut_before_it_is_measured_still_ends_with_an_ellipsis()
    {
        // Only the start of a very long line is looked at. Here that start is "a" and a gap, which reads as just "a".
        var text = new TextContent("a" + new string(' ', 100) + "bcd");

        PreviewText.For(text, maxLines: 1, maxLineLength: 10).ShouldBe("a…");
    }

    [Fact]
    public void A_text_with_nothing_visible_is_shown_as_empty()
    {
        PreviewText.For(new TextContent(" \r\n\t ")).ShouldBe("(empty)");
        PreviewText.For(new TextContent(string.Empty)).ShouldBe("(empty)");
    }

    [Fact]
    public void Html_and_rich_text_show_their_plain_text()
    {
        PreviewText.For(new HtmlContent("<b>bold</b>", "bold")).ShouldBe("bold");
        PreviewText.For(new RtfContent("{\\rtf1 rich}", "rich")).ShouldBe("rich");
    }

    [Fact]
    public void A_file_list_shows_file_names_not_folders()
    {
        var files = new FilesContent([@"C:\Users\someone\Secret Project\plan.docx", "/home/someone/budget.xlsx"]);

        PreviewText.For(files).ShouldBe("plan.docx\nbudget.xlsx");
    }

    [Fact]
    public void A_folder_path_with_a_trailing_separator_shows_the_folder_name()
    {
        PreviewText.For(new FilesContent([@"C:\Users\someone\Projects\", "/home/someone/photos/"])).ShouldBe("Projects\nphotos");
    }

    [Fact]
    public void A_long_file_list_says_how_many_more_there_are()
    {
        var files = new FilesContent([@"C:\a.txt", @"C:\b.txt", @"C:\c.txt", @"C:\d.txt"]);

        PreviewText.For(files, maxLines: 2).ShouldBe("a.txt\nb.txt\n+2 more");
    }

    [Fact]
    public void An_image_with_a_readable_header_shows_its_size()
    {
        PreviewText.For(new ImageContent(Png(1280, 720).AsSpan())).ShouldBe("Image 1280 \u00D7 720");
    }

    [Fact]
    public void An_image_without_a_readable_header_is_just_an_image()
    {
        PreviewText.For(new ImageContent(new byte[] { 1, 2, 3 })).ShouldBe("Image");
    }

    // --- source names ---

    [Theory]
    [InlineData("notepad.exe", "Notepad")]
    [InlineData("chrome.EXE", "Chrome")]
    [InlineData("WINWORD.EXE", "WINWORD")]
    [InlineData("VS Code", "VS Code")]
    [InlineData("code", "Code")]
    public void An_executable_name_becomes_a_name_to_show(string source, string expected)
    {
        SourceName.Friendly(source).ShouldBe(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(".exe")]
    public void An_unknown_source_has_no_name(string? source)
    {
        SourceName.Friendly(source).ShouldBeNull();
    }

    // --- PNG size ---

    [Fact]
    public void The_size_is_read_from_the_header()
    {
        PngInfo.ReadSize(Png(1920, 1080)).ShouldBe((1920, 1080));
    }

    [Fact]
    public void Data_that_is_not_a_png_header_has_no_size()
    {
        PngInfo.ReadSize([]).ShouldBeNull();
        PngInfo.ReadSize([.. new byte[40]]).ShouldBeNull();
        PngInfo.ReadSize([.. Png(10, 10).Take(20)]).ShouldBeNull();
    }

    [Fact]
    public void A_header_of_exactly_the_needed_length_is_enough_and_the_largest_size_is_accepted()
    {
        PngInfo.ReadSize([.. Png(7, 9).Take(24)]).ShouldBe((7, 9));
        PngInfo.ReadSize(Png(1_000_000, 1_000_000)).ShouldBe((1_000_000, 1_000_000));
        PngInfo.ReadSize(Png(1_000_001, 1)).ShouldBeNull();
        PngInfo.ReadSize(Png(1, 1_000_001)).ShouldBeNull();
    }

    [Fact]
    public void A_header_that_claims_no_pixels_or_an_absurd_size_has_no_size()
    {
        PngInfo.ReadSize(Png(0, 10)).ShouldBeNull();
        PngInfo.ReadSize(Png(10, 0)).ShouldBeNull();
        PngInfo.ReadSize(Png(int.MaxValue, 10)).ShouldBeNull();
    }

    // A PNG signature and an IHDR chunk, enough for a size. The rest of a real file is not needed.
    private static ImmutableArray<byte> Png(int width, int height)
    {
        var bytes = new byte[33];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8), 13);
        "IHDR"u8.CopyTo(bytes.AsSpan(12));
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16), (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20), (uint)height);
        return [.. bytes];
    }
}
