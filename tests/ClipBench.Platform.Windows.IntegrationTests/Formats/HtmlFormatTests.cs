using System.Text;
using ClipBench.Core.Results;
using ClipBench.Platform.Windows.Formats;

namespace ClipBench.Platform.Windows.IntegrationTests.Formats;

public class HtmlFormatTests
{
    private const int Cap = 1 << 20;

    // Builds CF_HTML data the way applications do, with the offsets worked out from the real bytes.
    private static byte[] Document(string fragment, string lineEnding = "\r\n", string extraHeader = "", bool nulTerminate = true)
    {
        const string before = "<html><body><!--StartFragment-->";
        const string after = "<!--EndFragment--></body></html>";

        string Header(int startHtml, int endHtml, int startFragment, int endFragment) =>
            $"Version:0.9{lineEnding}StartHTML:{startHtml:D10}{lineEnding}EndHTML:{endHtml:D10}{lineEnding}"
            + $"StartFragment:{startFragment:D10}{lineEnding}EndFragment:{endFragment:D10}{lineEnding}{extraHeader}";

        var headerLength = Encoding.UTF8.GetByteCount(Header(0, 0, 0, 0));
        var startFragment = headerLength + Encoding.UTF8.GetByteCount(before);
        var endFragment = startFragment + Encoding.UTF8.GetByteCount(fragment);
        var endHtml = endFragment + Encoding.UTF8.GetByteCount(after);
        var text = Header(headerLength, endHtml, startFragment, endFragment) + before + fragment + after + (nulTerminate ? "\0" : "");
        return Encoding.UTF8.GetBytes(text);
    }

    private static string Extract(byte[] data) => HtmlFormat.ExtractFragment(data, Cap).Value;

    // --- reading what applications write ---

    [Fact]
    public void The_fragment_is_the_part_between_the_offsets_and_not_the_whole_page()
    {
        Extract(Document("<b>Hello</b>")).ShouldBe("<b>Hello</b>");
    }

    [Theory]
    [InlineData("plain")]
    [InlineData("<p>مرحبا بالعالم</p>")]
    [InlineData("<i>emoji 😀 and é</i>")]
    [InlineData("")]
    public void Text_in_any_script_comes_back_unchanged(string fragment)
    {
        Extract(Document(fragment)).ShouldBe(fragment);
    }

    [Fact]
    public void Line_breaks_other_header_fields_and_a_missing_terminator_are_tolerated()
    {
        Extract(Document("x", lineEnding: "\n")).ShouldBe("x");
        Extract(Document("x", extraHeader: "SourceURL:https://example.com/page\r\n")).ShouldBe("x");
        Extract(Document("x", nulTerminate: false)).ShouldBe("x");
    }

    [Fact]
    public void Field_names_are_not_case_sensitive()
    {
        // Same length, same offsets: only the case of the field names changes.
        var text = Encoding.UTF8.GetString(Document("abc"))
            .Replace("StartFragment:", "STARTFRAGMENT:", StringComparison.Ordinal)
            .Replace("EndFragment:", "endfragment:", StringComparison.Ordinal);
        text.ShouldContain("STARTFRAGMENT:");

        HtmlFormat.ExtractFragment(Encoding.UTF8.GetBytes(text), Cap).Value.ShouldBe("abc");
    }

    // --- refusing what is damaged ---

    public static TheoryData<string, string> Damaged => new()
    {
        { "no header at all", "<html>abc</html>" },
        { "an empty file", "" },
        { "only NUL bytes", "\0\0\0" },
        { "no fragment fields", "Version:0.9\r\nStartHTML:0000000000\r\nEndHTML:0000000030\r\n<html>abc</html>" },
        { "only the start of the fragment", "Version:0.9\r\nStartFragment:0000000030\r\n<html>abc</html>" },
        // The header of the next three is 63 bytes and the whole data is 79, so a start of 63 to 79 is where
        // a fragment would be. Each case is valid except for the one thing named, so the check that
        // refuses it is the one under test.
        { "the end before the start", "Version:0.9\r\nStartFragment:0000000070\r\nEndFragment:0000000065\r\n<html>abc</html>" },
        { "an offset past the end of the data", "Version:0.9\r\nStartFragment:0000000063\r\nEndFragment:0000009999\r\n<html>abc</html>" },
        { "a negative offset", "Version:0.9\r\nStartFragment:-000000001\r\nEndFragment:0000000040\r\n<html>abc</html>" },
        { "a start inside the header", "Version:0.9\r\nStartFragment:0000000003\r\nEndFragment:0000000040\r\n<html>abc</html>" },
        { "an offset that is not a number", "Version:0.9\r\nStartFragment:abc\r\nEndFragment:0000000040\r\n<html>abc</html>" },
        { "an empty offset", "Version:0.9\r\nStartFragment:\r\nEndFragment:0000000040\r\n<html>abc</html>" },
        { "an offset too large for any integer", "Version:0.9\r\nStartFragment:99999999999999999999\r\nEndFragment:99999999999999999999\r\n<html>abc</html>" },
        { "an offset with a plus sign", "Version:0.9\r\nStartFragment:+000000063\r\nEndFragment:0000000079\r\n<html>abc</html>" },
    };

