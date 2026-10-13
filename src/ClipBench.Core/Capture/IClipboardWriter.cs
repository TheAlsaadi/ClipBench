using ClipBench.Core.Clips;
using ClipBench.Core.Results;

namespace ClipBench.Core.Capture;

/// <summary>Puts content on the clipboard.</summary>
public interface IClipboardWriter
{
    /// <summary>
    /// Replaces the clipboard with the content in every form that suits it (text also as Unicode text,
    /// HTML and rich text with their plain text, an image as a bitmap and PNG, files as a file list).
    /// The write carries the <see cref="ClipboardFormatNames.OwnWrite"/> marker so ClipBench does not
    /// record it again.
    /// </summary>
    /// <param name="content">What to put on the clipboard.</param>
    /// <param name="cancellationToken">Cancels the wait for a busy clipboard.</param>
    /// <returns>
    /// The sequence number right after the write, or <see cref="ErrorCode.ClipboardBusy"/> or
    /// <see cref="ErrorCode.ClipboardWriteFailed"/>.
    /// </returns>
    Task<Result<long>> WriteAsync(ClipContent content, CancellationToken cancellationToken);
}
