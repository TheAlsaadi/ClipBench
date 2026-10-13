namespace ClipBench.Storage.Tests.Support;

/// <summary>A clock the test controls, so time-based behavior is deterministic.</summary>
internal sealed class FakeTimeProvider(DateTimeOffset start) : TimeProvider
{
    /// <summary>The time the fake clock starts at by default.</summary>
    public static readonly DateTimeOffset Start = new(2026, 10, 13, 9, 0, 0, TimeSpan.Zero);

    private DateTimeOffset _now = start;

    public FakeTimeProvider()
        : this(Start)
    {
    }

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}
