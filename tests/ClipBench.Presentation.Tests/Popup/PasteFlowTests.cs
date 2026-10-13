using ClipBench.Core.Clips;
using ClipBench.Core.Diagnostics;
using ClipBench.Core.Pasting;
using ClipBench.Core.Results;
using ClipBench.Presentation.Popup;
using ClipBench.Presentation.Tests.Support;

namespace ClipBench.Presentation.Tests.Popup;

public class PasteFlowTests
{
    private readonly FakeForegroundTracker _tracker = new();
    private readonly FakePasteInjector _injector = new();
    private readonly FakeClipboardWriter _writer = new();
    private readonly FakeNotifier _notifier = new();
    private readonly FakeActivityLog _log = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private PasteFlow Create(PasteFlowOptions? options = null) => new(_tracker, _injector, _writer, _notifier, _log, options);

    private static Clip Clip(string text = "hello") => new(ClipId.New(), new TextContent(text), DateTimeOffset.UnixEpoch);

    private static Result Failed(ErrorCode code) => Result.Failure(new ResultError(code, "details"));

    // --- where to paste ---

    [Fact]
    public async Task A_paste_goes_to_the_window_that_had_focus_when_the_flow_began()
    {
        var flow = Create();
        flow.Begin();
        var clip = Clip("pasted text");

        await flow.PasteAsync(clip, Ct);

        var request = _injector.Requests.Single();
        request.Target.ShouldBe(new WindowHandle(0x1234));
        request.Content.ShouldBe(clip.Content);
        request.RestorePreviousClipboard.ShouldBeTrue();
        request.RestoreDelay.ShouldBe(TimeSpan.FromMilliseconds(250));
        _tracker.Captures.ShouldBe(1);
    }