    [Theory]
    [MemberData(nameof(Damaged))]
    public void Damaged_data_is_a_failure_and_not_a_crash(string description, string text)
    {
        var result = HtmlFormat.ExtractFragment(Encoding.UTF8.GetBytes(text), Cap);

        result.IsFailure.ShouldBeTrue(description);
        result.Error.Code.ShouldBe(ErrorCode.ClipboardUnreadable);
    }

    [Fact]
    public void A_fragment_that_runs_into_the_NUL_padding_is_refused()
    {
        // Applications pad the data with NULs. An end offset inside the padding is damage, and the NULs
        // are not part of the page.
        var document = Document("abc");
        var padded = new byte[document.Length + 10];
        document.CopyTo(padded, 0);
        var realEnd = document.Length - 1;
        var text = Encoding.UTF8.GetString(padded)
            .Replace($"EndFragment:{ByteIndexOf(document, "<!--EndFragment-->"):D10}", $"EndFragment:{realEnd + 3:D10}", StringComparison.Ordinal);

        HtmlFormat.ExtractFragment(Encoding.UTF8.GetBytes(text), Cap).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void The_same_header_with_valid_offsets_is_accepted_so_the_damaged_cases_above_fail_for_their_own_reason()
    {
        var valid = Encoding.UTF8.GetBytes("Version:0.9\r\nStartFragment:0000000063\r\nEndFragment:0000000079\r\n<html>abc</html>");

        Extract(valid).ShouldBe("<html>abc</html>");
    }

    [Fact]
    public void Data_over_the_size_cap_is_refused_before_it_is_read()
    {
        var data = Document(new string('a', 1000));

        HtmlFormat.ExtractFragment(data, data.Length - 1).IsFailure.ShouldBeTrue();
        HtmlFormat.ExtractFragment(data, data.Length).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void A_huge_header_without_the_fields_is_not_scanned_to_the_end()
    {
        var junk = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("Junk:1\r\n", 100_000)));

        var result = HtmlFormat.ExtractFragment(junk, junk.Length);

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void A_fragment_that_cuts_a_multi_byte_character_does_not_crash()
    {
        // "é" is two bytes; end the fragment after the first one.
        var data = Document("é");
        var text = Encoding.UTF8.GetString(data);
        var cut = text.Replace(
            $"EndFragment:{ByteIndexOf(data, "<!--EndFragment-->"):D10}",
            $"EndFragment:{ByteIndexOf(data, "<!--EndFragment-->") - 1:D10}",
            StringComparison.Ordinal);

        var result = HtmlFormat.ExtractFragment(Encoding.UTF8.GetBytes(cut), Cap);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Random_bytes_never_throw()
    {
        var random = new Random(1234);
        for (var round = 0; round < 500; round++)
        {
            var data = new byte[random.Next(0, 400)];
            random.NextBytes(data);

            Should.NotThrow(() => HtmlFormat.ExtractFragment(data, Cap));
        }
    }

    [Fact]
    public void Random_changes_to_a_valid_document_never_throw()
    {
        var random = new Random(99);
        var valid = Document("<b>text</b>");
        for (var round = 0; round < 500; round++)
        {
            var data = (byte[])valid.Clone();
            data[random.Next(data.Length)] = (byte)random.Next(256);

            Should.NotThrow(() => HtmlFormat.ExtractFragment(data, Cap));
        }
    }

    // --- writing it back ---

    [Theory]
    [InlineData("<b>Hello</b>")]
    [InlineData("<p>مرحبا</p>")]
    [InlineData("a 😀 b")]
    [InlineData("<td>1</td><td>2</td>")]
    public void A_fragment_that_is_written_can_be_read_back(string fragment)
    {
        HtmlFormat.ExtractFragment(HtmlFormat.Build(fragment), Cap).Value.ShouldBe(fragment);
    }

    [Fact]
    public void The_written_data_has_the_layout_applications_expect()
    {
        var data = HtmlFormat.Build("héllo");
        var text = Encoding.UTF8.GetString(data);

        text.ShouldStartWith("Version:0.9\r\n");
        text.ShouldContain("<!--StartFragment-->héllo<!--EndFragment-->");
        data[^1].ShouldBe((byte)0, "the data ends with a NUL, as applications write it");
        // The offsets are bytes, not characters: "é" is two bytes.
        var start = int.Parse(Field(text, "StartFragment"), System.Globalization.CultureInfo.InvariantCulture);
        var end = int.Parse(Field(text, "EndFragment"), System.Globalization.CultureInfo.InvariantCulture);
        Encoding.UTF8.GetString(data, start, end - start).ShouldBe("héllo");
        Field(text, "StartFragment").Length.ShouldBe(10, "offsets are written with ten digits");
    }

    private static string Field(string text, string name)
    {
        var from = text.IndexOf(name + ":", StringComparison.Ordinal) + name.Length + 1;
        return text[from..text.IndexOf("\r\n", from, StringComparison.Ordinal)];
    }

    private static int ByteIndexOf(byte[] data, string marker) => data.AsSpan().IndexOf(Encoding.UTF8.GetBytes(marker));
}
