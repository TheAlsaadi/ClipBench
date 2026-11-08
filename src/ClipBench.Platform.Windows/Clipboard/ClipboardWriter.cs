using System.Text;
using ClipBench.Core.Capture;
using ClipBench.Core.Clips;
using ClipBench.Core.Results;
using ClipBench.Platform.Windows.Formats;

namespace ClipBench.Platform.Windows.Clipboard;

/// <summary>What came of a write.</summary>
/// <param name="Result">The clipboard's sequence number after a write that worked, or why it did not.</param>
/// <param name="SequenceLeft">
/// The sequence number the write left the clipboard at when it cleared it, whether or not the write then worked; null
/// when it never cleared it, so the user's own content is still there.
/// </param>
internal readonly record struct ClipboardWriteReport(Result<long> Result, long? SequenceLeft);

/// <summary>
/// Puts a clip on the clipboard in every form that suits it, tagged with the private format that tells
/// ClipBench's own reader to ignore it. Everything is converted before the clipboard is touched, so a clip
/// that cannot be written leaves what the user had copied exactly as it was.
/// </summary>
internal sealed class ClipboardWriter : IClipboardWriter
{

    private readonly IClipboardApi _api;
    private readonly TimeProvider _time;
    private readonly ClipboardRetryPolicy _retry;

    /// <summary>Creates a writer.</summary>
    /// <param name="api">The Windows clipboard.</param>
    /// <param name="time">The clock used for waiting when another program holds the clipboard.</param>
    /// <param name="retry">How long to keep trying, or null for the default.</param>
    public ClipboardWriter(IClipboardApi api, TimeProvider time, ClipboardRetryPolicy? retry = null)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(time);
        _api = api;
        _time = time;
        _retry = retry ?? ClipboardRetryPolicy.Default;
    }

    /// <inheritdoc/>
    public async Task<Result<long>> WriteAsync(ClipContent content, CancellationToken cancellationToken) =>
        (await WriteAndReportAsync(content, cancellationToken).ConfigureAwait(false)).Result;

    /// <summary>
    /// Writes like <see cref="WriteAsync"/> and also says whether the user's clipboard was cleared on the way, which a
    /// write that fails part way leaves behind. A write that never opened the clipboard, or that Windows would not let
    /// clear it, changed nothing.
    /// </summary>
    /// <param name="content">What to put on the clipboard.</param>
    /// <param name="cancellationToken">Ends the wait for a busy clipboard.</param>
    /// <returns>The result, and the clipboard's sequence number after the write when it cleared the clipboard.</returns>
    public async Task<ClipboardWriteReport> WriteAndReportAsync(ClipContent content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        var parts = Prepare(content);
        if (parts.IsFailure)
        {
            return new(Result.Failure<long>(parts.Error), null);
        }

        var marker = _api.RegisterFormat(ClipboardFormatNames.OwnWrite);
        if (marker == 0)
        {
            return new(Failure(ErrorCode.ClipboardWriteFailed, "Windows would not prepare the clipboard for ClipBench."), null);
        }

        // The writing runs inside the opener, in the same stretch as the open and the close.
        var cleared = false;
        var use = await ClipboardOpener.UseAsync(
            _api,
            _retry,
            _time,
            session =>
            {
                var put = Put(session, parts.Value, marker, out var emptied);
                cleared = emptied;
                return put;
            },
            cancellationToken).ConfigureAwait(false);
        if (!use.Opened)
        {
            return new(Failure(ErrorCode.ClipboardBusy, "Another program is using the clipboard. Try again in a moment."), null);
        }

        var left = cleared ? _api.SequenceNumber : (long?)null;
        return new(use.Value.IsFailure ? Result.Failure<long>(use.Value.Error) : Result.Success(_api.SequenceNumber), left);
    }

    private static Result Put(IClipboardSession session, List<(uint Format, byte[] Data)> parts, uint marker, out bool emptied)
    {
        emptied = session.Empty();
        if (!emptied)
        {
            return Result.Failure(new ResultError(ErrorCode.ClipboardWriteFailed, "Windows would not clear the clipboard."));
        }

        foreach (var (format, data) in parts)
        {
            if (!session.Set(format, data))
            {
                // Part of a clip is worse than none: some programs would paste the HTML without its text.
                session.Empty();
                return Result.Failure(new ResultError(ErrorCode.ClipboardWriteFailed, "Windows would not take the content."));
            }
        }

        if (!session.Set(marker, ClipboardFormats.OwnWriteMarkerData))
        {
            session.Empty();
            return Result.Failure(new ResultError(ErrorCode.ClipboardWriteFailed, "Windows would not take the content."));
        }

        return Result.Success();
    }

    private Result<List<(uint Format, byte[] Data)>> Prepare(ClipContent content)
    {
        switch (content)
        {
            case TextContent text:
                return Parts((ClipboardFormats.UnicodeText, UnicodeText(text.Text)));
            case HtmlContent html:
                return Named(ClipboardFormats.Html, out var htmlFormat)
                    ? Parts((htmlFormat, HtmlFormat.Build(html.Html)), (ClipboardFormats.UnicodeText, UnicodeText(html.Text)))
                    : Unavailable();
            case RtfContent rtf:
                return Named(ClipboardFormats.Rtf, out var rtfFormat)
                    ? Parts((rtfFormat, Encoding.Latin1.GetBytes(rtf.Rtf + "\0")), (ClipboardFormats.UnicodeText, UnicodeText(rtf.Text)))
                    : Unavailable();
            case FilesContent files:
                return Parts((ClipboardFormats.Hdrop, DropFiles.Build(files.Paths)));
            case ImageContent image:
                var bitmap = Dib.FromPng(image.Png.AsSpan());
                if (bitmap.IsFailure)
                {
                    return Result.Failure<List<(uint, byte[])>>(bitmap.Error);
                }

                return Named(ClipboardFormats.Png, out var pngFormat)
                    ? Parts((pngFormat, image.Png.ToArray()), (ClipboardFormats.DibV5, bitmap.Value))
                    : Unavailable();
            default:
                return Result.Failure<List<(uint, byte[])>>(new ResultError(ErrorCode.ClipboardWriteFailed, "That kind of content cannot be put on the clipboard."));
        }
    }

    private bool Named(string name, out uint format)
    {
        format = _api.RegisterFormat(name);
        return format != 0;
    }

    private static byte[] UnicodeText(string text) => Encoding.Unicode.GetBytes(text + "\0");

    private static Result<List<(uint Format, byte[] Data)>> Parts(params (uint Format, byte[] Data)[] parts) =>
        Result.Success<List<(uint Format, byte[] Data)>>([.. parts]);

    private static Result<List<(uint Format, byte[] Data)>> Unavailable() =>
        Result.Failure<List<(uint Format, byte[] Data)>>(new ResultError(ErrorCode.ClipboardWriteFailed, "Windows would not prepare the clipboard for ClipBench."));

    private static Result<long> Failure(ErrorCode code, string message) =>
        Result.Failure<long>(new ResultError(code, message));
}
