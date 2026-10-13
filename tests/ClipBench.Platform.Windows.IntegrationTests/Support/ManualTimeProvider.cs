namespace ClipBench.Platform.Windows.IntegrationTests.Support;

/// <summary>
/// A clock and timers that the test moves by hand, so waits happen instantly and in a known order.
/// <c>Task.Delay(delay, timeProvider)</c> creates a timer here and completes when the test fires it.
/// </summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private readonly List<ManualTimer> _timers = [];
    private DateTimeOffset _now = new(2026, 10, 13, 9, 0, 0, TimeSpan.Zero);

    /// <summary>Every finite wait that was requested, in order.</summary>
    public List<TimeSpan> RequestedDelays { get; } = [];

    public override DateTimeOffset GetUtcNow() => _now;

    /// <summary>Moves the clock forward without firing any timer.</summary>
    public void Advance(TimeSpan by) => _now += by;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        timer.Change(dueTime, period);
        return timer;
    }

    /// <summary>Moves the clock to the earliest pending timer and fires it.</summary>
    /// <returns>False when no timer is pending.</returns>
    public bool FireNext()
    {
        var next = _timers.Where(timer => timer.Due is not null).OrderBy(timer => timer.Due).FirstOrDefault();
        if (next is null)
        {
            return false;
        }

        if (next.Due > _now)
        {
            _now = next.Due!.Value;
        }

        next.Fire();
        return true;
    }

    /// <summary>Fires timers as they appear until the task completes.</summary>
    public async Task<T> RunAsync<T>(Task<T> task)
    {
        while (!task.IsCompleted)
        {
            if (!FireNext())
            {
                await Task.Yield();
            }
        }

        return await task;
    }

    private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
    {
        public DateTimeOffset? Due { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            if (dueTime == Timeout.InfiniteTimeSpan)
            {
                Due = null;
                return true;
            }

            owner.RequestedDelays.Add(dueTime);
            Due = owner._now + dueTime;
            if (!owner._timers.Contains(this))
            {
                owner._timers.Add(this);
            }

            return true;
        }

        public void Fire()
        {
            Due = null;
            callback(state);
        }

        public void Dispose()
        {
            Due = null;
            owner._timers.Remove(this);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
