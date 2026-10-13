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

    /// <summary>
    /// The most bytes of content the unpinned clips may hold together, 256 MiB by default. Pinned clips
    /// do not count. The newest clip is always kept, so a single large copy is never dropped on arrival.
    /// At least 1.
    /// </summary>
    public long MaxTotalBytes
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1L);
            field = value;
        }
    } = 256L * 1024 * 1024;
}
