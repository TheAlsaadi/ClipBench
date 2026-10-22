using ClipBench.Core.Clips;
using ClipBench.Core.Diagnostics;
using ClipBench.Core.Hotkeys;
using ClipBench.Core.Pasting;
using ClipBench.Core.Results;
using ClipBench.Core.Settings;
using ClipBench.Core.Tests.Support;

namespace ClipBench.Core.Tests.Pasting;

public sealed class QueueRunnerTests : IAsyncDisposable
{
    private static readonly Hotkey _ctrlV = new(HotkeyModifiers.Control, 'V');
    private static readonly Hotkey _esc = new(HotkeyModifiers.None, 0x1B);

    private readonly FakeHotkeyService _hotkeys = new();
    private readonly FakePasteInjector _injector = new();
    private readonly FakeForegroundTracker _tracker = new();
    private readonly FakeNotifier _notifier = new();
    private readonly FakeActivityLog _log = new();
    private readonly SettingsService _settings = new(new FakeSettingsStore());
    private readonly QueueRunner _runner;

    public QueueRunnerTests()
    {
        _runner = new QueueRunner(_hotkeys, _injector, _tracker, _settings, _notifier, _log);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync()
    {
        await _runner.DisposeAsync();
        _settings.Dispose();
    }

    private static Clip[] Texts(params string[] texts) => [.. texts.Select(text => TestClips.Text(text))];

    private string[] PastedTexts() => [.. _injector.Requests.Select(request => ((TextContent)request.Content).Text)];

    private async Task StartAsync(params string[] texts) => (await _runner.StartAsync(Texts(texts), Ct)).IsSuccess.ShouldBeTrue();

    // --- starting ---

    [Fact]
    public void The_queue_keys_are_ctrl_v_and_a_plain_esc()
    {
        QueueRunner.PasteKey.ShouldBe(_ctrlV);
        QueueRunner.StopKey.ShouldBe(_esc);
    }

    [Fact]
    public void Before_a_start_nothing_runs_and_nothing_is_claimed()
    {
        _runner.Progress.ShouldBe(new QueueProgress(QueueState.Idle, 0, 0));
        _runner.Progress.IsRunning.ShouldBeFalse();
        _hotkeys.Registered.ShouldBeEmpty();
    }

    [Fact]
    public async Task Starting_claims_ctrl_v_and_esc_and_waits_for_the_first_paste()
    {
        await StartAsync("name", "street", "city");

        _hotkeys.Registered.ShouldBe([_ctrlV, _esc], ignoreOrder: true);
        _runner.Progress.ShouldBe(new QueueProgress(QueueState.Armed, 0, 3));
        _runner.Progress.IsRunning.ShouldBeTrue();
        _injector.Requests.ShouldBeEmpty();
        _log.Entries.ShouldContain(entry => entry.Activity == ActivityEvent.QueueStarted && entry.Details.Count == 3);
    }

    [Fact]
    public async Task Starting_with_nothing_selected_fails_and_claims_nothing()
    {
        var result = await _runner.StartAsync([], Ct);

        result.Error.Code.ShouldBe(ErrorCode.NothingSelected);
        _hotkeys.Calls.ShouldBeEmpty();
        _runner.Progress.IsRunning.ShouldBeFalse();
    }

    [Fact]
    public async Task When_another_program_has_ctrl_v_the_queue_does_not_start_and_says_why()
    {
        _hotkeys.Taken.Add(_ctrlV);

        var result = await _runner.StartAsync(Texts("a"), Ct);

        result.Error.Code.ShouldBe(ErrorCode.HotkeyUnavailable);
        result.Error.Message.ShouldBe("Another program is using Ctrl+V, so the queue cannot start.");
        _hotkeys.Registered.ShouldBeEmpty();
        _runner.Progress.IsRunning.ShouldBeFalse();
    }

    [Fact]
    public async Task When_another_program_has_esc_the_queue_does_not_start_and_lets_go_of_ctrl_v()
    {
        _hotkeys.Taken.Add(_esc);

        var result = await _runner.StartAsync(Texts("a"), Ct);

        result.Error.Code.ShouldBe(ErrorCode.HotkeyUnavailable);
        result.Error.Message.ShouldBe("Another program is using Esc, so the queue cannot start.");
        _hotkeys.Registered.ShouldBeEmpty();
        _hotkeys.Calls.ShouldBe(["+Ctrl+V", "-Ctrl+V"]);
        _runner.Progress.IsRunning.ShouldBeFalse();
    }

    // --- pasting ---

    [Fact]
    public async Task Each_ctrl_v_pastes_the_next_clip_in_order_into_the_window_that_has_focus()
    {
        await StartAsync("name", "street", "city");

        _hotkeys.Press(_ctrlV);
        _runner.Progress.ShouldBe(new QueueProgress(QueueState.Armed, 1, 3));
        _hotkeys.Press(_ctrlV);

        PastedTexts().ShouldBe(["name", "street"]);
        _injector.Requests.ShouldAllBe(request => request.Target == FakeForegroundTracker.Window);
        _runner.Progress.ShouldBe(new QueueProgress(QueueState.Armed, 2, 3));
    }

    [Fact]
    public async Task Ctrl_v_is_let_go_while_ClipBench_presses_it_itself_and_claimed_again_after()
    {
        var claimedDuringPaste = new List<bool>();
        _injector.OnPaste = _ => claimedDuringPaste.Add(_hotkeys.IsRegistered(_ctrlV));
        await StartAsync("one", "two", "three");

        _hotkeys.Press(_ctrlV);

        claimedDuringPaste.ShouldBe([false]);
        _hotkeys.IsRegistered(_ctrlV).ShouldBeTrue();
        _hotkeys.IsRegistered(_esc).ShouldBeTrue();
    }

    [Fact]
    public async Task Without_a_window_with_focus_the_paste_goes_to_whatever_has_it()
    {
        _tracker.HasFocusWindow = false;
        await StartAsync("one");

        _hotkeys.Press(_ctrlV);

        _injector.Requests.Single().Target.ShouldBeNull();
    }

    [Fact]
    public async Task Each_paste_follows_the_restore_setting_in_force_at_that_moment()
    {
        await StartAsync("one", "two");

        _hotkeys.Press(_ctrlV);
        await _settings.UpdateAsync(settings => settings with { RestorePreviousClipboard = false }, Ct);
        _hotkeys.Press(_ctrlV);

        _injector.Requests.Select(request => request.RestorePreviousClipboard).ShouldBe([true, false]);
    }

    [Fact]
    public async Task Pictures_and_rich_text_are_pasted_as_they_are()
    {
        var image = TestClips.Image(3);
        (await _runner.StartAsync([image], Ct)).IsSuccess.ShouldBeTrue();

        _hotkeys.Press(_ctrlV);

        _injector.Requests.Single().Content.ShouldBe(image.Content);
    }

    [Fact]
    public async Task After_the_last_clip_both_keys_go_back_and_the_queue_says_it_is_done()
    {
        await StartAsync("one", "two");

        _hotkeys.Press(_ctrlV);
        _hotkeys.Press(_ctrlV);

        _hotkeys.Registered.ShouldBeEmpty();
        _runner.Progress.ShouldBe(new QueueProgress(QueueState.Exhausted, 2, 2));
        _runner.Progress.IsRunning.ShouldBeFalse();
        _log.Entries.ShouldContain(entry => entry.Activity == ActivityEvent.QueueFinished);
    }

    [Fact]
    public async Task A_press_reported_after_the_queue_ended_pastes_nothing()
    {
        await StartAsync("one");
        _hotkeys.Press(_ctrlV);

        _hotkeys.PressId(100);

        _injector.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_press_of_a_shortcut_that_is_not_the_queue_s_is_ignored()
    {
        await StartAsync("one");

        _hotkeys.PressId(1);

        _injector.Requests.ShouldBeEmpty();
        _runner.Progress.State.ShouldBe(QueueState.Armed);
    }

    [Fact]
    public async Task A_second_ctrl_v_reported_while_a_paste_runs_is_dropped()
    {
        var paste = new TaskCompletionSource<Result>();
        _injector.Answer = _ => paste.Task;
        await StartAsync("one", "two");
        var ctrlVId = 100;

        _hotkeys.Press(_ctrlV);
        _hotkeys.PressId(ctrlVId);
        paste.SetResult(Result.Success());

        PastedTexts().ShouldBe(["one"]);
        _runner.Progress.ShouldBe(new QueueProgress(QueueState.Armed, 1, 2));
    }

    // --- stopping ---

    [Fact]
    public async Task Esc_stops_the_queue_and_gives_both_keys_back()
    {
        await StartAsync("one", "two");
        _hotkeys.Press(_ctrlV);

        _hotkeys.Press(_esc);

        _hotkeys.Registered.ShouldBeEmpty();
        _runner.Progress.ShouldBe(new QueueProgress(QueueState.Cancelled, 1, 2));
        _log.Entries.ShouldContain(entry => entry.Activity == ActivityEvent.QueueStopped);
    }

    [Fact]
    public async Task Stopping_from_elsewhere_gives_both_keys_back()
    {
        await StartAsync("one", "two");

        await _runner.StopAsync(Ct);

        _hotkeys.Registered.ShouldBeEmpty();
        _runner.Progress.State.ShouldBe(QueueState.Cancelled);
    }

    [Fact]
    public async Task Stopping_when_nothing_runs_does_nothing()
    {
        await _runner.StopAsync(Ct);

        _hotkeys.Calls.ShouldBeEmpty();
        _runner.Progress.State.ShouldBe(QueueState.Idle);
    }

    [Fact]
    public async Task Esc_during_a_paste_lets_that_paste_finish_then_stops_without_claiming_ctrl_v_again()
    {
        var paste = new TaskCompletionSource<Result>();
        _injector.Answer = _ => paste.Task;
        await StartAsync("one", "two");

        _hotkeys.Press(_ctrlV);
        _hotkeys.Press(_esc);
        paste.SetResult(Result.Success());

        _hotkeys.Registered.ShouldBeEmpty();
        _runner.Progress.State.ShouldBe(QueueState.Cancelled);
        _injector.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_paste_that_fails_stops_the_queue_gives_the_keys_back_and_tells_the_user()
    {
        _injector.Answer = _ => Task.FromResult(Result.Failure(new ResultError(ErrorCode.InputBlocked, "blocked")));
        await StartAsync("one", "two");

        _hotkeys.Press(_ctrlV);

        _hotkeys.Registered.ShouldBeEmpty();
        _runner.Progress.State.ShouldBe(QueueState.Cancelled);
        _notifier.Shown.ShouldBe([("ClipBench", "The queue stopped because the paste did not work.")]);
        _log.Entries.ShouldContain(entry => entry.Activity == ActivityEvent.QueueFailed && entry.Details.Error == ErrorCode.InputBlocked);
    }

    [Fact]
    public async Task A_paste_into_an_administrator_window_stops_the_queue_and_says_so()
    {
        _injector.Answer = _ => Task.FromResult(Result.Failure(new ResultError(ErrorCode.TargetElevated, "elevated")));
        await StartAsync("one");

        _hotkeys.Press(_ctrlV);

        _notifier.Shown.Single().Message.ShouldBe(
            "The queue stopped: Windows does not let ClipBench paste into a window that runs as administrator.");
    }

    [Fact]
    public async Task A_paste_that_throws_stops_the_queue_and_gives_the_keys_back()
    {
        _injector.Answer = _ => throw new InvalidOperationException("bug");
        await StartAsync("one", "two");

        _hotkeys.Press(_ctrlV);

        _hotkeys.Registered.ShouldBeEmpty();
        _runner.Progress.State.ShouldBe(QueueState.Cancelled);
        _notifier.Shown.ShouldHaveSingleItem();
        _log.Entries.ShouldContain(entry => entry.Activity == ActivityEvent.QueueFaulted && entry.Details.ExceptionType == typeof(InvalidOperationException));
    }

    [Fact]
    public async Task When_ctrl_v_cannot_be_claimed_again_the_queue_stops_and_says_why()
    {
        await StartAsync("one", "two");
        _hotkeys.FailNextClaimOf = _ctrlV;

        _hotkeys.Press(_ctrlV);

        _hotkeys.Registered.ShouldBeEmpty();
        _runner.Progress.State.ShouldBe(QueueState.Cancelled);
        _notifier.Shown.Single().Message.ShouldBe("The queue stopped because ClipBench could not take Ctrl+V back from another program.");
    }

    [Fact]
    public async Task Starting_a_new_queue_replaces_the_running_one()
    {
        await StartAsync("old one", "old two");

        await StartAsync("new");

        _hotkeys.Registered.ShouldBe([_ctrlV, _esc], ignoreOrder: true);
        _hotkeys.Calls.ShouldBe(["+Ctrl+V", "+Esc", "-Ctrl+V", "-Esc", "+Ctrl+V", "+Esc"]);
        _runner.Progress.ShouldBe(new QueueProgress(QueueState.Armed, 0, 1));
        _hotkeys.Press(_ctrlV);
        PastedTexts().ShouldBe(["new"]);
    }

    [Fact]
    public async Task A_finished_queue_can_be_followed_by_another()
    {
        await StartAsync("one");
        _hotkeys.Press(_ctrlV);

        await StartAsync("two");
        _hotkeys.Press(_ctrlV);

        PastedTexts().ShouldBe(["one", "two"]);
    }

    // --- progress ---

    [Fact]
    public async Task Every_step_is_announced()
    {
        var states = new List<QueueProgress>();
        _runner.Changed += (_, _) => states.Add(_runner.Progress);

        await StartAsync("one", "two");
        _hotkeys.Press(_ctrlV);
        _hotkeys.Press(_esc);

        states.ShouldBe(
        [
            new QueueProgress(QueueState.Armed, 0, 2),
            new QueueProgress(QueueState.Armed, 1, 2),
            new QueueProgress(QueueState.Cancelled, 1, 2),
        ]);
    }

    [Fact]
    public async Task A_subscriber_that_throws_does_not_stop_the_queue()
    {
        _runner.Changed += (_, _) => throw new InvalidOperationException("subscriber bug");
        await StartAsync("one", "two");

        _hotkeys.Press(_ctrlV);

        PastedTexts().ShouldBe(["one"]);
        _hotkeys.IsRegistered(_ctrlV).ShouldBeTrue();
    }

    // --- the end of the app ---

    [Fact]
    public async Task Disposing_stops_the_queue_and_gives_both_keys_back()
    {
        await StartAsync("one", "two");

        await _runner.DisposeAsync();

        _hotkeys.Registered.ShouldBeEmpty();
        _hotkeys.SubscriberCount.ShouldBe(0);
        _runner.Progress.State.ShouldBe(QueueState.Cancelled);
    }

    [Fact]
    public async Task Disposing_twice_is_harmless_and_a_start_afterwards_is_a_bug()
    {
        await _runner.DisposeAsync();
        await _runner.DisposeAsync();

        await Should.ThrowAsync<ObjectDisposedException>(() => _runner.StartAsync(Texts("a"), Ct));
    }

    [Fact]
    public async Task The_log_holds_counts_and_codes_never_the_clips()
    {
        await StartAsync("secret one", "secret two");
        _hotkeys.Press(_ctrlV);
        _hotkeys.Press(_ctrlV);

        _log.Entries.Select(entry => entry.Activity).ShouldBe(
            [ActivityEvent.QueueStarted, ActivityEvent.QueuePasted, ActivityEvent.QueuePasted, ActivityEvent.QueueFinished]);
        _log.Entries[1].Details.Bytes.ShouldBe(10);
    }

    [Fact]
    public void Missing_services_are_a_bug()
    {
        Should.Throw<ArgumentNullException>(() => new QueueRunner(null!, _injector, _tracker, _settings, _notifier, _log));
        Should.Throw<ArgumentNullException>(() => new QueueRunner(_hotkeys, null!, _tracker, _settings, _notifier, _log));
        Should.Throw<ArgumentNullException>(() => new QueueRunner(_hotkeys, _injector, null!, _settings, _notifier, _log));
        Should.Throw<ArgumentNullException>(() => new QueueRunner(_hotkeys, _injector, _tracker, null!, _notifier, _log));
        Should.Throw<ArgumentNullException>(() => new QueueRunner(_hotkeys, _injector, _tracker, _settings, null!, _log));
        Should.Throw<ArgumentNullException>(() => new QueueRunner(_hotkeys, _injector, _tracker, _settings, _notifier, null!));
    }
}
