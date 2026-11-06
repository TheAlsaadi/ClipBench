using ClipBench.Core.Capture;
using ClipBench.Core.Clips;
using ClipBench.Core.Diagnostics;
using ClipBench.Core.History;
using ClipBench.Core.Results;
using ClipBench.Core.Screen;
using ClipBench.Core.Tests.Support;

namespace ClipBench.Core.Tests.Screen;

public sealed class ScreenTextServiceTests : IDisposable
{
    private const string Jwt =
        "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIiwibmFtZSI6IkpvaG4gRG9lIiwiaWF0IjoxNTE2MjM5MDIyfQ.SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c";

    private readonly FakeRegionPicker _picker = new();
    private readonly FakeScreenCapturer _capturer = new();
    private readonly FakeTextRecognizer _recognizer = new();
    private readonly FakeClipboardWriter _writer = new();
    private readonly FakeIncognito _incognito = new();
    private readonly FakeNotifier _notifier = new();
    private readonly FakeActivityLog _log = new();
    private readonly FakeTimeProvider _clock = new();
    private readonly FakeClipStore _store = new();
    private readonly ClipLibrary _library;
    private readonly ScreenTextService _service;

    public ScreenTextServiceTests()
    {
        _library = new ClipLibrary(new ClipHistory(new HistoryOptions(), _clock), _store) { SensitiveExpiry = TimeSpan.FromMinutes(2) };
        _service = new ScreenTextService(_picker, _capturer, _recognizer, _writer, _library, _incognito, _notifier, _log, _clock);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _library.Dispose();

    // --- the good path ---

    [Fact]
    public async Task The_text_in_the_chosen_area_goes_to_the_clipboard_and_the_history()
    {
        var result = await _service.CaptureTextAsync(Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(new ScreenTextOutcome(21, true));
        _capturer.Regions.ShouldBe([new PixelRect(10, 20, 300, 100)]);
        ((TextContent)_writer.Written.Single()).Text.ShouldBe("Hello from the screen");
        var clip = _library.Clips.Single();
        ((TextContent)clip.Content).Text.ShouldBe("Hello from the screen");
        clip.SourceApp.ShouldBe(ScreenTextService.SourceName);
        clip.CapturedAt.ShouldBe(_clock.GetUtcNow());
    }

    [Fact]
    public async Task The_user_is_told_how_much_text_was_copied_without_the_text_itself()
    {
        await _service.CaptureTextAsync(Ct);

        _notifier.Shown.Single().Message.ShouldBe("Copied 21 characters from the screen.");
        _notifier.Shown.Single().Message.ShouldNotContain("Hello");
    }

    [Fact]
    public async Task White_space_at_the_ends_of_the_text_is_dropped_and_the_inside_is_kept()
    {
        _recognizer.Answer = Result.Success("  \r\n line one\r\nline two \r\n\r\n");

        await _service.CaptureTextAsync(Ct);

        ((TextContent)_writer.Written.Single()).Text.ShouldBe("line one\r\nline two");
    }

    [Fact]
    public async Task The_activity_log_records_the_size_and_never_the_text()
    {
        await _service.CaptureTextAsync(Ct);

        var entry = _log.Entries.Single(e => e.Activity == ActivityEvent.ScreenTextCopied);
        entry.Details.Count.ShouldBe(21);
        entry.Details.Bytes.ShouldBe(21);
        entry.Level.ShouldBe(ActivityLevel.Information);
    }

    // --- the pixels ---

    [Fact]
    public async Task The_pixels_are_zeroed_and_let_go_of_as_soon_as_the_text_is_read()
    {
        await _service.CaptureTextAsync(Ct);

        _recognizer.PixelsWereIntactDuringTheCall.ShouldBeTrue();
        _capturer.Images.Single().IsDisposed.ShouldBeTrue();
        _capturer.Images.Single().Pixels.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_pixels_are_zeroed_when_the_reading_fails_too()
    {
        _recognizer.Answer = Result.Failure<string>(new ResultError(ErrorCode.OcrFailed, "Windows could not read the picture."));

        await _service.CaptureTextAsync(Ct);

        _capturer.Images.Single().IsDisposed.ShouldBeTrue();
    }

    [Fact]
    public async Task The_pixels_are_zeroed_when_the_reading_is_cancelled()
    {
        using var source = new CancellationTokenSource();

        // The user gives up while the area is being chosen: the picture is taken and then reading it is cancelled.
        _picker.During = () =>
        {
            source.Cancel();
            return Task.CompletedTask;
        };

        await Should.ThrowAsync<OperationCanceledException>(() => _service.CaptureTextAsync(source.Token));

        _capturer.Images.Count.ShouldBe(1);
        _capturer.Images.ShouldAllBe(image => image.IsDisposed);
    }

    // --- secrets and incognito ---

    [Fact]
    public async Task A_secret_read_from_the_screen_is_kept_only_for_the_secret_time()
    {
        _recognizer.Answer = Result.Success(Jwt);

        await _service.CaptureTextAsync(Ct);

        _library.Clips.Single().ExpiresAt.ShouldBe(_clock.GetUtcNow() + TimeSpan.FromMinutes(2));
    }

    [Fact]
    public async Task With_incognito_on_the_text_is_copied_to_the_clipboard_but_not_recorded()
    {
        _incognito.IsPaused = true;

        var result = await _service.CaptureTextAsync(Ct);

        result.Value.ShouldBe(new ScreenTextOutcome(21, false));
        _writer.Written.Count.ShouldBe(1);
        _library.Clips.ShouldBeEmpty();
        _store.Saved.ShouldBeEmpty();
        _notifier.Shown.Single().Message.ShouldBe("Copied 21 characters from the screen. Incognito is on, so it was not kept in the history.");
    }

    // --- things that do not work ---

    [Fact]
    public async Task Pressing_escape_on_the_overlay_does_nothing_and_says_nothing()
    {
        _picker.Answer = Result.Failure<PixelRect>(new ResultError(ErrorCode.SelectionCancelled, "cancelled"));

        var result = await _service.CaptureTextAsync(Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ErrorCode.SelectionCancelled);
        _capturer.Regions.ShouldBeEmpty();
        _writer.Written.ShouldBeEmpty();
        _notifier.Shown.ShouldBeEmpty();
        _log.Entries.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(ErrorCode.RegionTooSmall)]
    [InlineData(ErrorCode.RegionTooLarge)]
    public async Task A_region_that_cannot_be_used_is_explained(ErrorCode code)
    {
        _picker.Answer = Result.Failure<PixelRect>(new ResultError(code, "Choose a different area."));

        var result = await _service.CaptureTextAsync(Ct);

        result.Error.Code.ShouldBe(code);
        _notifier.Shown.Single().Message.ShouldBe("Choose a different area.");
        _capturer.Regions.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_capture_that_fails_is_reported_and_nothing_is_read()
    {
        _capturer.Failure = new ResultError(ErrorCode.ScreenCaptureFailed, "Windows would not let ClipBench read that part of the screen.");

        var result = await _service.CaptureTextAsync(Ct);

        result.Error.Code.ShouldBe(ErrorCode.ScreenCaptureFailed);
        _notifier.Shown.Single().Message.ShouldBe("Windows would not let ClipBench read that part of the screen.");
        _recognizer.Calls.ShouldBe(0);
        _writer.Written.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_missing_language_pack_gives_the_message_the_reader_wrote()
    {
        const string Message = "Windows has no text-recognition language for your languages. Add one in Settings.";
        _recognizer.Answer = Result.Failure<string>(new ResultError(ErrorCode.OcrLanguageMissing, Message));

        var result = await _service.CaptureTextAsync(Ct);

        result.Error.Code.ShouldBe(ErrorCode.OcrLanguageMissing);
        _notifier.Shown.Single().Message.ShouldBe(Message);
        _writer.Written.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\r\n\r\n")]
    public async Task An_area_with_no_text_says_so_and_changes_nothing(string recognized)
    {
        _recognizer.Answer = Result.Success(recognized);

        var result = await _service.CaptureTextAsync(Ct);

        result.Error.Code.ShouldBe(ErrorCode.NoTextFound);
        _notifier.Shown.Single().Message.ShouldBe("No text was found in that area.");
        _writer.Written.ShouldBeEmpty();
        _library.Clips.ShouldBeEmpty();
    }

    [Fact]
    public async Task Text_over_the_size_limit_is_refused()
    {
        _recognizer.Answer = Result.Success(new string('a', (int)CaptureLimits.Default.MaxTextBytes + 1));

        var result = await _service.CaptureTextAsync(Ct);

        result.Error.Code.ShouldBe(ErrorCode.ContentTooLarge);
        _writer.Written.ShouldBeEmpty();
        _library.Clips.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_clipboard_that_cannot_be_written_is_reported_and_the_text_is_not_recorded()
    {
        _writer.Failure = new ResultError(ErrorCode.ClipboardWriteFailed, "ClipBench could not put the text on the clipboard.");

        var result = await _service.CaptureTextAsync(Ct);

        result.Error.Code.ShouldBe(ErrorCode.ClipboardWriteFailed);
        _notifier.Shown.Single().Message.ShouldBe("ClipBench could not put the text on the clipboard.");
        _library.Clips.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_history_that_cannot_be_saved_still_copies_the_text_and_says_the_text_was_copied()
    {
        _store.Failure = new ResultError(ErrorCode.StorageFailure, "disk");

        var result = await _service.CaptureTextAsync(Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Recorded.ShouldBeTrue();
        _writer.Written.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Every_failure_is_logged_by_its_code_alone()
    {
        _recognizer.Answer = Result.Failure<string>(new ResultError(ErrorCode.OcrFailed, "Windows could not read the picture."));

        await _service.CaptureTextAsync(Ct);

        var entry = _log.Entries.Single(e => e.Activity == ActivityEvent.ScreenTextFailed);
        entry.Details.Error.ShouldBe(ErrorCode.OcrFailed);
        entry.Level.ShouldBe(ActivityLevel.Warning);
    }

    // --- one at a time ---

    [Fact]
    public async Task A_second_request_while_one_is_running_is_ignored()
    {
        var release = new TaskCompletionSource();
        var started = new TaskCompletionSource();
        _picker.During = async () =>
        {
            started.SetResult();
            await release.Task;
        };

        var first = _service.CaptureTextAsync(Ct);
        await started.Task;
        var second = await _service.CaptureTextAsync(Ct);
        release.SetResult();
        await first;

        second.Error.Code.ShouldBe(ErrorCode.ScreenTextBusy);
        _picker.Calls.ShouldBe(1);
        _notifier.Shown.Count.ShouldBe(1);
    }

    [Fact]
    public async Task After_one_request_ends_the_next_can_start_even_when_the_first_failed()
    {
        _picker.Answer = Result.Failure<PixelRect>(new ResultError(ErrorCode.SelectionCancelled, "cancelled"));
        await _service.CaptureTextAsync(Ct);
        _picker.Answer = Result.Success(new PixelRect(0, 0, 50, 50));

        var result = await _service.CaptureTextAsync(Ct);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Missing_parts_are_a_bug()
    {
        Should.Throw<ArgumentNullException>(() => new ScreenTextService(null!, _capturer, _recognizer, _writer, _library, _incognito, _notifier, _log, _clock));
        Should.Throw<ArgumentNullException>(() => new ScreenTextService(_picker, null!, _recognizer, _writer, _library, _incognito, _notifier, _log, _clock));
        Should.Throw<ArgumentNullException>(() => new ScreenTextService(_picker, _capturer, null!, _writer, _library, _incognito, _notifier, _log, _clock));
        Should.Throw<ArgumentNullException>(() => new ScreenTextService(_picker, _capturer, _recognizer, null!, _library, _incognito, _notifier, _log, _clock));
        Should.Throw<ArgumentNullException>(() => new ScreenTextService(_picker, _capturer, _recognizer, _writer, null!, _incognito, _notifier, _log, _clock));
        Should.Throw<ArgumentNullException>(() => new ScreenTextService(_picker, _capturer, _recognizer, _writer, _library, null!, _notifier, _log, _clock));
        Should.Throw<ArgumentNullException>(() => new ScreenTextService(_picker, _capturer, _recognizer, _writer, _library, _incognito, null!, _log, _clock));
        Should.Throw<ArgumentNullException>(() => new ScreenTextService(_picker, _capturer, _recognizer, _writer, _library, _incognito, _notifier, null!, _clock));
        Should.Throw<ArgumentNullException>(() => new ScreenTextService(_picker, _capturer, _recognizer, _writer, _library, _incognito, _notifier, _log, null!));
    }
}

public class ScreenImageTests
{
    [Fact]
    public void An_image_holds_its_size_and_its_pixels()
    {
        using var image = new ScreenImage(2, 3, new byte[2 * 3 * 4]);

        image.Width.ShouldBe(2);
        image.Height.ShouldBe(3);
        image.Pixels.Length.ShouldBe(24);
        image.IsDisposed.ShouldBeFalse();
    }

    [Fact]
    public void Letting_go_of_an_image_zeroes_its_pixels_in_the_buffer_it_was_given_and_leaves_none()
    {
        var buffer = Enumerable.Repeat((byte)0xFF, 16).ToArray();
        var image = new ScreenImage(2, 2, buffer);

        image.Dispose();
        image.Dispose();

        buffer.ShouldAllBe(value => value == 0);
        image.Pixels.ShouldBeEmpty();
        image.IsDisposed.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0, 1, 4)]
    [InlineData(1, 0, 4)]
    [InlineData(-1, 1, 4)]
    [InlineData(2, 2, 15)]
    [InlineData(2, 2, 17)]
    public void An_image_whose_buffer_does_not_match_its_size_is_a_bug(int width, int height, int length)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new ScreenImage(width, height, new byte[Math.Max(length, 0)]));
    }

    [Fact]
    public void A_missing_buffer_is_a_bug()
    {
        Should.Throw<ArgumentNullException>(() => new ScreenImage(1, 1, null!));
    }
}
