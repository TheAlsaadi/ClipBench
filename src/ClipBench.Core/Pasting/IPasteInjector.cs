using ClipBench.Core.Results;

namespace ClipBench.Core.Pasting;

/// <summary>Pastes content into another program by putting it on the clipboard and sending Ctrl+V.</summary>
public interface IPasteInjector
{
    /// <summary>
    /// Brings the target window back to the front, puts the content on the clipboard, checks that nothing
    /// else changed the clipboard, sends Ctrl+V, and after the restore delay puts the previous clipboard
    /// back when asked. No key is sent unless the target has focus.
    /// </summary>
    /// <param name="request">What to paste and where.</param>
    /// <param name="cancellationToken">Cancels the paste before any key is sent.</param>
    /// <returns>
    /// Success, or why nothing was pasted: <see cref="ErrorCode.TargetNotFound"/>,
    /// <see cref="ErrorCode.TargetElevated"/>, <see cref="ErrorCode.FocusNotRestored"/>,
    /// <see cref="ErrorCode.ClipboardChangedBeforePaste"/>, <see cref="ErrorCode.InputBlocked"/>,
    /// <see cref="ErrorCode.ClipboardBusy"/> or <see cref="ErrorCode.ClipboardWriteFailed"/>.
    /// </returns>
    Task<Result> PasteAsync(PasteRequest request, CancellationToken cancellationToken);
}
