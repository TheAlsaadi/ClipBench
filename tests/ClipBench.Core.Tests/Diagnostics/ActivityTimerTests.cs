using ClipBench.Core.Diagnostics;
using ClipBench.Core.Tests.Support;

namespace ClipBench.Core.Tests.Diagnostics;

public sealed class ActivityTimerTests
{
    private readonly FakeTimeProvider _clock = new();
    private readonly FakeActivityLog _log = new();

    [Fact]
    public void Stopping_logs_the_event_with_the_time_since_the_start_in_whole_milliseconds()
    {
        var timer = ActivityTimer.Start(_clock);
        _clock.Advance(TimeSpan.FromMilliseconds(87.9));

        timer.Stop(_log, ActivityEvent.PopupShown);

        var entry = _log.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(ActivityLevel.Information);
        entry.Activity.ShouldBe(ActivityEvent.PopupShown);
        entry.Details.Milliseconds.ShouldBe(87);
    }

    [Fact]
    public void The_elapsed_time_can_be_read_without_logging()
    {
        var timer = ActivityTimer.Start(_clock);
        _clock.Advance(TimeSpan.FromSeconds(1.5));

        timer.ElapsedMilliseconds.ShouldBe(1500);
        _log.Entries.ShouldBeEmpty();
    }

    [Fact]
    public void Startup_is_logged_as_ready_with_its_duration()
    {
        var timer = ActivityTimer.Start(_clock);
        _clock.Advance(TimeSpan.FromMilliseconds(640));

        timer.Stop(_log, ActivityEvent.AppReady);

        _log.Entries.ShouldHaveSingleItem().ShouldBe(new FakeActivityLog.Entry(ActivityLevel.Information, ActivityEvent.AppReady, new ActivityDetails { Milliseconds = 640 }));
    }

    [Fact]
    public void A_timer_that_was_never_started_is_a_bug()
    {
        _ = Should.Throw<InvalidOperationException>(() => default(ActivityTimer).Stop(_log, ActivityEvent.PopupShown));
    }

    [Fact]
    public void A_missing_clock_or_log_is_a_bug()
    {
        _ = Should.Throw<ArgumentNullException>(() => ActivityTimer.Start(null!));
        _ = Should.Throw<ArgumentNullException>(() => ActivityTimer.Start(_clock).Stop(null!, ActivityEvent.PopupShown));
    }
}