    [Fact]
    public async Task The_options_decide_whether_and_when_the_clipboard_is_put_back()
    {
        var flow = Create(new PasteFlowOptions { RestorePreviousClipboard = false, RestoreDelay = TimeSpan.FromSeconds(1) });
        flow.Begin();

        await flow.PasteAsync(Clip(), Ct);

        var request = _injector.Requests.Single();
        request.RestorePreviousClipboard.ShouldBeFalse();
        request.RestoreDelay.ShouldBe(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Without_a_window_on_record_the_paste_goes_to_whatever_has_focus()
    {
        _tracker.Next = Result.Failure<ForegroundTarget>(new ResultError(ErrorCode.TargetNotFound, "no window"));
        var flow = Create();
        flow.Begin();

        await flow.PasteAsync(Clip(), Ct);

        _injector.Requests.Single().Target.ShouldBeNull();
    }

    [Fact]
    public async Task Without_asking_for_a_target_the_tracker_is_left_alone_and_the_paste_goes_to_whatever_has_focus()
    {
        // The popup was opened from the tray icon, so the window that had focus is the taskbar, which is no place to paste.
        var flow = Create();
        flow.Begin(recordTarget: false);

        await flow.PasteAsync(Clip(), Ct);

        _tracker.Captures.ShouldBe(0);
        _injector.Requests.Single().Target.ShouldBeNull();
    }

    [Fact]
    public async Task Beginning_without_a_target_forgets_an_earlier_one()
    {
        var flow = Create();
        flow.Begin();
        flow.Begin(recordTarget: false);

        await flow.PasteAsync(Clip(), Ct);

        _injector.Requests.Single().Target.ShouldBeNull();
    }

    [Fact]
    public async Task A_window_on_record_is_used_once_so_a_later_paste_never_lands_in_a_stale_window()
    {
        var flow = Create();
        flow.Begin();
        await flow.PasteAsync(Clip("first"), Ct);

        await flow.PasteAsync(Clip("second"), Ct);

        _injector.Requests[0].Target.ShouldBe(new WindowHandle(0x1234));
        _injector.Requests[1].Target.ShouldBeNull();
    }

    [Fact]
    public async Task Beginning_again_replaces_the_window_on_record()
    {
        var flow = Create();
        flow.Begin();
        _tracker.Next = Result.Success(new ForegroundTarget(new WindowHandle(0x9999), 7, "code.exe", BlocksInputFromUs: false));
        flow.Begin();

        await flow.PasteAsync(Clip(), Ct);

        _injector.Requests.Single().Target.ShouldBe(new WindowHandle(0x9999));
    }

    // --- success ---

    [Fact]
    public async Task A_paste_that_worked_is_logged_by_kind_and_size_and_says_nothing_to_the_user()
    {
        var flow = Create();
        flow.Begin();

        await flow.PasteAsync(Clip("twelve bytes"), Ct);

        _notifier.Shown.ShouldBeEmpty();
        _writer.Written.ShouldBeEmpty();
        var entry = _log.Entries.Single();
        entry.Level.ShouldBe(ActivityLevel.Information);
        entry.Activity.ShouldBe(ActivityEvent.PasteSucceeded);
        entry.Details.Format.ShouldBe(ClipFormat.Text);
        entry.Details.Bytes.ShouldBe(12);
    }

    // --- failures ---

    [Theory]
    [InlineData(ErrorCode.TargetNotFound, "The window you were in has closed, so nothing was pasted.")]
    [InlineData(ErrorCode.FocusNotRestored, "ClipBench could not return to the window you were in, so nothing was pasted.")]
    [InlineData(ErrorCode.ClipboardChangedBeforePaste, "Another program changed the clipboard during the paste, so nothing was pasted.")]
    [InlineData(ErrorCode.ClipboardBusy, "Another program is using the clipboard. Try again in a moment.")]
    [InlineData(ErrorCode.ClipboardWriteFailed, "ClipBench could not put the clip on the clipboard.")]
    [InlineData(ErrorCode.PlatformFailure, "The paste did not work.")]
    public async Task A_paste_that_failed_tells_the_user_why_in_plain_words(ErrorCode code, string expected)
    {
        _injector.Outcome = Failed(code);
        var flow = Create();
        flow.Begin();

        await flow.PasteAsync(Clip(), Ct);

        _notifier.Shown.ShouldBe([("ClipBench", expected)]);
        _writer.Written.ShouldBeEmpty();
        var entry = _log.Entries.Single();
        entry.Level.ShouldBe(ActivityLevel.Warning);
        entry.Activity.ShouldBe(ActivityEvent.PasteFailed);
        entry.Details.Error.ShouldBe(code);
    }

    [Fact]
    public async Task An_administrator_window_gets_the_clip_on_the_clipboard_instead_and_the_user_is_told()
    {
        _injector.Outcome = Failed(ErrorCode.TargetElevated);
        var clip = Clip("for the admin window");
        var flow = Create();
        flow.Begin();

        await flow.PasteAsync(clip, Ct);

        _writer.Written.ShouldBe([clip.Content]);
        _notifier.Shown.ShouldBe(
        [
            ("ClipBench", "Windows does not let ClipBench paste into a window that runs as administrator. The clip is on your clipboard: press Ctrl+V."),
        ]);
        _log.Entries.Single().Details.Error.ShouldBe(ErrorCode.TargetElevated);
    }

    [Fact]
    public async Task Blocked_keystrokes_also_leave_the_clip_on_the_clipboard()
    {
        _injector.Outcome = Failed(ErrorCode.InputBlocked);
        var clip = Clip();
        var flow = Create();
        flow.Begin();

        await flow.PasteAsync(clip, Ct);

        _writer.Written.ShouldBe([clip.Content]);
        _notifier.Shown.Single().Message.ShouldBe("Windows blocked the paste. The clip is on your clipboard: press Ctrl+V.");
    }

    [Theory]
    [InlineData(ErrorCode.TargetElevated, "Windows does not let ClipBench paste into a window that runs as administrator.")]
    [InlineData(ErrorCode.InputBlocked, "Windows blocked the paste.")]
    public async Task When_even_the_clipboard_fallback_fails_the_notice_does_not_promise_it(ErrorCode code, string expected)
    {
        _injector.Outcome = Failed(code);
        _writer.Outcome = Result.Failure<long>(new ResultError(ErrorCode.ClipboardBusy, "busy"));
        var flow = Create();
        flow.Begin();

        await flow.PasteAsync(Clip(), Ct);

        _notifier.Shown.Single().Message.ShouldBe(expected);
    }

    // --- stopping and bugs ---

    [Fact]
    public async Task A_cancelled_paste_says_nothing()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var flow = Create();
        flow.Begin();

        await flow.PasteAsync(Clip(), cancelled.Token);

        _notifier.Shown.ShouldBeEmpty();
        _log.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_unexpected_exception_is_logged_by_type_and_the_user_gets_a_plain_notice()
    {
        _injector.Throws = new InvalidOperationException("secret text in a message");
        var flow = Create();
        flow.Begin();

        await flow.PasteAsync(Clip(), Ct);

        var entry = _log.Entries.Single();
        entry.Level.ShouldBe(ActivityLevel.Error);
        entry.Activity.ShouldBe(ActivityEvent.PasteFaulted);
        entry.Details.ExceptionType.ShouldBe(typeof(InvalidOperationException));
        _notifier.Shown.ShouldBe([("ClipBench", "The paste did not work.")]);
    }
}
