using System.Buffers.Binary;
using System.Text;
using ClipBench.Core.Clips;
using ClipBench.Core.Results;
using ClipBench.Storage.Clips;

namespace ClipBench.Storage.Tests.Clips;

public class ClipContentCodecTests
{
    private const string Arabic = "\u0645\u0631\u062D\u0628\u0627 \u0628\u0627\u0644\u0639\u0627\u0644\u0645";
    private const string Emoji = "grin \uD83D\uDE00 family \uD83D\uDC68\u200D\uD83D\uDC69\u200D\uD83D\uDC67";
    private const string Combining = "e\u0301 n\u0303";

    private static ClipContent RoundTrip(ClipContent content)
    {
        var encoded = ClipContentCodec.Encode(content);
        encoded.IsSuccess.ShouldBeTrue();
        var decoded = ClipContentCodec.Decode(content.Format, encoded.Value);
        decoded.IsSuccess.ShouldBeTrue(decoded.IsFailure ? decoded.Error.Message : string.Empty);
        return decoded.Value;
    }

    private static byte[] Frame(params byte[][] parts)
    {
        var bytes = new List<byte>();
        bytes.AddRange(UInt(parts.Length));
        foreach (var part in parts)
        {
            bytes.AddRange(UInt(part.Length));
            bytes.AddRange(part);
        }

        return [.. bytes];
    }

