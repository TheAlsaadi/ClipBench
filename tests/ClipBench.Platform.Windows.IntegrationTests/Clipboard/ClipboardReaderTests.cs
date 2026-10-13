using System.Text;
using ClipBench.Core.Capture;
using ClipBench.Core.Clips;
using ClipBench.Core.Results;
using ClipBench.Platform.Windows.Clipboard;
using ClipBench.Platform.Windows.Formats;
using ClipBench.Platform.Windows.IntegrationTests.Support;

namespace ClipBench.Platform.Windows.IntegrationTests.Clipboard;

public class ClipboardReaderTests
{
    private readonly FakeClipboardApi _api = new();
    private readonly ManualTimeProvider _time = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Replacement => char.ConvertFromUtf32(0xFFFD);

    private Task<Result<ClipboardCapture>> Read(CaptureFilter? filter = null) =>
        _time.RunAsync(new ClipboardReader(_api, _time).ReadAsync(filter ?? new CaptureFilter(), Ct));

    private async Task<ClipboardCapture> Capture(CaptureFilter? filter = null)
    {
        var result = await Read(filter);
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? $"{result.Error.Code}: {result.Error.Message}" : string.Empty);
        return result.Value;
    }

    private async Task<ClipContent> Content(CaptureFilter? filter = null)
    {
        var capture = await Capture(filter);
        capture.Decision.IsAccepted.ShouldBeTrue($"rejected: {capture.Decision.Rejection}");
        return capture.Content!;
    }

    private async Task<ErrorCode> Failure(CaptureFilter? filter = null)
    {
        var result = await Read(filter);
        result.IsFailure.ShouldBeTrue();
        return result.Error.Code;
    }

    private bool ContentWasTouched => _api.Log.Any(entry => entry.StartsWith("read:", StringComparison.Ordinal) || entry.StartsWith("size:", StringComparison.Ordinal));

    private static byte[] Dib24(int width, int height, int seed)
    {
        var rows = Enumerable.Range(0, height)
            .Select(y => PngBuilder.RandomPixels(width, 1, 3, seed + y))
            .ToList();
        return DibBuilder.Join(DibBuilder.InfoHeader(width, height, 24), DibBuilder.Rows(rows));
    }

    // --- what is read ---

    [Fact]
    public async Task Plain_text_becomes_text_with_the_source_and_the_sequence_number_at_the_time()
    {
        _api.PutText("héllo ✓");
        var sequence = _api.SequenceNumber;

        var capture = await Capture();

        capture.Content.ShouldBe(new TextContent("héllo ✓"));
        capture.SourceProcess.ShouldBe("app.exe");
        capture.SequenceNumber.ShouldBe(sequence);
    }

    [Fact]
    public async Task Text_ends_at_the_first_NUL_and_anything_after_it_is_ignored()
    {
        _api.Put(ClipboardFormats.UnicodeText, Encoding.Unicode.GetBytes("abc\0leftover\0"));

        (await Content()).ShouldBe(new TextContent("abc"));
    }

    [Fact]
    public async Task Text_that_is_not_valid_UTF16_is_repaired_and_never_stored_broken()
    {
        // A lone surrogate (0xD800) would be refused by storage.
        _api.Put(ClipboardFormats.UnicodeText, [0x00, 0xD8, 0x41, 0x00, 0x00, 0x00]);

        (await Content()).ShouldBe(new TextContent(Replacement + "A"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\r\n\t")]
    public async Task Text_that_is_empty_or_only_whitespace_is_nothing_to_keep(string text)
    {
        _api.PutText(text);

        (await Failure()).ShouldBe(ErrorCode.ClipboardEmpty);
    }

    [Fact]
    public async Task HTML_with_text_keeps_the_copied_fragment_and_the_text_alternative()
    {
        // The text differs from what removing the tags would give, so the test can tell which one was used.
        _api.PutText("Hello world (as the program wrote it)");
        _api.PutHtml("<b>Hello</b> world");

        (await Content()).ShouldBe(new HtmlContent("<b>Hello</b> world", "Hello world (as the program wrote it)"));
    }

    [Fact]
    public async Task HTML_whose_header_is_damaged_falls_back_to_the_text()
    {
        _api.PutText("plain");
        _api.Put(ClipboardFormats.Html, Encoding.UTF8.GetBytes("Version:0.9\r\nStartFragment:0000009999\r\nEndFragment:0000009999\r\n<p>x</p>"));

        (await Content()).ShouldBe(new TextContent("plain"));
    }

    [Fact]
    public async Task HTML_that_is_blank_falls_back_to_the_text()
    {
        _api.PutText("plain");
        _api.PutHtml("   ");

        (await Content()).ShouldBe(new TextContent("plain"));
    }

    [Fact]
    public async Task HTML_without_a_text_alternative_is_given_one_by_removing_the_tags()
    {
        _api.PutHtml("<p>One</p><p>Two &amp; three</p>");

        (await Content()).ShouldBe(new HtmlContent("<p>One</p><p>Two &amp; three</p>", "One\nTwo & three"));
    }

    [Fact]
    public async Task Rich_text_with_text_keeps_both()
    {
        _api.PutText("Hello");
        _api.PutRtf(@"{\rtf1\ansi Hello}");

        (await Content()).ShouldBe(new RtfContent(@"{\rtf1\ansi Hello}", "Hello"));
    }

    [Fact]
    public async Task HTML_is_preferred_to_rich_text_when_both_are_there()
    {
        _api.PutText("Hello");
        _api.PutRtf(@"{\rtf1 Hello}");
        _api.PutHtml("<i>Hello</i>");

        (await Content()).ShouldBeOfType<HtmlContent>();
    }

    [Fact]
    public async Task Rich_text_with_nothing_else_cannot_be_given_a_text_alternative_and_is_not_kept()
    {
        _api.PutRtf(@"{\rtf1 Hello}");

        (await Failure()).ShouldBe(ErrorCode.ClipboardEmpty);
    }

    [Fact]
    public async Task A_list_of_files_is_kept_as_paths_even_when_text_is_there_too()
    {
        _api.PutText(@"C:\a.txt");
        _api.PutFiles(@"C:\a.txt", @"D:\folder\b.png");

        (await Content()).ShouldBe(new FilesContent([@"C:\a.txt", @"D:\folder\b.png"]));
    }

    [Fact]
    public async Task A_damaged_file_list_falls_back_to_the_text()
    {
        _api.PutText("fallback");
        _api.Put(ClipboardFormats.Hdrop, [1, 2, 3]);

        (await Content()).ShouldBe(new TextContent("fallback"));
    }

    [Fact]
    public async Task A_damaged_file_list_and_nothing_else_is_unreadable_and_not_empty()
    {
        _api.Put(ClipboardFormats.Hdrop, [1, 2, 3]);

        (await Failure()).ShouldBe(ErrorCode.ClipboardUnreadable);
    }

    // --- images ---

    [Fact]
    public async Task A_bitmap_becomes_a_PNG_with_the_same_pixels()
    {
        var pixels = PngBuilder.RandomPixels(6, 4, 3, 1);
        _api.Put(ClipboardFormats.Dib, DibBuilder.Join(DibBuilder.InfoHeader(6, 4, 24), DibBuilder.Rows([.. Enumerable.Range(0, 4).Select(y => ToBgr(pixels.AsSpan(y * 18, 18)))])));

        var content = (ImageContent)await Content();

        var decoded = Png.Decode(content.Png.AsSpan()).Value;
        decoded.Width.ShouldBe(6);
        decoded.Height.ShouldBe(4);
        decoded.Pixels.ShouldBe(pixels);
    }

    private static byte[] ToBgr(ReadOnlySpan<byte> rgb)
    {
        var bgr = new byte[rgb.Length];
        for (var i = 0; i < rgb.Length; i += 3)
        {
            bgr[i] = rgb[i + 2];
            bgr[i + 1] = rgb[i + 1];
            bgr[i + 2] = rgb[i];
        }

        return bgr;
    }

    [Fact]
    public async Task The_version_5_bitmap_is_preferred_to_the_plain_one_because_it_can_carry_alpha()
    {
        _api.Put(ClipboardFormats.Dib, Dib24(2, 2, 1));
        var withAlpha = DibBuilder.Join(
            DibBuilder.ExtendedHeader(124, 1, 1, 32, 0x00FF0000, 0x0000FF00, 0x000000FF, 0xFF000000),
            [10, 20, 30, 128]);
        _api.Put(ClipboardFormats.DibV5, withAlpha);

        var content = (ImageContent)await Content();

        var decoded = Png.Decode(content.Png.AsSpan()).Value;
        decoded.Channels.ShouldBe(4);
        decoded.Pixels.ShouldBe([30, 20, 10, 128]);
    }

    [Fact]
    public async Task A_browsers_Copy_image_has_HTML_but_no_text_and_is_kept_as_the_image()
    {
        _api.PutHtml("<img src=\"https://example.com/cat.png\">");
        _api.Put(ClipboardFormats.Dib, Dib24(3, 3, 2));

        (await Content()).ShouldBeOfType<ImageContent>();
    }

    [Fact]
    public async Task A_spreadsheet_range_has_text_HTML_and_a_picture_and_is_kept_as_HTML()
    {
        _api.PutText("1\t2");
        _api.PutHtml("<table><tr><td>1</td><td>2</td></tr></table>");
        _api.Put(ClipboardFormats.Dib, Dib24(3, 3, 3));

        (await Content()).ShouldBeOfType<HtmlContent>();
    }

    [Fact]
    public async Task Text_and_an_image_together_are_kept_as_text()
    {
        _api.PutText("caption");
        _api.Put(ClipboardFormats.Dib, Dib24(3, 3, 4));

        (await Content()).ShouldBe(new TextContent("caption"));
    }

    [Fact]
    public async Task Blank_text_with_an_image_is_kept_as_the_image()
    {
        _api.PutText(" ");
        _api.Put(ClipboardFormats.Dib, Dib24(3, 3, 5));

        (await Content()).ShouldBeOfType<ImageContent>();
    }

    [Fact]
    public async Task A_damaged_bitmap_with_text_falls_back_to_the_text()
    {
        _api.PutText("fallback");
        _api.Put(ClipboardFormats.Dib, [1, 2, 3, 4]);

        (await Content()).ShouldBe(new TextContent("fallback"));
    }

    [Fact]
    public async Task A_damaged_bitmap_and_nothing_else_is_unreadable()
    {
        _api.Put(ClipboardFormats.Dib, [1, 2, 3, 4]);

        (await Failure()).ShouldBe(ErrorCode.ClipboardUnreadable);
    }

    // --- nothing to keep ---

    [Fact]
    public async Task An_empty_clipboard_has_nothing_to_keep()
    {
        (await Failure()).ShouldBe(ErrorCode.ClipboardEmpty);
        ContentWasTouched.ShouldBeFalse();
    }

    [Fact]
    public async Task A_clipboard_with_only_formats_ClipBench_does_not_read_has_nothing_to_keep()
    {
        _api.Put("Some Private Format", [1, 2, 3]);
        _api.Put(2, [1, 2, 3]);   // CF_BITMAP: a handle, never read; Windows adds it next to a DIB

        (await Failure()).ShouldBe(ErrorCode.ClipboardEmpty);
    }

    // --- privacy: the filter is applied before any content is read ---

    [Theory]
    [InlineData(ClipboardFormatNames.ExcludeFromMonitorProcessing)]
    [InlineData(ClipboardFormatNames.ViewerIgnore)]
    [InlineData("clipboard viewer ignore")]
    public async Task Content_marked_private_is_rejected_without_a_single_byte_of_it_being_read(string marker)
    {
        _api.PutText("hunter2");
        _api.PutMarker(marker);

        var capture = await Capture();

        capture.Decision.Rejection.ShouldBe(CaptureRejection.PrivacyMarker);
        capture.Content.ShouldBeNull();
        capture.SourceProcess.ShouldBeNull();
        ContentWasTouched.ShouldBeFalse("the text must never have been copied into memory");
    }

    [Fact]
    public async Task A_history_flag_of_zero_rejects_the_content_and_only_the_flag_itself_is_read()
    {
        _api.PutText("hunter2");
        _api.PutCanInclude(0);

        var capture = await Capture();

        capture.Decision.Rejection.ShouldBe(CaptureRejection.PrivacyMarker);
        _api.Log.Where(entry => entry.StartsWith("read:", StringComparison.Ordinal)).ShouldBe([$"read:{_api.IdOf(ClipboardFormatNames.CanIncludeInClipboardHistory)}"]);
        ContentWasTouched.ShouldBeTrue();   // the flag read counts, the text must not
        _api.Log.ShouldNotContain($"read:{ClipboardFormats.UnicodeText}");
        _api.Log.ShouldNotContain($"size:{ClipboardFormats.UnicodeText}");
    }

    [Fact]
    public async Task A_history_flag_of_one_allows_the_content()
    {
        _api.PutText("fine");
        _api.PutCanInclude(1);

        (await Content()).ShouldBe(new TextContent("fine"));
    }

    [Fact]
    public async Task A_history_flag_that_cannot_be_read_is_treated_as_private()
    {
        _api.PutText("maybe a secret");
        _api.PutCanInclude(1);
        _api.UnreadableFormats.Add(_api.IdOf(ClipboardFormatNames.CanIncludeInClipboardHistory));

        var capture = await Capture();

        capture.Decision.Rejection.ShouldBe(CaptureRejection.PrivacyMarker);
        _api.Log.ShouldNotContain($"read:{ClipboardFormats.UnicodeText}");
    }

    [Fact]
    public async Task A_history_flag_that_is_too_short_to_be_a_number_is_treated_as_private()
    {
        _api.PutText("maybe a secret");
        _api.Put(ClipboardFormatNames.CanIncludeInClipboardHistory, [1, 0]);

        (await Capture()).Decision.Rejection.ShouldBe(CaptureRejection.PrivacyMarker);
    }

    [Theory]
    [InlineData(0, CaptureRejection.PrivacyMarker)]
    [InlineData(1, CaptureRejection.None)]
    public async Task A_history_flag_in_a_block_that_Windows_rounded_up_in_size_is_still_read_from_its_first_four_bytes(int value, CaptureRejection expected)
    {
        // GlobalAlloc may report more bytes than were asked for, so the flag can arrive with padding after it.
        _api.PutText("text");
        _api.Put(ClipboardFormatNames.CanIncludeInClipboardHistory, [.. BitConverter.GetBytes(value), 0, 0, 0, 0, 0, 0, 0, 0]);

        (await Capture()).Decision.Rejection.ShouldBe(expected);
    }

    [Fact]
    public async Task ClipBenchs_own_writes_are_rejected_without_being_read()
    {
        _api.PutText("something we just pasted");
        _api.PutMarker(ClipboardFormatNames.OwnWrite);

        var capture = await Capture();

        capture.Decision.Rejection.ShouldBe(CaptureRejection.OwnWrite);
        ContentWasTouched.ShouldBeFalse();
    }

    [Fact]
    public async Task In_incognito_nothing_is_read()
    {
        _api.PutText("anything");

        var capture = await Capture(new CaptureFilter { Incognito = true });

        capture.Decision.Rejection.ShouldBe(CaptureRejection.Incognito);
        ContentWasTouched.ShouldBeFalse();
    }

    [Fact]
    public async Task Content_from_an_excluded_program_is_rejected_without_being_read()
    {
        _api.Owner = "KeePass.exe";
        _api.PutText("a password");

        var capture = await Capture(new CaptureFilter { ExcludedProcesses = ["keepass"] });

        capture.Decision.Rejection.ShouldBe(CaptureRejection.ExcludedProcess);
        capture.SourceProcess.ShouldBeNull();
        ContentWasTouched.ShouldBeFalse();
    }

    [Fact]
    public async Task A_source_that_cannot_be_found_is_not_a_reason_to_reject()
    {
        _api.Owner = null;
        _api.PutText("anonymous");

        var capture = await Capture(new CaptureFilter { ExcludedProcesses = ["keepass"] });

        capture.Decision.IsAccepted.ShouldBeTrue();
        capture.SourceProcess.ShouldBeNull();
    }

    // --- size ---

    [Fact]
    public async Task Text_over_the_limit_is_rejected_after_it_is_measured()
    {
        _api.PutText(new string('x', 150));

        var capture = await Capture(new CaptureFilter { Limits = new CaptureLimits { MaxTextBytes = 100 } });

        capture.Decision.Rejection.ShouldBe(CaptureRejection.TooLarge);
        capture.Content.ShouldBeNull();
    }

    [Fact]
    public async Task Text_at_the_limit_is_kept()
    {
        _api.PutText(new string('x', 100));

        (await Content(new CaptureFilter { Limits = new CaptureLimits { MaxTextBytes = 100 } })).SizeInBytes.ShouldBe(100);
    }

    [Fact]
    public async Task Data_far_over_the_limit_is_not_even_copied()
    {
        // 4 bytes of data for every byte of limit is the most that is ever copied (UTF-16 can double the size).
        _api.PutText(new string('x', 500));   // 1,002 bytes on the clipboard

        var capture = await Capture(new CaptureFilter { Limits = new CaptureLimits { MaxTextBytes = 100 } });

        capture.Decision.Rejection.ShouldBe(CaptureRejection.TooLarge);
        _api.Log.ShouldContain($"size:{ClipboardFormats.UnicodeText}");
        _api.Log.ShouldNotContain($"read:{ClipboardFormats.UnicodeText}");
    }

    [Fact]
    public async Task A_bitmap_over_the_hard_cap_is_rejected_without_being_copied()
    {
        _api.Put(ClipboardFormats.Dib, Dib24(2, 2, 1));
        _api.ReportedSizes[ClipboardFormats.Dib] = Dib.MaxBytes + 1;

        var capture = await Capture();

        capture.Decision.Rejection.ShouldBe(CaptureRejection.TooLarge);
        _api.Log.ShouldNotContain($"read:{ClipboardFormats.Dib}");
    }

    [Fact]
    public async Task An_image_is_judged_by_the_size_of_the_PNG_and_not_the_size_of_the_raw_bitmap()
    {
        // 200 x 200 flat colour: 120 KB of bitmap, a few hundred bytes of PNG. A 20 MB limit judged on the
        // bitmap would turn away most 4K screenshots; judging the PNG keeps them.
        var rows = Enumerable.Range(0, 200).Select(_ => new byte[600]).ToList();
        _api.Put(ClipboardFormats.Dib, DibBuilder.Join(DibBuilder.InfoHeader(200, 200, 24), DibBuilder.Rows(rows)));

        var capture = await Capture(new CaptureFilter { Limits = new CaptureLimits { MaxImageBytes = 10_000 } });

        capture.Decision.IsAccepted.ShouldBeTrue();
        capture.Content!.SizeInBytes.ShouldBeLessThan(10_000);
    }

    [Fact]
    public async Task An_image_whose_PNG_is_over_the_limit_is_rejected()
    {
        _api.Put(ClipboardFormats.Dib, Dib24(60, 60, 9));   // random pixels do not compress

        var capture = await Capture(new CaptureFilter { Limits = new CaptureLimits { MaxImageBytes = 1_000 } });

        capture.Decision.Rejection.ShouldBe(CaptureRejection.TooLarge);
    }

    // --- the clipboard is used politely ---

    [Fact]
    public async Task The_clipboard_is_opened_once_and_closed_again_after_a_read()
    {
        _api.PutText("x");

        await Capture();

        _api.Log.Count(entry => entry == "open").ShouldBe(1);
        _api.Log[^1].ShouldBe("close");
        _api.IsOpen.ShouldBeFalse();
    }

    [Theory]
    [InlineData("rejected")]
    [InlineData("empty")]
    [InlineData("damaged")]
    public async Task The_clipboard_is_closed_whatever_the_outcome(string outcome)
    {
        switch (outcome)
        {
            case "rejected":
                _api.PutText("x");
                _api.PutMarker(ClipboardFormatNames.ExcludeFromMonitorProcessing);
                break;
            case "damaged":
                _api.Put(ClipboardFormats.Hdrop, [1]);
                break;
        }

        await Read();

        _api.IsOpen.ShouldBeFalse();
        _api.Log.Count(entry => entry == "open").ShouldBe(_api.Log.Count(entry => entry == "close"));
    }

    [Fact]
    public async Task A_busy_clipboard_is_retried_and_then_read()
    {
        _api.PutText("late");
        _api.BusyAttempts = 2;

        (await Content()).ShouldBe(new TextContent("late"));
        _time.RequestedDelays.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_clipboard_that_stays_busy_is_reported_as_busy_and_not_as_empty()
    {
        _api.PutText("never read");
        _api.BusyAttempts = int.MaxValue;

        (await Failure()).ShouldBe(ErrorCode.ClipboardBusy);
        ContentWasTouched.ShouldBeFalse();
    }

    [Fact]
    public async Task Cancelling_while_waiting_for_a_busy_clipboard_ends_the_read()
    {
        _api.BusyAttempts = int.MaxValue;
        using var cancellation = new CancellationTokenSource();
        var reading = new ClipboardReader(_api, _time).ReadAsync(new CaptureFilter(), cancellation.Token);

        await cancellation.CancelAsync();

        await Should.ThrowAsync<TaskCanceledException>(reading);
    }
}
