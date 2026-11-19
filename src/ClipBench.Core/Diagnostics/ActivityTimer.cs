namespace ClipBench.Core.Diagnostics;

/// <summary>Measures how long something took and logs it as a number of milliseconds, the only detail it records.</summary>
public readonly struct ActivityTimer
{
    private readonly TimeProvider? _time;
    private readonly long _started;

    private ActivityTimer(TimeProvider time)
    {
        _time = time;
        _started = time.GetTimestamp();
    }

    /// <summary>Gets the whole milliseconds since the timer started.</summary>
    public long ElapsedMilliseconds =>
        _time is { } time
            ? (long)time.GetElapsedTime(_started).TotalMilliseconds
            : throw new InvalidOperationException("The timer was never started.");

    /// <summary>Starts a timer.</summary>
    /// <param name="time">The clock.</param>
    /// <returns>The running timer.</returns>
    public static ActivityTimer Start(TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(time);
        return new ActivityTimer(time);
    }

    /// <summary>Logs the activity with the time since the start.</summary>
    /// <param name="log">The activity log.</param>
    /// <param name="activity">What took that long.</param>
    public void Stop(IActivityLog log, ActivityEvent activity)
    {
        ArgumentNullException.ThrowIfNull(log);
        var milliseconds = ElapsedMilliseconds;
        log.Write(ActivityLevel.Information, activity, new ActivityDetails { Milliseconds = milliseconds });
    }
}
