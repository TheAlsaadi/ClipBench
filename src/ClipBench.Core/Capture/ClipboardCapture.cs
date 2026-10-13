using ClipBench.Core.Clips;

namespace ClipBench.Core.Capture;

/// <summary>
/// One look at the clipboard: whether it is recorded and, if so, what it holds. A rejected capture never
/// holds content, because the filter is applied before the content is read.
/// </summary>
public sealed record ClipboardCapture
{
    private ClipboardCapture()
    {
    }

    /// <summary>The clipboard sequence number the capture was taken at. It grows with every change to the clipboard.</summary>
    public long SequenceNumber { get; private init; }

    /// <summary>Whether the change is recorded and, if not, why.</summary>
    public CaptureDecision Decision { get; private init; }

    /// <summary>What was copied. Null unless the change is accepted.</summary>
    public ClipContent? Content { get; private init; }

    /// <summary>The executable that put the content on the clipboard, when known. Null unless the change is accepted.</summary>
    public string? SourceProcess { get; private init; }

    /// <summary>Creates a capture that is recorded.</summary>
    /// <param name="sequenceNumber">The clipboard sequence number.</param>
    /// <param name="content">What was copied.</param>
    /// <param name="sourceProcess">The executable that owns the content, or null when unknown.</param>
    /// <returns>The capture.</returns>
    public static ClipboardCapture Accepted(long sequenceNumber, ClipContent content, string? sourceProcess)
    {
        ArgumentNullException.ThrowIfNull(content);
        return new ClipboardCapture { SequenceNumber = sequenceNumber, Content = content, SourceProcess = sourceProcess };
    }

    /// <summary>Creates a capture that is not recorded.</summary>
    /// <param name="sequenceNumber">The clipboard sequence number.</param>
    /// <param name="reason">Why it is not recorded. Not <see cref="CaptureRejection.None"/>.</param>
    /// <returns>The capture. It holds no content and no source.</returns>
    public static ClipboardCapture Rejected(long sequenceNumber, CaptureRejection reason)
    {
        if (reason == CaptureRejection.None)
        {
            throw new ArgumentException("A rejection needs a reason.", nameof(reason));
        }

        return new ClipboardCapture { SequenceNumber = sequenceNumber, Decision = new CaptureDecision(reason) };
    }

    /// <summary>Printing never includes the content or the source: captures end up in logs by accident.</summary>
    /// <returns>The sequence number, and the kind and size or the reason.</returns>
    public override string ToString() => Content is { } content
        ? $"Clipboard capture {SequenceNumber} ({content.Format}, {content.SizeInBytes} bytes)"
        : $"Clipboard capture {SequenceNumber} (rejected: {Decision.Rejection})";
}
