using System.Text;
using ClipBench.Core.Capture;
using ClipBench.Core.Clips;
using ClipBench.Core.Diagnostics;
using ClipBench.Core.History;
using ClipBench.Core.Notifications;
using ClipBench.Core.Results;

namespace ClipBench.Core.Screen;

/// <summary>What copying text from the screen did.</summary>
/// <param name="Characters">How many characters were copied.</param>
/// <param name="Recorded">True when the text was also added to the history; false while incognito is on.</param>
public readonly record struct ScreenTextOutcome(int Characters, bool Recorded);

/// <summary>
/// Copies the text in a part of the screen: the user chooses a region, its pixels are read into memory, the text is
/// recognized, and the pixels are zeroed. The text goes on the clipboard and, unless incognito is on, into the history,
/// where it meets the same rules as any copy: a size limit, and a secret is hidden and expires. Only one request runs at a
/// time. The user is told what happened in words written here, never in the text that was read.
/// </summary>
public sealed class ScreenTextService
{
    /// <summary>The program name stored with a clip made this way.</summary>
    public const string SourceName = "Screen text";

    private readonly IRegionPicker _picker;
    private readonly IScreenCapturer _capturer;
    private readonly ITextRecognizer _recognizer;
    private readonly IClipboardWriter _clipboard;
    private readonly ClipLibrary _library;
    private readonly ICaptureControl _incognito;
    private readonly IUserNotifier _notifier;
    private readonly IActivityLog _log;
    private readonly TimeProvider _time;
    private int _running;

    /// <summary>Creates the service.</summary>
    /// <param name="picker">Lets the user choose the region.</param>
    /// <param name="capturer">Reads the region's pixels.</param>
    /// <param name="recognizer">Reads the text in them.</param>
    /// <param name="clipboard">Puts the text on the clipboard.</param>
    /// <param name="library">The history the text may be added to.</param>
    /// <param name="incognito">Tells whether nothing may be recorded.</param>
    /// <param name="notifier">Tells the user what happened.</param>
    /// <param name="log">Where the outcome is recorded, by size and error code only.</param>
    /// <param name="time">The clock for the clip's time.</param>
    public ScreenTextService(
        IRegionPicker picker,
        IScreenCapturer capturer,
        ITextRecognizer recognizer,
        IClipboardWriter clipboard,
        ClipLibrary library,
        ICaptureControl incognito,
        IUserNotifier notifier,
        IActivityLog log,
        TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(picker);
        ArgumentNullException.ThrowIfNull(capturer);
        ArgumentNullException.ThrowIfNull(recognizer);
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(incognito);
        ArgumentNullException.ThrowIfNull(notifier);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(time);
        _picker = picker;
        _capturer = capturer;
        _recognizer = recognizer;
        _clipboard = clipboard;
        _library = library;
        _incognito = incognito;
        _notifier = notifier;
        _log = log;
        _time = time;
    }

    /// <summary>Asks the user for a region and copies the text in it.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>
    /// What was copied, or why nothing was. <see cref="ErrorCode.SelectionCancelled"/> and <see cref="ErrorCode.ScreenTextBusy"/>
    /// are silent; every other failure has already been told to the user.
    /// </returns>
    public async Task<Result<ScreenTextOutcome>> CaptureTextAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
        {
            return Result.Failure<ScreenTextOutcome>(new ResultError(ErrorCode.ScreenTextBusy, "Text is already being copied from the screen."));
        }

        try
        {
            var result = await RunAsync(cancellationToken).ConfigureAwait(false);
            if (result.IsFailure)
            {
                Report(result.Error);
            }

            return result;
        }
        finally
        {
            Volatile.Write(ref _running, 0);
        }
    }

    private async Task<Result<ScreenTextOutcome>> RunAsync(CancellationToken cancellationToken)
    {
        var picked = await _picker.PickAsync(cancellationToken).ConfigureAwait(false);
        if (picked.IsFailure)
        {
            return Result.Failure<ScreenTextOutcome>(picked.Error);
        }

        var recognized = await ReadAsync(picked.Value, cancellationToken).ConfigureAwait(false);
        if (recognized.IsFailure)
        {
            return Result.Failure<ScreenTextOutcome>(recognized.Error);
        }

        var text = recognized.Value.Trim();
        if (text.Length == 0)
        {
            return Result.Failure<ScreenTextOutcome>(new ResultError(ErrorCode.NoTextFound, "No text was found in that area."));
        }

        var bytes = Encoding.UTF8.GetByteCount(text);
        if (bytes > CaptureLimits.Default.MaxTextBytes)
        {
            return Result.Failure<ScreenTextOutcome>(new ResultError(ErrorCode.ContentTooLarge, "The text in that area is too large to copy."));
        }

        var content = new TextContent(text);
        var written = await _clipboard.WriteAsync(content, cancellationToken).ConfigureAwait(false);
        if (written.IsFailure)
        {
            return Result.Failure<ScreenTextOutcome>(written.Error);
        }

        var recorded = !_incognito.IsPaused;
        if (recorded)
        {
            // A history that cannot be saved still holds the clip for this run; the text is on the clipboard either way.
            _ = await _library.AddAsync(new Clip(ClipId.New(), content, _time.GetUtcNow(), SourceName), cancellationToken).ConfigureAwait(false);
        }

        _log.Write(ActivityLevel.Information, ActivityEvent.ScreenTextCopied, new ActivityDetails { Count = text.Length, Bytes = bytes });
        _notifier.Show("ClipBench", Message(text.Length, recorded));
        return Result.Success(new ScreenTextOutcome(text.Length, recorded));
    }

    // The pixels exist only inside this method: zeroed whether the reading worked, failed or was cancelled.
    private async Task<Result<string>> ReadAsync(PixelRect region, CancellationToken cancellationToken)
    {
        var captured = await _capturer.CaptureAsync(region, cancellationToken).ConfigureAwait(false);
        if (captured.IsFailure)
        {
            return Result.Failure<string>(captured.Error);
        }

        using var image = captured.Value;
        return await _recognizer.RecognizeAsync(image, cancellationToken).ConfigureAwait(false);
    }

    private void Report(ResultError error)
    {
        if (error.Code is ErrorCode.SelectionCancelled or ErrorCode.ScreenTextBusy)
        {
            return;
        }

        _log.Write(ActivityLevel.Warning, ActivityEvent.ScreenTextFailed, new ActivityDetails { Error = error.Code });
        _notifier.Show("ClipBench", error.Message);
    }

    private static string Message(int characters, bool recorded)
    {
        var copied = characters == 1 ? "Copied 1 character from the screen." : $"Copied {characters} characters from the screen.";
        return recorded ? copied : copied + " Incognito is on, so it was not kept in the history.";
    }
}
