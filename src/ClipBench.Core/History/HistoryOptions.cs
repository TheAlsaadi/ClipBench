namespace ClipBench.Core.History;

/// <summary>The limits that keep the history from growing without bound.</summary>
public sealed record HistoryOptions
{
    /// <summary>The most unpinned clips to keep. Pinned clips do not count. At least 1.</summary>
    public int MaxCount
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            field = value;
        }
    } = 1000;

    /// <summary>How long an unpinned clip is kept after it was last copied. Greater than zero.</summary>
    public TimeSpan MaxAge
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero);
            field = value;
        }
    } = TimeSpan.FromDays(30);
}
