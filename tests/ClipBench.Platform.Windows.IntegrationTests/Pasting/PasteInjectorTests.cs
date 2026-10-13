using System.Text;
using ClipBench.Core.Capture;
using ClipBench.Core.Clips;
using ClipBench.Core.Hotkeys;
using ClipBench.Core.Pasting;
using ClipBench.Core.Results;
using ClipBench.Platform.Windows.Clipboard;
using ClipBench.Platform.Windows.IntegrationTests.Support;
using ClipBench.Platform.Windows.Pasting;

namespace ClipBench.Platform.Windows.IntegrationTests.Pasting;

public class PasteInjectorTests
{
    private const ushort Control = 0x11;
    private const ushort V = 0x56;
    private static readonly WindowHandle _target = new(0x100);

    private readonly FakeClipboardApi _clipboard = new();
    private readonly FakeInput _input = new();
    private readonly FakeIntegrity _integrity = new();
    private readonly FakeProcessLookup _processes = new() { ProcessId = 4321, ImagePath = @"C:\Windows\notepad.exe" };
    private readonly ManualTimeProvider _time = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private PasteInjector Injector(PasteTiming? timing = null) => new(
        new ClipboardWriter(_clipboard, _time),
        _clipboard,
        new ForegroundTracker(_input, _processes, _integrity),
        _input,
        _input,
        _time,
        timing);

    private static PasteRequest Request(bool restore = true, WindowHandle? target = null, string text = "pasted", TimeSpan? restoreDelay = null) => new()
    {
        Content = new TextContent(text),
        Target = target ?? _target,
        RestorePreviousClipboard = restore,
        RestoreDelay = restoreDelay ?? TimeSpan.FromMilliseconds(250),
    };

    private Task<Result> Paste(PasteRequest request, PasteTiming? timing = null) => _time.RunAsync(Injector(timing).PasteAsync(request, Ct));

