using System.Text;
using ClipBench.Core.Capture;
using ClipBench.Core.Clips;
using ClipBench.Core.Results;
using ClipBench.Platform.Windows.Clipboard;
using ClipBench.Platform.Windows.Formats;
using ClipBench.Platform.Windows.IntegrationTests.Support;

namespace ClipBench.Platform.Windows.IntegrationTests.Clipboard;

public class ClipboardWriterTests
{
    private readonly FakeClipboardApi _api = new();
    private readonly ManualTimeProvider _time = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Task<Result<long>> Write(ClipContent content) =>
        _time.RunAsync(new ClipboardWriter(_api, _time).WriteAsync(content, Ct));

    private async Task<long> WriteOk(ClipContent content)
    {
        var result = await Write(content);
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? $"{result.Error.Code}: {result.Error.Message}" : string.Empty);
        return result.Value;
    }

    private static ImageContent SampleImage(int channels = 4)
    {
        var pixels = PngBuilder.RandomPixels(5, 3, channels, 7);
        return new ImageContent(Png.Encode(5, 3, channels, (y, row) => pixels.AsSpan(y * 5 * channels, 5 * channels).CopyTo(row)));
    }

    // --- what is written ---

    [Fact]
    public async Task Text_is_written_as_UTF16_with_a_NUL()
    {
        await WriteOk(new TextContent("héllo ✓"));

        _api.DataOf(ClipboardFormats.UnicodeText).ShouldBe(Encoding.Unicode.GetBytes("héllo ✓\0"));
    }

    [Fact]
    public async Task HTML_is_written_in_the_HTML_layout_together_with_its_text()
    {
        await WriteOk(new HtmlContent("<b>Hi</b>", "Hi"));

        HtmlFormat.ExtractFragment(_api.DataOf(ClipboardFormats.Html)!, 1 << 20).Value.ShouldBe("<b>Hi</b>");
        _api.DataOf(ClipboardFormats.UnicodeText).ShouldBe(Encoding.Unicode.GetBytes("Hi\0"));
    }

    [Fact]
    public async Task Rich_text_is_written_as_ASCII_together_with_its_text()
    {
        await WriteOk(new RtfContent(@"{\rtf1\ansi Hi}", "Hi"));

        _api.DataOf(ClipboardFormats.Rtf).ShouldBe(Encoding.Latin1.GetBytes("{\\rtf1\\ansi Hi}\0"));
        _api.DataOf(ClipboardFormats.UnicodeText).ShouldBe(Encoding.Unicode.GetBytes("Hi\0"));
    }

    [Fact]
    public async Task An_image_is_written_as_a_PNG_and_as_a_version_5_bitmap()
    {
        var image = SampleImage();

        await WriteOk(image);

        _api.DataOf(ClipboardFormats.Png).ShouldBe(image.Png.ToArray());
        var bitmapAsPng = Dib.ToPng(_api.DataOf(ClipboardFormats.DibV5)!).Value;
        Png.Decode(bitmapAsPng).Value.Pixels.ShouldBe(Png.Decode(image.Png.AsSpan()).Value.Pixels);
    }

    [Fact]
    public async Task A_list_of_files_is_written_as_a_file_list()
    {
        await WriteOk(new FilesContent([@"C:\a.txt", @"\\server\share\b.txt"]));

        DropFiles.Parse(_api.DataOf(ClipboardFormats.Hdrop)!, 1 << 20).Value.ShouldBe([@"C:\a.txt", @"\\server\share\b.txt"]);
    }

    [Theory]
    [InlineData("text")]
    [InlineData("html")]
    [InlineData("rtf")]
    [InlineData("image")]
    [InlineData("files")]
    public async Task Every_write_carries_the_marker_that_says_it_came_from_ClipBench(string kind)
    {
        ClipContent content = kind switch
        {
            "text" => new TextContent("x"),
            "html" => new HtmlContent("<i>x</i>", "x"),
            "rtf" => new RtfContent(@"{\rtf1 x}", "x"),
            "image" => SampleImage(),
            _ => new FilesContent([@"C:\x.txt"]),
        };

        await WriteOk(content);

        _api.Has(ClipboardFormatNames.OwnWrite).ShouldBeTrue();
    }

    [Fact]
    public async Task The_receipt_is_the_sequence_number_right_after_the_write_so_a_later_change_can_be_noticed()
    {
        var receipt = await WriteOk(new TextContent("x"));

        receipt.ShouldBe(_api.SequenceNumber);
        _api.PutText("someone else");
        _api.SequenceNumber.ShouldNotBe(receipt);
    }

    [Fact]
    public async Task What_was_on_the_clipboard_before_is_replaced_and_not_added_to()
    {
        _api.PutText("old");
        _api.PutHtml("<p>old</p>");
        _api.Put("Some Private Format", [1]);

        await WriteOk(new TextContent("new"));

        _api.Has("Some Private Format").ShouldBeFalse();
        _api.Has(ClipboardFormats.Html).ShouldBeFalse();
        _api.DataOf(ClipboardFormats.UnicodeText).ShouldBe(Encoding.Unicode.GetBytes("new\0"));
    }

    [Fact]
    public async Task The_clipboard_is_opened_once_emptied_first_and_closed()
    {
        await WriteOk(new TextContent("x"));

        _api.Log.ShouldBe(["open", "empty", $"set:{ClipboardFormats.UnicodeText}", $"set:{_api.IdOf(ClipboardFormatNames.OwnWrite)}", "close"]);
    }

    // --- when it goes wrong ---

    [Fact]
    public async Task A_busy_clipboard_is_retried()
    {
        _api.BusyAttempts = 2;

        await WriteOk(new TextContent("x"));

        _time.RequestedDelays.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_clipboard_that_stays_busy_is_reported_and_left_as_it_was()
    {
        _api.PutText("keep me");
        _api.BusyAttempts = int.MaxValue;

        var result = await Write(new TextContent("x"));

        result.Error.Code.ShouldBe(ErrorCode.ClipboardBusy);
        _api.DataOf(ClipboardFormats.UnicodeText).ShouldBe(Encoding.Unicode.GetBytes("keep me\0"));
    }

    [Fact]
    public async Task A_picture_that_cannot_be_converted_fails_before_the_clipboard_is_touched()
    {
        _api.PutText("keep me");
        var palettePng = PngBuilder.Concat(
            PngBuilder.Signature,
            PngBuilder.Chunk("IHDR", PngBuilder.Header(1, 1, colorType: 3)),
            PngBuilder.Chunk("IDAT", PngBuilder.Deflate(new byte[2])),
            PngBuilder.End());

        var result = await Write(new ImageContent(palettePng));

        result.Error.Code.ShouldBe(ErrorCode.ClipboardWriteFailed);
        _api.Log.ShouldNotContain("open");
        _api.DataOf(ClipboardFormats.UnicodeText).ShouldBe(Encoding.Unicode.GetBytes("keep me\0"));
    }

    [Fact]
    public async Task If_Windows_will_not_empty_the_clipboard_the_write_fails_and_the_clipboard_is_closed()
    {
        _api.RefuseEmpty = true;

        var result = await Write(new TextContent("x"));

        result.Error.Code.ShouldBe(ErrorCode.ClipboardWriteFailed);
        _api.IsOpen.ShouldBeFalse();
    }

    [Fact]
    public async Task If_Windows_refuses_a_format_part_way_nothing_half_written_is_left_behind()
    {
        _api.RefusedFormats.Add(ClipboardFormats.UnicodeText);

        var result = await Write(new HtmlContent("<b>x</b>", "x"));

        result.Error.Code.ShouldBe(ErrorCode.ClipboardWriteFailed);
        _api.FormatIds.ShouldBeEmpty("a clipboard holding HTML with no text would paste as nothing in some programs");
        _api.IsOpen.ShouldBeFalse();
    }

    [Fact]
    public async Task Cancelling_while_waiting_for_a_busy_clipboard_ends_the_write()
    {
        _api.BusyAttempts = int.MaxValue;
        using var cancellation = new CancellationTokenSource();
        var writing = new ClipboardWriter(_api, _time).WriteAsync(new TextContent("x"), cancellation.Token);

        await cancellation.CancelAsync();

        await Should.ThrowAsync<TaskCanceledException>(writing);
    }

    // --- round trips through the reader ---

    private async Task<ClipContent> ReadBackWithoutTheMarker()
    {
        _api.Remove(ClipboardFormatNames.OwnWrite);
        var result = await _time.RunAsync(new ClipboardReader(_api, _time).ReadAsync(new CaptureFilter(), Ct));
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        return result.Value.Content!;
    }

    [Fact]
    public async Task What_the_writer_writes_the_reader_reads_back_unchanged_for_every_kind()
    {
        ClipContent[] contents =
        [
            new TextContent("héllo\r\nworld ✓ مرحبا 😀"),
            new HtmlContent("<td>1</td><td>2</td>", "1\t2"),
            new RtfContent(@"{\rtf1\ansi\deff0 {\fonttbl{\f0 Arial;}}Hello}", "Hello"),
            new FilesContent([@"C:\one.txt", @"\\server\share\two three.txt", @"C:\ملف.txt"]),
        ];

        foreach (var content in contents)
        {
            await WriteOk(content);

            (await ReadBackWithoutTheMarker()).ShouldBe(content);
        }
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public async Task An_image_survives_a_write_and_a_read_with_the_same_pixels(int channels)
    {
        var image = SampleImage(channels);
        await WriteOk(image);
        _api.Remove(ClipboardFormats.Png);   // read it the way a program that only knows bitmaps would

        var back = (ImageContent)await ReadBackWithoutTheMarker();

        var original = Png.Decode(image.Png.AsSpan()).Value;
        var returned = Png.Decode(back.Png.AsSpan()).Value;
        returned.Width.ShouldBe(original.Width);
        returned.Height.ShouldBe(original.Height);
        if (channels == 4)
        {
            returned.Pixels.ShouldBe(original.Pixels);
        }
        else
        {
            for (var i = 0; i < original.Width * original.Height; i++)
            {
                returned.Pixels.AsSpan(4 * i, 3).ToArray().ShouldBe(original.Pixels.AsSpan(3 * i, 3).ToArray());
                returned.Pixels[(4 * i) + 3].ShouldBe((byte)255);
            }
        }
    }

    [Fact]
    public async Task The_reader_refuses_what_the_writer_wrote_when_the_marker_is_still_there()
    {
        await WriteOk(new TextContent("our own paste"));

        var result = await _time.RunAsync(new ClipboardReader(_api, _time).ReadAsync(new CaptureFilter(), Ct));

        result.Value.Decision.Rejection.ShouldBe(CaptureRejection.OwnWrite);
        result.Value.Content.ShouldBeNull();
    }
}
