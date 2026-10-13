using System.Text;
using ClipBench.Core.Capture;
using ClipBench.Core.Clips;
using ClipBench.Core.Results;
using ClipBench.Platform.Windows.Clipboard;
using ClipBench.Platform.Windows.Formats;
using ClipBench.Platform.Windows.IntegrationTests.Support;
using Windows.Win32;

namespace ClipBench.Platform.Windows.IntegrationTests.Clipboard;

/// <summary>
/// These tests use the real Windows clipboard, so they replace whatever is on it while they run. The fixture
/// puts the user's clipboard back afterwards. They are excluded from the CI run with Category=Integration.
/// </summary>
[Collection("Clipboard")]
[Trait("Category", "Integration")]
public sealed class RealClipboardTests(RealClipboardFixture clipboard)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string ThisProgram => Path.GetFileName(Environment.ProcessPath)!;

    private ClipboardReader Reader => new(clipboard.Api, TimeProvider.System);

    private ClipboardWriter Writer => new(clipboard.Api, TimeProvider.System);

    // Another program replaces the clipboard: it empties it and sets each format, without ClipBench's marker.
    private async Task PutAsync(params (uint Format, byte[] Data)[] items)
    {
        await TestClipboard.UseAsync(
            clipboard.ForeignApi,
            session =>
            {
                session.Empty().ShouldBeTrue();
                foreach (var (format, data) in items)
                {
                    session.Set(format, data).ShouldBeTrue($"format {format}");
                }

                return 0;
            },
            Ct);
    }

    private uint Register(string name) => clipboard.ForeignApi.RegisterFormat(name);

    private static byte[] Text(string text) => Encoding.Unicode.GetBytes(text + "\0");

    private async Task<ClipboardCapture> ReadAsync(CaptureFilter? filter = null)
    {
        var result = await Reader.ReadAsync(filter ?? new CaptureFilter(), Ct);
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? $"{result.Error.Code}: {result.Error.Message}" : string.Empty);
        return result.Value;
    }

    private async Task<IReadOnlyList<(uint Id, string? Name, byte[] Data)>> AllFormatsAsync()
    {
        // Formats that are not blocks of memory (a bitmap object, for instance) cannot be copied and are left out.
        return await TestClipboard.UseAsync<IReadOnlyList<(uint Id, string? Name, byte[] Data)>>(
            clipboard.ForeignApi,
            session => [.. session.Formats()
                .Select(format => (format.Id, format.Name, Data: session.Read(format.Id, 64 * 1024 * 1024)))
                .Where(entry => entry.Data is not null)
                .Select(entry => (entry.Id, entry.Name, entry.Data!))],
            Ct);
    }

    private async Task<byte[]?> RawAsync(uint format) => (await AllFormatsAsync()).FirstOrDefault(entry => entry.Id == format).Data;

    // --- reading what another program put there ---

    [Fact]
    public async Task Text_is_read_with_the_program_it_came_from_and_a_sequence_number_that_grew()
    {
        var before = clipboard.Api.SequenceNumber;
        await PutAsync((ClipboardFormats.UnicodeText, Text("ClipBench integration test ✓ مرحبا 😀\r\nsecond line")));

        var capture = await ReadAsync();

        capture.Content.ShouldBe(new TextContent("ClipBench integration test ✓ مرحبا 😀\r\nsecond line"));
        capture.SourceProcess.ShouldBe(ThisProgram, "the clipboard owner window belongs to this process");
        capture.SequenceNumber.ShouldBeGreaterThan(before);
    }

    [Fact]
    public async Task HTML_with_text_is_read_as_the_copied_fragment_and_its_text()
    {
        await PutAsync((ClipboardFormats.UnicodeText, Text("Hello bold")), (Register(ClipboardFormats.Html), HtmlFormat.Build("Hello <b>bold</b>")));

        (await ReadAsync()).Content.ShouldBe(new HtmlContent("Hello <b>bold</b>", "Hello bold"));
    }

    [Fact]
    public async Task Rich_text_with_text_is_read_with_both()
    {
        await PutAsync((ClipboardFormats.UnicodeText, Text("Hello")), (Register(ClipboardFormats.Rtf), Encoding.Latin1.GetBytes(@"{\rtf1\ansi Hello}" + "\0")));

        (await ReadAsync()).Content.ShouldBe(new RtfContent(@"{\rtf1\ansi Hello}", "Hello"));
    }

    [Fact]
    public async Task A_file_list_is_read_as_paths_and_the_files_are_not_touched()
    {
        string[] paths = [@"C:\ClipBench\does-not-exist.txt", @"\\no-such-server\share\file.bin"];
        await PutAsync((ClipboardFormats.Hdrop, DropFiles.Build(paths)));

        (await ReadAsync()).Content.ShouldBe(new FilesContent(paths));
    }

    [Fact]
    public async Task A_bitmap_is_read_as_a_PNG_with_the_same_pixels()
    {
        var pixels = PngBuilder.RandomPixels(7, 5, 3, 21);
        var rows = Enumerable.Range(0, 5).Select(y => ToBgr(pixels.AsSpan(y * 21, 21))).ToList();
        await PutAsync((ClipboardFormats.Dib, DibBuilder.Join(DibBuilder.InfoHeader(7, 5, 24), DibBuilder.Rows(rows))));

        var image = (ImageContent)(await ReadAsync()).Content!;

        var decoded = Png.Decode(image.Png.AsSpan()).Value;
        decoded.Width.ShouldBe(7);
        decoded.Height.ShouldBe(5);
        decoded.Pixels.ShouldBe(pixels);
    }

    private static byte[] ToBgr(ReadOnlySpan<byte> rgb)
    {
        var bgr = new byte[rgb.Length];
        for (var i = 0; i < rgb.Length; i += 3)
        {
            (bgr[i], bgr[i + 1], bgr[i + 2]) = (rgb[i + 2], rgb[i + 1], rgb[i]);
        }

        return bgr;
    }

    [Fact]
    public async Task A_large_text_just_under_the_limit_is_read_and_one_over_the_limit_is_turned_away()
    {
        await PutAsync((ClipboardFormats.UnicodeText, Text(new string('x', 2 * 1024 * 1024 - 10))));
        (await ReadAsync()).Content!.SizeInBytes.ShouldBe((2 * 1024 * 1024) - 10);

        await PutAsync((ClipboardFormats.UnicodeText, Text(new string('x', 2 * 1024 * 1024 + 10))));
        (await ReadAsync()).Decision.Rejection.ShouldBe(CaptureRejection.TooLarge);
    }

    [Fact]
    public async Task An_empty_clipboard_is_reported_as_empty()
    {
        await PutAsync();

        var result = await Reader.ReadAsync(new CaptureFilter(), Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ErrorCode.ClipboardEmpty);
    }

    // --- privacy, with the real operating system ---

    [Theory]
    [InlineData(ClipboardFormatNames.ExcludeFromMonitorProcessing)]
    [InlineData(ClipboardFormatNames.ViewerIgnore)]
    public async Task Content_a_program_marked_private_is_not_recorded(string marker)
    {
        await PutAsync((ClipboardFormats.UnicodeText, Text("ClipBench test secret")), (Register(marker), [1, 0, 0, 0]));

        var capture = await ReadAsync();

        capture.Decision.Rejection.ShouldBe(CaptureRejection.PrivacyMarker);
        capture.Content.ShouldBeNull();
    }

    [Theory]
    [InlineData(0, CaptureRejection.PrivacyMarker)]
    [InlineData(1, CaptureRejection.None)]
    public async Task The_can_include_in_history_flag_is_honoured_as_Windows_stores_it(int flag, CaptureRejection expected)
    {
        await PutAsync((ClipboardFormats.UnicodeText, Text("flagged")), (Register(ClipboardFormatNames.CanIncludeInClipboardHistory), BitConverter.GetBytes(flag)));

        (await ReadAsync()).Decision.Rejection.ShouldBe(expected);
    }

    [Fact]
    public async Task Content_from_an_excluded_program_is_not_recorded_and_a_different_program_is_not_excluded()
    {
        await PutAsync((ClipboardFormats.UnicodeText, Text("from this test program")));

        (await ReadAsync(new CaptureFilter { ExcludedProcesses = [ThisProgram] })).Decision.Rejection.ShouldBe(CaptureRejection.ExcludedProcess);
        (await ReadAsync(new CaptureFilter { ExcludedProcesses = ["some-other-program.exe"] })).Decision.IsAccepted.ShouldBeTrue();
    }

    [Fact]
    public async Task In_incognito_nothing_is_recorded()
    {
        await PutAsync((ClipboardFormats.UnicodeText, Text("incognito")));

        (await ReadAsync(new CaptureFilter { Incognito = true })).Decision.Rejection.ShouldBe(CaptureRejection.Incognito);
    }

    // --- a clipboard another program holds open ---

    [Fact]
    public async Task A_clipboard_another_program_keeps_open_is_reported_as_busy_and_works_again_once_it_lets_go()
    {
        await PutAsync((ClipboardFormats.UnicodeText, Text("behind the lock")));
        using var held = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var holder = Task.Factory.StartNew(
            () =>
            {
                // Open and close on the same thread, as Windows requires.
                IClipboardSession? session = null;
                SpinWait.SpinUntil(() => (session = new Win32ClipboardApi(0).TryOpen()) is not null, TimeSpan.FromSeconds(10));
                held.Set();
                release.Wait(TimeSpan.FromSeconds(30));
                session?.Dispose();
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
        held.Wait(TimeSpan.FromSeconds(15), Ct).ShouldBeTrue();
        var impatient = new ClipboardReader(clipboard.Api, TimeProvider.System, new ClipboardRetryPolicy(3, TimeSpan.FromMilliseconds(5), TimeSpan.FromMilliseconds(5)));

        var busy = await impatient.ReadAsync(new CaptureFilter(), Ct);

        busy.IsFailure.ShouldBeTrue();
        busy.Error.Code.ShouldBe(ErrorCode.ClipboardBusy);

        release.Set();
        await holder;
        (await ReadAsync()).Content.ShouldBe(new TextContent("behind the lock"));
    }

    // --- writing ---

    [Fact]
    public async Task Text_that_ClipBench_writes_is_on_the_clipboard_as_UTF16_and_tagged_as_ClipBenchs_own()
    {
        var receipt = await Writer.WriteAsync(new TextContent("written by ClipBench ✓"), Ct);

        receipt.IsSuccess.ShouldBeTrue();
        (await RawAsync(ClipboardFormats.UnicodeText))!.Take(Text("written by ClipBench ✓").Length).ToArray().ShouldBe(Text("written by ClipBench ✓"));
        (await AllFormatsAsync()).ShouldContain(entry => entry.Name == ClipboardFormatNames.OwnWrite);
        clipboard.Api.SequenceNumber.ShouldBe(receipt.Value);
    }

    [Fact]
    public async Task What_ClipBench_wrote_is_not_recorded_again_but_is_still_there_to_paste()
    {
        await Writer.WriteAsync(new TextContent("our own paste"), Ct);

        var capture = await ReadAsync();

        capture.Decision.Rejection.ShouldBe(CaptureRejection.OwnWrite);
        capture.Content.ShouldBeNull();
        Encoding.Unicode.GetString((await RawAsync(ClipboardFormats.UnicodeText))!).TrimEnd('\0').ShouldBe("our own paste");
    }

    [Fact]
    public async Task The_owner_of_what_ClipBench_wrote_is_this_program()
    {
        await Writer.WriteAsync(new TextContent("owner check"), Ct);

        clipboard.Api.OwnerProcessName().ShouldBe(ThisProgram);
    }

    [Fact]
    public async Task What_ClipBench_writes_replaces_what_was_there()
    {
        await PutAsync((ClipboardFormats.UnicodeText, Text("old")), (Register("ClipBench Test Private Format"), [1, 2, 3]));

        await Writer.WriteAsync(new TextContent("new"), Ct);

        (await AllFormatsAsync()).ShouldNotContain(entry => entry.Name == "ClipBench Test Private Format");
    }

    [Fact]
    public async Task Written_HTML_and_rich_text_and_files_can_be_read_by_another_program()
    {
        await Writer.WriteAsync(new HtmlContent("<b>bold</b>", "bold"), Ct);
        HtmlFormat.ExtractFragment((await AllFormatsAsync()).Single(entry => entry.Name == ClipboardFormats.Html).Data, 1 << 20).Value.ShouldBe("<b>bold</b>");

        await Writer.WriteAsync(new RtfContent(@"{\rtf1 hello}", "hello"), Ct);
        Encoding.Latin1.GetString((await AllFormatsAsync()).Single(entry => entry.Name == ClipboardFormats.Rtf).Data).TrimEnd('\0').ShouldBe(@"{\rtf1 hello}");

        await Writer.WriteAsync(new FilesContent([@"C:\a b\c.txt", @"D:\d.txt"]), Ct);
        DropFiles.Parse(await RawAsync(ClipboardFormats.Hdrop) ?? [], 1 << 20).Value.ShouldBe([@"C:\a b\c.txt", @"D:\d.txt"]);
    }

    [Fact]
    public async Task A_written_picture_is_available_as_PNG_version_5_bitmap_and_the_plain_bitmap_older_programs_ask_for()
    {
        var pixels = PngBuilder.RandomPixels(6, 4, 3, 33);
        var png = new ImageContent(Png.Encode(6, 4, 3, (y, row) => pixels.AsSpan(y * 18, 18).CopyTo(row)));

        (await Writer.WriteAsync(png, Ct)).IsSuccess.ShouldBeTrue();

        var formats = await AllFormatsAsync();
        formats.ShouldContain(entry => entry.Name == ClipboardFormats.Png);
        formats.ShouldContain(entry => entry.Id == ClipboardFormats.DibV5);
        formats.ShouldContain(entry => entry.Id == ClipboardFormats.Dib, "Windows makes the plain bitmap from the version 5 one for older programs");
        var plain = Png.Decode(Dib.ToPng(formats.Single(entry => entry.Id == ClipboardFormats.Dib).Data).Value).Value;
        var colours = plain.Channels == 4
            ? plain.Pixels.Where((_, i) => i % 4 != 3).ToArray()   // drop the alpha channel when there is one
            : plain.Pixels;
        colours.ShouldBe(pixels, "an older program that reads the plain bitmap sees the same colours");
    }

    [Fact]
    public async Task Every_kind_survives_being_written_by_ClipBench_and_read_back_by_a_program_that_ignores_the_marker()
    {
        ClipContent[] contents =
        [
            new TextContent("round trip ✓ 😀 مرحبا"),
            new HtmlContent("<td>1</td><td>2</td>", "1\t2"),
            new RtfContent(@"{\rtf1\ansi Round trip}", "Round trip"),
            new FilesContent([@"C:\one.txt", @"\\server\share\two.txt"]),
        ];

        foreach (var content in contents)
        {
            await Writer.WriteAsync(content, Ct);
            var formats = await AllFormatsAsync();

            // The same formats put back by a program that does not carry ClipBench's marker.
            await PutAsync([.. formats.Where(entry => entry.Name != ClipboardFormatNames.OwnWrite).Select(entry => (entry.Id, entry.Data))]);

            (await ReadAsync()).Content.ShouldBe(content);
        }
    }

    // --- a snapshot of the real clipboard ---

    [Fact]
    public async Task A_snapshot_of_the_real_clipboard_puts_back_every_format_byte_for_byte_including_a_privacy_marker()
    {
        await PutAsync(
            (ClipboardFormats.UnicodeText, Text("snapshot me")),
            (Register(ClipboardFormats.Html), HtmlFormat.Build("<i>snapshot me</i>")),
            (Register(ClipboardFormatNames.ExcludeFromMonitorProcessing), [1, 0, 0, 0]));
        var before = (await AllFormatsAsync()).Where(entry => entry.Id is 13 or >= 0xC000).Select(entry => (entry.Id, entry.Data)).OrderBy(entry => entry.Id).ToList();
        var snapshot = await TestClipboard.UseAsync(clipboard.ForeignApi, session => ClipboardSnapshot.Take(session, 1 << 24), Ct);

        snapshot.ShouldNotBeNull();
        await PutAsync((ClipboardFormats.UnicodeText, Text("the paste replaced it")));
        (await TestClipboard.UseAsync(clipboard.ForeignApi, session => snapshot.Restore(session), Ct)).ShouldBeTrue();

        snapshot.Dispose();
        var after = (await AllFormatsAsync()).Where(entry => entry.Id is 13 or >= 0xC000).Select(entry => (entry.Id, entry.Data)).OrderBy(entry => entry.Id).ToList();
        after.Count.ShouldBe(before.Count);
        for (var i = 0; i < before.Count; i++)
        {
            after[i].Id.ShouldBe(before[i].Id);
            after[i].Data.Take(before[i].Data.Length).ToArray().ShouldBe(before[i].Data);
        }
    }

    [Fact]
    public async Task The_sequence_number_grows_with_every_change_and_stays_put_when_nothing_changes()
    {
        var first = clipboard.Api.SequenceNumber;
        await PutAsync((ClipboardFormats.UnicodeText, Text("one")));
        var second = clipboard.Api.SequenceNumber;
        var again = clipboard.Api.SequenceNumber;
        await PutAsync((ClipboardFormats.UnicodeText, Text("two")));
        var third = clipboard.Api.SequenceNumber;

        second.ShouldBeGreaterThan(first);
        again.ShouldBe(second);
        third.ShouldBeGreaterThan(second);
    }

    [Fact]
    public async Task A_format_that_is_a_handle_and_not_memory_is_refused_and_does_not_corrupt_the_heap()
    {
        // Regression: Windows adds a bitmap handle (CF_BITMAP) next to every bitmap. Treating a handle as a block
        // of memory (GlobalSize, GlobalLock) corrupted the heap and crashed the process.
        var rows = Enumerable.Range(0, 3).Select(_ => new byte[9]).ToList();
        await PutAsync((ClipboardFormats.Dib, DibBuilder.Join(DibBuilder.InfoHeader(3, 3, 24), DibBuilder.Rows(rows))));
        var (formats, size, data) = await TestClipboard.UseAsync(
            clipboard.ForeignApi,
            session => (session.Formats().Select(format => format.Id).ToList(), session.SizeOf(2), session.Read(2, 1 << 20)),
            Ct);
        formats.ShouldContain(2u, "Windows made a bitmap handle from the DIB");
        size.ShouldBeNull();
        data.ShouldBeNull();

        // A damaged heap shows up when memory is allocated and freed. Give it every chance to.
        for (var i = 0; i < 2_000; i++)
        {
            System.Runtime.InteropServices.Marshal.FreeHGlobal(System.Runtime.InteropServices.Marshal.AllocHGlobal(32 + i));
        }
    }

    [Fact]
    public void The_generated_declarations_are_for_the_functions_this_test_relies_on()
    {
        // A wrong signature would show up as a crash or a wrong value in the tests above. This pins the one
        // value every other test depends on: a registered format number is in the registered range.
        PInvoke.RegisterClipboardFormat("ClipBench Test Range Check").ShouldBeGreaterThanOrEqualTo(0xC000u);
    }
}
