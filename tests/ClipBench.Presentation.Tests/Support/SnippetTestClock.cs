namespace ClipBench.Presentation.Tests.Support;

/// <summary>
/// A clock for snippet tests with a fixed local time zone: 21:30:45 UTC on 23 October 2026 is 00:30:45 on the 24th at
/// UTC+3, whatever the zone of the machine that runs the test.
/// </summary>
internal sealed class SnippetTestClock : TimeProvider
{
    private readonly TimeZoneInfo _zone = TimeZoneInfo.CreateCustomTimeZone("test", TimeSpan.FromHours(3), "test", "test");

    public override TimeZoneInfo LocalTimeZone => _zone;

    public override DateTimeOffset GetUtcNow() => new(2026, 10, 23, 21, 30, 45, TimeSpan.Zero);
}