    private static byte[] UInt(int value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)value);
        return bytes;
    }

    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    // --- every format round-trips ---

    [Theory]
    [InlineData("")]
    [InlineData("plain text")]
    [InlineData("line one\r\nline two\nline three\ttabbed")]
    [InlineData(Arabic)]
    [InlineData(Emoji)]
    [InlineData(Combining)]
    [InlineData("nul \0 inside")]
    public void Text_round_trips(string text)
    {
        RoundTrip(new TextContent(text)).ShouldBe(new TextContent(text));
    }

    [Fact]
    public void A_long_text_round_trips()
    {
        var text = string.Concat(Enumerable.Repeat(Arabic + Emoji + "abc ", 5000));

        RoundTrip(new TextContent(text)).ShouldBe(new TextContent(text));
    }

    [Theory]
    [InlineData("<b>bold</b>", "bold")]
    [InlineData("<p dir=\"rtl\">" + Arabic + "</p>", Arabic)]
    [InlineData("<i>" + Emoji + "</i>", Emoji)]
    [InlineData("", "")]
    public void Html_round_trips_with_its_plain_text(string html, string text)
    {
        RoundTrip(new HtmlContent(html, text)).ShouldBe(new HtmlContent(html, text));
    }

    [Theory]
    [InlineData("{\\rtf1\\ansi bold}", "bold")]
    [InlineData("{\\rtf1 " + Combining + "}", Combining)]
    public void Rtf_round_trips_with_its_plain_text(string rtf, string text)
    {
        RoundTrip(new RtfContent(rtf, text)).ShouldBe(new RtfContent(rtf, text));
    }

    [Fact]
    public void An_image_round_trips_byte_for_byte()
    {
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x00, 0xFF, 0x00, 0x0D, 0x0A, 0x1A, 0x0A];

        RoundTrip(new ImageContent(png)).ShouldBe(new ImageContent(png));
    }

    [Fact]
    public void A_file_list_round_trips_in_order()
    {
        string[] paths = [@"C:\Users\me\one.txt", @"C:\Users\me\" + Arabic + ".docx", @"\\server\share\" + Emoji, "relative/path.md"];

        var decoded = (FilesContent)RoundTrip(new FilesContent(paths));

        decoded.Paths.ShouldBe(paths);
    }

    // --- the stored format is pinned: it feeds the hash and the sealed value ---

    [Fact]
    public void Text_is_stored_as_its_plain_UTF8_bytes()
    {
        ClipContentCodec.Encode(new TextContent(Arabic)).Value.ShouldBe(Utf8(Arabic));
    }

    [Fact]
    public void An_image_is_stored_as_its_plain_bytes()
    {
        ClipContentCodec.Encode(new ImageContent([1, 2, 3])).Value.ShouldBe([1, 2, 3]);
    }

    [Fact]
    public void Html_is_stored_as_a_count_and_two_length_prefixed_parts()
    {
        var encoded = ClipContentCodec.Encode(new HtmlContent("<b>x</b>", "x")).Value;

        encoded.ShouldBe(Frame(Utf8("<b>x</b>"), Utf8("x")));
    }

    [Fact]
    public void Different_splits_of_the_same_characters_encode_differently()
    {
        var first = ClipContentCodec.Encode(new HtmlContent("ab", "")).Value;
        var second = ClipContentCodec.Encode(new HtmlContent("a", "b")).Value;

        first.ShouldNotBe(second);
    }

    [Fact]
    public void Text_with_a_lone_surrogate_is_stored_with_a_replacement_character()
    {
        // Malformed UTF-16 cannot be written as UTF-8. It is replaced, as a preview already does, not rejected.
        var decoded = (TextContent)RoundTrip(new TextContent("a\uD800b"));

        decoded.Text.ShouldBe("a\uFFFDb");
    }

    // --- size limits ---

    [Fact]
    public void Content_over_the_limit_is_refused_when_encoding()
    {
        var result = ClipContentCodec.Encode(new TextContent(new string('x', 101)), maxBytes: 100);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ErrorCode.ContentTooLarge);
    }

    [Fact]
    public void Content_exactly_at_the_limit_is_accepted()
    {
        ClipContentCodec.Encode(new TextContent(new string('x', 100)), maxBytes: 100).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void An_image_is_measured_against_the_limit_too()
    {
        ClipContentCodec.Encode(new ImageContent(new byte[100]), maxBytes: 100).IsSuccess.ShouldBeTrue();
        ClipContentCodec.Encode(new ImageContent(new byte[101]), maxBytes: 100).Error.Code.ShouldBe(ErrorCode.ContentTooLarge);
    }

    [Fact]
    public void Framed_content_is_measured_with_its_framing()
    {
        // The two strings are 8 bytes, the framing adds 12, so 20 in all.
        var content = new HtmlContent("abcd", "efgh");

        ClipContentCodec.Encode(content, maxBytes: 20).IsSuccess.ShouldBeTrue();
        ClipContentCodec.Encode(content, maxBytes: 19).Error.Code.ShouldBe(ErrorCode.ContentTooLarge);
    }

    [Fact]
    public void Stored_data_over_the_limit_is_refused_without_being_parsed()
    {
        var result = ClipContentCodec.Decode(ClipFormat.Text, new byte[101], maxBytes: 100);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ErrorCode.ContentTooLarge);
    }

    // --- malformed stored data fails cleanly ---

    [Theory]
    [InlineData(ClipFormat.Html)]
    [InlineData(ClipFormat.Rtf)]
    [InlineData(ClipFormat.Files)]
    public void Framed_data_with_a_cut_off_end_is_rejected(ClipFormat format)
    {
        var whole = Frame(Utf8("one"), Utf8("two"));

        for (var length = 0; length < whole.Length; length++)
        {
            ClipContentCodec.Decode(format, whole.AsSpan(0, length)).IsFailure.ShouldBeTrue($"{format} accepted {length} of {whole.Length} bytes");
        }
    }

    [Theory]
    [InlineData(ClipFormat.Html)]
    [InlineData(ClipFormat.Rtf)]
    [InlineData(ClipFormat.Files)]
    public void Framed_data_with_trailing_bytes_is_rejected(ClipFormat format)
    {
        byte[] data = [.. Frame(Utf8("one"), Utf8("two")), 0];

        ClipContentCodec.Decode(format, data).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void A_part_that_claims_more_bytes_than_remain_is_rejected()
    {
        byte[] data = [.. UInt(2), .. UInt(int.MaxValue), 1, 2, 3];

        ClipContentCodec.Decode(ClipFormat.Html, data).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void An_absurd_part_count_is_rejected_without_allocating_for_it()
    {
        byte[] data = [.. UInt(int.MaxValue), 0, 0, 0, 0];

        ClipContentCodec.Decode(ClipFormat.Files, data).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void A_count_over_the_part_limit_is_rejected_even_if_every_part_is_valid()
    {
        ClipContentCodec.Decode(ClipFormat.Files, ManyPaths(ClipContentCodec.MaxParts + 1)).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void A_count_at_the_part_limit_is_accepted()
    {
        var decoded = ClipContentCodec.Decode(ClipFormat.Files, ManyPaths(ClipContentCodec.MaxParts));

        decoded.IsSuccess.ShouldBeTrue();
        ((FilesContent)decoded.Value).Paths.Length.ShouldBe(ClipContentCodec.MaxParts);
    }

    [Fact]
    public void Stored_data_exactly_at_the_limit_is_accepted()
    {
        ClipContentCodec.Decode(ClipFormat.Text, new byte[100].Select(_ => (byte)'x').ToArray(), maxBytes: 100).IsSuccess.ShouldBeTrue();
    }

    // Every path is the single letter "a", so each part is valid on its own.
    private static byte[] ManyPaths(int count)
    {
        var data = new byte[4 + (count * 5)];
        BinaryPrimitives.WriteUInt32LittleEndian(data, (uint)count);
        for (var i = 0; i < count; i++)
        {
            var offset = 4 + (i * 5);
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset), 1);
            data[offset + 4] = (byte)'a';
        }

        return data;
    }

    [Theory]
    [InlineData(ClipFormat.Html, 1)]
    [InlineData(ClipFormat.Html, 3)]
    [InlineData(ClipFormat.Rtf, 1)]
    [InlineData(ClipFormat.Rtf, 3)]
    public void Html_and_rtf_need_exactly_two_parts(ClipFormat format, int parts)
    {
        var data = Frame([.. Enumerable.Range(0, parts).Select(_ => Utf8("x"))]);

        ClipContentCodec.Decode(format, data).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void An_empty_file_list_is_rejected()
    {
        ClipContentCodec.Decode(ClipFormat.Files, Frame()).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void A_blank_file_path_is_rejected()
    {
        ClipContentCodec.Decode(ClipFormat.Files, Frame(Utf8("a.txt"), Utf8("  "))).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void An_empty_image_is_rejected()
    {
        ClipContentCodec.Decode(ClipFormat.Image, []).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Text_that_is_not_valid_UTF8_is_rejected()
    {
        ClipContentCodec.Decode(ClipFormat.Text, new byte[] { 0x61, 0xC3, 0x28 }).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Framed_text_that_is_not_valid_UTF8_is_rejected()
    {
        ClipContentCodec.Decode(ClipFormat.Html, Frame(Utf8("ok"), [0xFF, 0xFE])).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void An_unknown_format_is_rejected()
    {
        ClipContentCodec.Decode((ClipFormat)99, [1, 2, 3]).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void A_failure_message_never_contains_the_data()
    {
        var result = ClipContentCodec.Decode(ClipFormat.Html, Frame(Utf8("secret-marker")));

        result.Error.Message.ShouldNotContain("secret-marker");
    }

    [Theory]
    [InlineData(ClipFormat.Text)]
    [InlineData(ClipFormat.Html)]
    [InlineData(ClipFormat.Rtf)]
    [InlineData(ClipFormat.Image)]
    [InlineData(ClipFormat.Files)]
    public void Random_bytes_never_crash_the_decoder(ClipFormat format)
    {
        var random = new Random(20261013);
        for (var i = 0; i < 3000; i++)
        {
            var data = new byte[random.Next(0, 64)];
            random.NextBytes(data);
            if (i % 3 == 0 && data.Length >= 4)
            {
                // Make it look framed so the parser gets past the first check.
                BinaryPrimitives.WriteUInt32LittleEndian(data, (uint)random.Next(0, 4));
            }

            var result = ClipContentCodec.Decode(format, data);

            (result.IsSuccess || result.IsFailure).ShouldBeTrue();
        }
    }
}
