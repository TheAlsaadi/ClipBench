using ClipBench.Core.Results;

namespace ClipBench.Core.Capture;

/// <summary>Tells which clipboard change was seen.</summary>
/// <param name="sequenceNumber">The clipboard sequence number after the change.</param>
public sealed class ClipboardChangedEventArgs(long sequenceNumber) : EventArgs
{
    /// <summary>The clipboard sequence number after the change. It grows with every change.</summary>
    public long SequenceNumber { get; } = sequenceNumber;
}

/// <summary>Tells when the clipboard changes. It carries no content: read it with <see cref="IClipboardReader"/>.</summary>
public interface IClipboardListener : IDisposable
{
    /// <summary>
    /// Raised once for each change made by another program, on a thread-pool thread. Changes ClipBench
    /// made itself are not reported, and a change that Windows announces twice is reported once.
    /// </summary>
    event EventHandler<ClipboardChangedEventArgs>? Changed;

    /// <summary>Starts listening. Does nothing when already started.</summary>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>Success, or <see cref="ErrorCode.PlatformFailure"/> when Windows would not set up the listener.</returns>
    Task<Result> StartAsync(CancellationToken cancellationToken);
}