    private async Task<Result> PasteOk(PasteRequest request, PasteTiming? timing = null)
    {
        var result = await Paste(request, timing);
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? $"{result.Error.Code}: {result.Error.Message}" : string.Empty);
        return result;
    }

    private string? ClipboardText() => _clipboard.DataOf(ClipboardFormats.UnicodeText) is { } bytes ? Encoding.Unicode.GetString(bytes).TrimEnd('\0') : null;

    private void GiveTheUserSomethingCopied()
    {
        _clipboard.PutText("what the user had");
        _clipboard.PutHtml("<b>what the user had</b>");
        _clipboard.PutMarker(ClipboardFormatNames.ExcludeFromMonitorProcessing);
    }

    // --- the paste itself ---

    [Fact]
    public async Task The_content_is_on_the_clipboard_when_the_keys_are_sent_and_the_keys_are_control_v()
    {
        string? atSend = null;
        _input.OnSend = () => atSend = ClipboardText();

        await PasteOk(Request(text: "hello"));

        atSend.ShouldBe("hello");
        _input.Sent.ShouldBe([[new KeyEvent(Control, false), new KeyEvent(V, false), new KeyEvent(V, true), new KeyEvent(Control, true)]]);
    }

    [Fact]
    public async Task Focus_is_given_back_to_the_target_first_and_a_moment_is_allowed_for_it_to_settle()
    {
        await PasteOk(Request());

        _input.FocusRequests.ShouldBe([0x100]);
        _time.RequestedDelays.ShouldContain(TimeSpan.FromMilliseconds(30), "the settle time before keys are sent");
    }

    [Fact]
    public async Task A_target_that_already_has_focus_is_not_asked_again_and_not_waited_for()
    {
        _input.Foreground = 0x100;

        await PasteOk(Request(restore: false));

        _input.FocusRequests.ShouldBeEmpty();
        _time.RequestedDelays.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_target_that_takes_a_few_checks_to_get_focus_is_waited_for()
    {
        _input.FocusMovesAfterPolls = 2;

        await PasteOk(Request(restore: false));

        _time.RequestedDelays.Count(delay => delay == TimeSpan.FromMilliseconds(20)).ShouldBe(2);
        _input.Sent.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Pasting_without_a_target_sends_the_keys_to_whatever_has_focus_and_checks_nothing_about_it()
    {
        // Queue paste: the user is already in the window and holds Ctrl while tapping V.
        _input.Windows.Clear();

        await PasteOk(new PasteRequest { Content = new TextContent("queued"), RestorePreviousClipboard = false });

        _input.FocusRequests.ShouldBeEmpty();
        _input.Sent.Count.ShouldBe(1);
        ClipboardText().ShouldBe("queued");
    }

    [Fact]
    public async Task Control_already_held_by_the_user_is_left_alone()
    {
        _input.Held = HotkeyModifiers.Control;

        await PasteOk(Request(restore: false));

        _input.Sent.ShouldBe([[new KeyEvent(V, false), new KeyEvent(V, true)]]);
    }

    // --- refusing to paste where it would not work ---

    [Fact]
    public async Task A_target_that_is_gone_is_reported_and_nothing_is_touched()
    {
        GiveTheUserSomethingCopied();
        _input.Windows.Remove(0x100);
        _clipboard.Log.Clear();

        var result = await Paste(Request());

        result.Error.Code.ShouldBe(ErrorCode.TargetNotFound);
        _clipboard.Log.ShouldBeEmpty();
        _input.Sent.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_target_with_more_rights_than_ClipBench_is_reported_instead_of_failing_silently_and_nothing_is_touched()
    {
        GiveTheUserSomethingCopied();
        _integrity.Levels[4321] = new IntegrityResult(IntegrityOutcome.Known, 0x3000);
        _clipboard.Log.Clear();

        var result = await Paste(Request());

        result.Error.Code.ShouldBe(ErrorCode.TargetElevated);
        _clipboard.Log.ShouldBeEmpty("the user's clipboard was not even opened");
        _input.Sent.ShouldBeEmpty();
        _input.FocusRequests.ShouldBeEmpty();
    }

    [Fact]
    public async Task If_focus_cannot_be_given_back_no_key_is_sent_and_the_users_clipboard_is_put_back()
    {
        GiveTheUserSomethingCopied();
        _input.FocusMovesAfterPolls = -1;

        var result = await Paste(Request());

        result.Error.Code.ShouldBe(ErrorCode.FocusNotRestored);
        _input.Sent.ShouldBeEmpty("keys sent to the wrong window could type into it");
        ClipboardText().ShouldBe("what the user had");
        _time.RequestedDelays.Count(delay => delay == TimeSpan.FromMilliseconds(20)).ShouldBe(50, "one second of checking, then give up");
    }

    [Fact]
    public async Task If_another_program_changes_the_clipboard_before_the_keys_no_key_is_sent_and_their_content_is_left_alone()
    {
        GiveTheUserSomethingCopied();
        _input.OnFocusRequest = () => _clipboard.PutText("someone else copied this");

        var result = await Paste(Request());

        result.Error.Code.ShouldBe(ErrorCode.ClipboardChangedBeforePaste);
        _input.Sent.ShouldBeEmpty();
        ClipboardText().ShouldBe("someone else copied this", "restoring would overwrite what they just copied");
    }

    [Fact]
    public async Task Keys_still_held_down_by_the_user_are_waited_for_and_then_the_paste_goes_ahead()
    {
        _input.Held = HotkeyModifiers.Windows | HotkeyModifiers.Alt;
        _input.ReleaseAfterPolls = 3;

        await PasteOk(Request(restore: false));

        _time.RequestedDelays.Count(delay => delay == TimeSpan.FromMilliseconds(20)).ShouldBe(3);
        _input.Sent.Count.ShouldBe(1);
        _input.Sent[0].Length.ShouldBe(4);
    }

    [Theory]
    [InlineData(HotkeyModifiers.Alt)]
    [InlineData(HotkeyModifiers.Shift)]
    [InlineData(HotkeyModifiers.Windows)]
    [InlineData(HotkeyModifiers.Control | HotkeyModifiers.Alt)]
    public async Task Keys_that_stay_held_down_stop_the_paste_because_Ctrl_V_would_become_another_shortcut(HotkeyModifiers held)
    {
        GiveTheUserSomethingCopied();
        _input.Held = held;

        var result = await Paste(Request());

        result.Error.Code.ShouldBe(ErrorCode.InputBlocked);
        _input.Sent.ShouldBeEmpty();
        ClipboardText().ShouldBe("what the user had");
    }

    [Fact]
    public async Task If_Windows_accepts_only_some_of_the_keys_the_paste_is_reported_as_blocked_and_the_clipboard_is_put_back()
    {
        GiveTheUserSomethingCopied();
        _input.AcceptOnly = 0;

        var result = await Paste(Request());

        result.Error.Code.ShouldBe(ErrorCode.InputBlocked);
        ClipboardText().ShouldBe("what the user had");
    }

    [Fact]
    public async Task A_clipboard_that_stays_busy_fails_the_paste_before_any_key_is_sent()
    {
        _clipboard.BusyAttempts = int.MaxValue;

        var result = await Paste(Request());

        result.Error.Code.ShouldBe(ErrorCode.ClipboardBusy);
        _input.Sent.ShouldBeEmpty();
    }

    [Fact]
    public async Task Cancelling_while_waiting_for_focus_ends_the_paste_puts_the_clipboard_back_and_sends_no_key()
    {
        GiveTheUserSomethingCopied();
        _input.FocusMovesAfterPolls = -1;
        using var cancellation = new CancellationTokenSource();
        var pasting = Injector().PasteAsync(Request(), cancellation.Token);

        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(pasting);
        _input.Sent.ShouldBeEmpty();
        ClipboardText().ShouldBe("what the user had");
    }

    // --- putting the user's clipboard back ---

    [Fact]
    public async Task After_the_paste_the_clipboard_the_user_had_comes_back_format_for_format_privacy_marker_included()
    {
        GiveTheUserSomethingCopied();
        var before = _clipboard.FormatIds.ToDictionary(id => id, id => _clipboard.DataOf(id)!);

        await PasteOk(Request());

        foreach (var (id, data) in before)
        {
            _clipboard.DataOf(id).ShouldBe(data, $"format {id} was restored");
        }

        _clipboard.Has(ClipboardFormatNames.ExcludeFromMonitorProcessing).ShouldBeTrue();
    }

    [Fact]
    public async Task The_restore_waits_the_requested_time_after_the_keys_so_the_target_can_read_the_clipboard_first()
    {
        GiveTheUserSomethingCopied();
        string? duringWait = null;
        _input.OnSend = () => duringWait = ClipboardText();

        await PasteOk(Request(restoreDelay: TimeSpan.FromSeconds(2)));

        _time.RequestedDelays[^1].ShouldBe(TimeSpan.FromSeconds(2));
        duringWait.ShouldBe("pasted");
        ClipboardText().ShouldBe("what the user had");
    }

    [Fact]
    public async Task The_restored_clipboard_is_tagged_as_ClipBenchs_own_so_it_is_not_recorded_again_as_a_new_copy()
    {
        GiveTheUserSomethingCopied();

        await PasteOk(Request());

        _clipboard.Has(ClipboardFormatNames.OwnWrite).ShouldBeTrue();
    }

    [Fact]
    public async Task If_another_program_copies_during_the_wait_their_copy_is_not_overwritten_by_the_restore()
    {
        GiveTheUserSomethingCopied();
        _input.OnSend = () => _clipboard.PutText("copied while the paste was landing");

        await PasteOk(Request());

        ClipboardText().ShouldBe("copied while the paste was landing");
    }

    [Fact]
    public async Task Without_restore_the_pasted_content_stays_on_the_clipboard_and_nothing_is_copied_first()
    {
        GiveTheUserSomethingCopied();
        _clipboard.Log.Clear();

        await PasteOk(Request(restore: false));

        ClipboardText().ShouldBe("pasted");
        _clipboard.Log.Count(entry => entry == "open").ShouldBe(1, "only the write opened the clipboard");
    }

    [Fact]
    public async Task A_clipboard_too_large_to_copy_is_not_restored_but_the_paste_still_works()
    {
        GiveTheUserSomethingCopied();

        await PasteOk(Request(), new PasteTiming { SnapshotLimit = 10 });

        _input.Sent.Count.ShouldBe(1);
        ClipboardText().ShouldBe("pasted");
    }

    [Fact]
    public async Task An_empty_clipboard_is_restored_as_empty()
    {
        await PasteOk(Request());

        _clipboard.Has(ClipboardFormats.UnicodeText).ShouldBeFalse();
    }
}
