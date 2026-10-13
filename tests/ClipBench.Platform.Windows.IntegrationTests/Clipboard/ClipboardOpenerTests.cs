using ClipBench.Platform.Windows.Clipboard;
using ClipBench.Platform.Windows.IntegrationTests.Support;

namespace ClipBench.Platform.Windows.IntegrationTests.Clipboard;

public class ClipboardOpenerTests
{
    private readonly FakeClipboardApi _api = new();
    private readonly ManualTimeProvider _time = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Task<ClipboardUse<int>> Use(Func<IClipboardSession, int>? work = null, ClipboardRetryPolicy? policy = null) =>
        _time.RunAsync(ClipboardOpener.UseAsync(_api, policy ?? ClipboardRetryPolicy.Default, _time, work ?? (_ => 7), Ct));

    [Fact]
    public async Task A_clipboard_that_is_free_runs_the_work_at_once_without_waiting_and_closes_afterwards()
    {
        var use = await Use(session => session.Formats().Count);

        use.Opened.ShouldBeTrue();
        use.Value.ShouldBe(0);
        _time.RequestedDelays.ShouldBeEmpty();
        _api.Log.ShouldBe(["open", "close"]);
        _api.IsOpen.ShouldBeFalse();
    }

    [Fact]
    public async Task The_work_runs_while_the_clipboard_is_open_and_its_result_comes_back()
    {
        var wasOpen = false;

        var use = await Use(_ =>
        {
            wasOpen = _api.IsOpen;
            return 42;
        });

        wasOpen.ShouldBeTrue();
        use.Value.ShouldBe(42);
    }

    [Fact]
    public async Task Opening_the_work_and_closing_all_happen_on_one_thread_even_after_waiting_for_a_busy_clipboard()
    {
        // Windows wants the thread that opened the clipboard to close it. The wait before a retry can move the code
        // to another thread, so the work has to run in the same stretch as the open, not after a task has been returned.
        _api.BusyAttempts = 3;
        var workThread = 0;

        await Use(_ =>
        {
            workThread = Environment.CurrentManagedThreadId;
            return 0;
        });

        _api.OpenedOnThread.ShouldBe(workThread);
        _api.ClosedOnThread.ShouldBe(workThread);
    }

    [Fact]
    public async Task The_clipboard_is_closed_even_when_the_work_throws_and_the_exception_reaches_the_caller()
    {
        var failing = _time.RunAsync(ClipboardOpener.UseAsync<int>(_api, ClipboardRetryPolicy.Default, _time, _ => throw new InvalidOperationException("boom"), Ct));

        (await Should.ThrowAsync<InvalidOperationException>(failing)).Message.ShouldBe("boom");
        _api.IsOpen.ShouldBeFalse();
    }

    [Fact]
    public async Task A_clipboard_held_by_another_program_is_tried_again_with_a_doubling_wait()
    {
        _api.BusyAttempts = 3;

        var use = await Use(policy: new ClipboardRetryPolicy(Attempts: 8, InitialDelay: TimeSpan.FromMilliseconds(10), MaxDelay: TimeSpan.FromSeconds(1)));

        use.Opened.ShouldBeTrue();
        _time.RequestedDelays.ShouldBe([TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(40)]);
    }

    [Fact]
    public async Task The_wait_stops_doubling_at_the_maximum()
    {
        _api.BusyAttempts = 5;

        var use = await Use(policy: new ClipboardRetryPolicy(Attempts: 8, InitialDelay: TimeSpan.FromMilliseconds(10), MaxDelay: TimeSpan.FromMilliseconds(30)));

        use.Opened.ShouldBeTrue();
        _time.RequestedDelays.ShouldBe(
            [TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(30), TimeSpan.FromMilliseconds(30), TimeSpan.FromMilliseconds(30)]);
    }

    [Fact]
    public async Task A_clipboard_that_stays_held_gives_up_after_the_last_attempt_without_a_final_wait_and_never_runs_the_work()
    {
        _api.BusyAttempts = 100;
        var ran = false;

        var use = await Use(
            _ =>
            {
                ran = true;
                return 1;
            },
            new ClipboardRetryPolicy(Attempts: 4, InitialDelay: TimeSpan.FromMilliseconds(10), MaxDelay: TimeSpan.FromSeconds(1)));

        use.Opened.ShouldBeFalse();
        ran.ShouldBeFalse();
        _api.Log.Count(entry => entry == "busy").ShouldBe(4);
        _time.RequestedDelays.Count.ShouldBe(3, "three waits between four attempts");
    }

    [Fact]
    public async Task A_single_attempt_policy_never_waits()
    {
        _api.BusyAttempts = 1;

        var use = await Use(policy: new ClipboardRetryPolicy(Attempts: 1, InitialDelay: TimeSpan.FromMilliseconds(10), MaxDelay: TimeSpan.FromSeconds(1)));

        use.Opened.ShouldBeFalse();
        _time.RequestedDelays.ShouldBeEmpty();
    }

    [Fact]
    public async Task Cancelling_during_a_wait_ends_it_at_once()
    {
        _api.BusyAttempts = 100;
        using var cancellation = new CancellationTokenSource();
        var waiting = ClipboardOpener.UseAsync(_api, ClipboardRetryPolicy.Default, _time, _ => 0, cancellation.Token);

        await cancellation.CancelAsync();

        await Should.ThrowAsync<TaskCanceledException>(waiting);
    }

    [Fact]
    public async Task A_token_that_is_already_cancelled_does_not_touch_the_clipboard()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(ClipboardOpener.UseAsync(_api, ClipboardRetryPolicy.Default, _time, _ => 0, cancellation.Token));

        _api.Log.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(0, 10, 10)]
    [InlineData(-1, 10, 10)]
    [InlineData(3, -1, 10)]
    [InlineData(3, 10, 5)]
    public void A_policy_that_makes_no_sense_is_a_bug(int attempts, int initialMilliseconds, int maxMilliseconds)
    {
        Should.Throw<ArgumentOutOfRangeException>(
            () => new ClipboardRetryPolicy(attempts, TimeSpan.FromMilliseconds(initialMilliseconds), TimeSpan.FromMilliseconds(maxMilliseconds)));
    }

    [Fact]
    public void The_default_policy_waits_under_a_second_and_a_half_in_total()
    {
        var policy = ClipboardRetryPolicy.Default;
        var total = TimeSpan.Zero;
        var delay = policy.InitialDelay;
        for (var attempt = 1; attempt < policy.Attempts; attempt++)
        {
            total += delay;
            delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, policy.MaxDelay.Ticks));
        }

        total.ShouldBeLessThan(TimeSpan.FromMilliseconds(1500));
        total.ShouldBeGreaterThan(TimeSpan.FromMilliseconds(500));
    }
}
