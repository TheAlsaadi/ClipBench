namespace ClipBench.Core.Capture;

/// <summary>The largest content that is captured, so one huge copy cannot fill memory or disk.</summary>
public sealed record CaptureLimits
{
    /// <summary>The default limits: 2 MiB of text and 20 MiB of image.</summary>
    public static CaptureLimits Default { get; } = new();

    /// <summary>The most bytes of text, rich text or file list to capture. Greater than zero.</summary>
    public long MaxTextBytes
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, 0);
            field = value;
        }
    } = 2L * 1024 * 1024;

    /// <summary>The most bytes of image to capture. Greater than zero.</summary>
    public long MaxImageBytes
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, 0);
            field = value;
        }
    } = 20L * 1024 * 1024;
}
