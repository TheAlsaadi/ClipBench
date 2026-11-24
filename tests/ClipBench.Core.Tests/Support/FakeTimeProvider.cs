namespace ClipBench.Core.Tests.Support;

/// <summary>A clock the test controls, so time-based behavior is deterministic.</summary>
internal sealed class FakeTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public FakeTimeProvider()
        : this(TestClips.Start)
    {
    }

    public override DateTimeOffset GetUtcNow() => _now;

    // Timestamps follow the same clock, so an elapsed time is exactly what the test advanced.
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => _now.UtcTicks;

    public void Advance(TimeSpan by) => _now += by;
}
