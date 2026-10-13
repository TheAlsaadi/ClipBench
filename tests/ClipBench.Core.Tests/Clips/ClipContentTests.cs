using System.Text;
using ClipBench.Core.Clips;

namespace ClipBench.Core.Tests.Clips;

public class ClipContentTests
{
    [Fact]
    public void Text_content_reports_its_format_and_plain_text()
    {
        var content = new TextContent("hello");

        content.Format.ShouldBe(ClipFormat.Text);
        content.PlainText.ShouldBe("hello");
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData("hello", 5)]
    [InlineData("héllo", 6)]
    [InlineData("مرحبا", 10)]
    [InlineData("😀", 4)]
    public void Text_size_is_the_number_of_UTF8_bytes(string text, long expected)
    {
        new TextContent(text).SizeInBytes.ShouldBe(expected);
    }

    [Fact]
    public void Text_content_with_no_text_is_a_bug()
    {
        Should.Throw<ArgumentNullException>(() => new TextContent(null!));
    }

    [Fact]
    public void Html_content_keeps_the_markup_and_exposes_the_plain_text_alternative()
    {
        var content = new HtmlContent("<b>bold</b>", "bold");

        content.Format.ShouldBe(ClipFormat.Html);
        content.Html.ShouldBe("<b>bold</b>");
        content.PlainText.ShouldBe("bold");
        content.SizeInBytes.ShouldBe(Encoding.UTF8.GetByteCount("<b>bold</b>") + Encoding.UTF8.GetByteCount("bold"));
    }

    [Fact]
    public void Rtf_content_keeps_the_markup_and_exposes_the_plain_text_alternative()
    {
        var content = new RtfContent(@"{\rtf1 bold}", "bold");

        content.Format.ShouldBe(ClipFormat.Rtf);
        content.Rtf.ShouldBe(@"{\rtf1 bold}");
        content.PlainText.ShouldBe("bold");
        content.SizeInBytes.ShouldBe(Encoding.UTF8.GetByteCount(@"{\rtf1 bold}") + Encoding.UTF8.GetByteCount("bold"));
    }

    [Fact]
    public void Rich_text_content_with_missing_parts_is_a_bug()
    {
        Should.Throw<ArgumentNullException>(() => new HtmlContent(null!, "x"));
        Should.Throw<ArgumentNullException>(() => new HtmlContent("x", null!));
        Should.Throw<ArgumentNullException>(() => new RtfContent(null!, "x"));
        Should.Throw<ArgumentNullException>(() => new RtfContent("x", null!));
    }

    [Fact]
    public void Image_content_holds_png_bytes_and_has_no_text()
    {
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

        var content = new ImageContent(png);

        content.Format.ShouldBe(ClipFormat.Image);
        content.PlainText.ShouldBeNull();
        content.SizeInBytes.ShouldBe(8);
        content.Png.ToArray().ShouldBe(png);
    }

    [Fact]
    public void Image_content_is_a_copy_so_changing_the_source_array_does_not_change_the_clip()
    {
        byte[] png = [1, 2, 3];
        var content = new ImageContent(png);

        png[0] = 99;

        content.Png[0].ShouldBe((byte)1);
    }

    [Fact]
    public void Image_content_with_no_bytes_is_a_bug()
    {
        Should.Throw<ArgumentException>(() => new ImageContent([]));
    }

    [Fact]
    public void Images_with_the_same_bytes_are_equal_and_hash_alike()
    {
        var first = new ImageContent(new byte[] { 1, 2, 3 });
        var second = new ImageContent(new byte[] { 1, 2, 3 });
        var other = new ImageContent(new byte[] { 1, 2, 4 });

        first.ShouldBe(second);
        first.GetHashCode().ShouldBe(second.GetHashCode());
        first.ShouldNotBe(other);
        first.Equals(null).ShouldBeFalse();
    }

    [Fact]
    public void A_file_list_stores_paths_only_and_never_touches_the_file_system()
    {
        // These paths do not exist. Building the content must not read, open or resolve them.
        var content = new FilesContent([@"C:\does\not\exist\a.txt", @"D:\also\missing\b.png"]);

        content.Format.ShouldBe(ClipFormat.Files);
        content.Paths.ShouldBe([@"C:\does\not\exist\a.txt", @"D:\also\missing\b.png"]);
        content.PlainText.ShouldBe("C:\\does\\not\\exist\\a.txt\r\nD:\\also\\missing\\b.png");
        content.SizeInBytes.ShouldBe(Encoding.UTF8.GetByteCount(@"C:\does\not\exist\a.txt") + Encoding.UTF8.GetByteCount(@"D:\also\missing\b.png"));
    }

    [Fact]
    public void A_file_list_is_a_copy_so_changing_the_source_list_does_not_change_the_clip()
    {
        var source = new List<string> { @"C:\a.txt" };
        var content = new FilesContent(source);

        source.Add(@"C:\b.txt");

        content.Paths.Length.ShouldBe(1);
    }

    [Fact]
    public void A_file_list_with_no_paths_or_a_blank_path_is_a_bug()
    {
        Should.Throw<ArgumentException>(() => new FilesContent([]));
        Should.Throw<ArgumentException>(() => new FilesContent([@"C:\a.txt", ""]));
        Should.Throw<ArgumentException>(() => new FilesContent([@"C:\a.txt", "  "]));
        Should.Throw<ArgumentNullException>(() => new FilesContent(null!));
        Should.Throw<ArgumentException>(() => new FilesContent([@"C:\a.txt", null!]));
    }

    [Fact]
    public void File_lists_are_equal_when_they_have_the_same_paths_in_the_same_order()
    {
        var first = new FilesContent([@"C:\a.txt", @"C:\b.txt"]);

        first.ShouldBe(new FilesContent([@"C:\a.txt", @"C:\b.txt"]));
        first.GetHashCode().ShouldBe(new FilesContent([@"C:\a.txt", @"C:\b.txt"]).GetHashCode());
        first.ShouldNotBe(new FilesContent([@"C:\b.txt", @"C:\a.txt"]));
        first.ShouldNotBe(new FilesContent([@"C:\a.txt"]));
        first.Equals(null).ShouldBeFalse();
    }

    [Fact]
    public void Content_of_different_formats_is_never_equal_even_with_the_same_text()
    {
        ClipContent text = new TextContent("same");
        ClipContent html = new HtmlContent("same", "same");
        ClipContent rtf = new RtfContent("same", "same");

        text.ShouldNotBe(html);
        html.ShouldNotBe(rtf);
        text.ShouldBe(new TextContent("same"));
        html.ShouldBe(new HtmlContent("same", "same"));
    }
}
