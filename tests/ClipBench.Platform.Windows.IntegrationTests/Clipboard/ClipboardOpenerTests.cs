using ClipBench.Platform.Windows.Clipboard;
using ClipBench.Platform.Windows.IntegrationTests.Support;

namespace ClipBench.Platform.Windows.IntegrationTests.Clipboard;

public class ClipboardOpenerTests
{
    private readonly FakeClipboardApi _api = new();
    private readonly ManualTimeProvider _time = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Task<IClipboardSession?> Open(ClipboardRetryPolicy? policy = null) =>
        _time.RunAsync(ClipboardOpener.OpenAsync(_api, policy ?? ClipboardRetryPolicy.Default, _time, Ct));

    [Fact]
    public async Task A_clipboard_that_is_free_opens_at_once_without_waiting()
    {
        using var session = await Open();

        session.ShouldNotBeNull();
        _time.RequestedDelays.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_clipboard_held_by_another_program_is_tried_again_with_a_doubling_wait()
    {
        _api.BusyAttempts = 3;

        using var session = await Open(new ClipboardRetryPolicy(Attempts: 8, InitialDelay: TimeSpan.FromMilliseconds(10), MaxDelay: TimeSpan.FromSeconds(1)));

        session.ShouldNotBeNull();
        _time.RequestedDelays.ShouldBe([TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(40)]);
    }

    [Fact]
    public async Task The_wait_stops_doubling_at_the_maximum()
    {
        _api.BusyAttempts = 5;

        using var session = await Open(new ClipboardRetryPolicy(Attempts: 8, InitialDelay: TimeSpan.FromMilliseconds(10), MaxDelay: TimeSpan.FromMilliseconds(30)));

        session.ShouldNotBeNull();
        _time.RequestedDelays.ShouldBe(
            [TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(30), TimeSpan.FromMilliseconds(30), TimeSpan.FromMilliseconds(30)]);
    }

    [Fact]
    public async Task A_clipboard_that_stays_held_gives_up_after_the_last_attempt_without_a_final_wait()
    {
        _api.BusyAttempts = 100;

        var session = await Open(new ClipboardRetryPolicy(Attempts: 4, InitialDelay: TimeSpan.FromMilliseconds(10), MaxDelay: TimeSpan.FromSeconds(1)));

        session.ShouldBeNull();
        _api.Log.Count(entry => entry == "busy").ShouldBe(4);
        _time.RequestedDelays.Count.ShouldBe(3, "three waits between four attempts");
    }

    [Fact]
    public async Task A_single_attempt_policy_never_waits()
    {
        _api.BusyAttempts = 1;

        var session = await Open(new ClipboardRetryPolicy(Attempts: 1, InitialDelay: TimeSpan.FromMilliseconds(10), MaxDelay: TimeSpan.FromSeconds(1)));

        session.ShouldBeNull();
        _time.RequestedDelays.ShouldBeEmpty();
    }

    [Fact]
    public async Task Cancelling_during_a_wait_ends_it_at_once()
    {
        _api.BusyAttempts = 100;
        using var cancellation = new CancellationTokenSource();
        var opening = ClipboardOpener.OpenAsync(_api, ClipboardRetryPolicy.Default, _time, cancellation.Token);

        await cancellation.CancelAsync();

        await Should.ThrowAsync<TaskCanceledException>(opening);
    }

    [Fact]
    public async Task A_token_that_is_already_cancelled_does_not_touch_the_clipboard()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(ClipboardOpener.OpenAsync(_api, ClipboardRetryPolicy.Default, _time, cancellation.Token));

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
