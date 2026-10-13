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

    public void Advance(TimeSpan by) => _now += by;
}
