using System.Collections.Immutable;
using System.Text;
using ClipBench.Core.Capture;
using ClipBench.Core.Clips;
using ClipBench.Core.Results;
using ClipBench.Platform.Windows.Formats;

namespace ClipBench.Platform.Windows.Clipboard;

/// <summary>
/// Reads the clipboard into one clip. It decides what to look at from the format names alone, asks the
/// capture filter whether the change may be recorded before it reads any content, and holds the clipboard
/// open only long enough to copy the bytes: converting a picture to PNG happens after it is closed again.
/// </summary>
internal sealed class ClipboardReader : IClipboardReader
{
    // UTF-16 can be twice the size of UTF-8, so a byte limit on text is applied to four times as many raw
    // bytes. Anything bigger than that is turned away without being copied.
    private const long RawBytesPerLimitByte = 4;

    // The privacy flag is a 4-byte number; this leaves room for the padding Windows may add.
    private const int MaxHistoryFlagBytes = 64;

    private readonly IClipboardApi _api;
    private readonly TimeProvider _time;
    private readonly ClipboardRetryPolicy _retry;

    /// <summary>Creates a reader.</summary>
    /// <param name="api">The Windows clipboard.</param>
    /// <param name="time">The clock used for waiting when another program holds the clipboard.</param>
    /// <param name="retry">How long to keep trying, or null for the default.</param>
    public ClipboardReader(IClipboardApi api, TimeProvider time, ClipboardRetryPolicy? retry = null)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(time);
        _api = api;
        _time = time;
        _retry = retry ?? ClipboardRetryPolicy.Default;
    }

    /// <inheritdoc/>
    public async Task<Result<ClipboardCapture>> ReadAsync(CaptureFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        // The copying runs inside the opener, in the same stretch as the open and the close; converting a picture waits until the clipboard is closed.
        var use = await ClipboardOpener.UseAsync(
            _api,
            _retry,
            _time,
            session =>
            {
                var sequence = _api.SequenceNumber;
                return (Sequence: sequence, Collected: Collect(session, filter, sequence));
            },
            cancellationToken).ConfigureAwait(false);

        return use.Opened
            ? Finish(use.Value.Collected!, filter, use.Value.Sequence)
            : Failure(ErrorCode.ClipboardBusy, "Another program is using the clipboard. Try again in a moment.");
    }

    private enum Candidate
    {
        Files,
        Html,
        Rtf,
        Text,
        Image,
    }

    private abstract record Collected;

    // The read is over: a rejection or a failure.
    private sealed record Ended(Result<ClipboardCapture> Result) : Collected;

    // The content is decoded.
    private sealed record Decoded(ClipContent Content, string? Source) : Collected;

    // A bitmap was copied and still has to be converted; Fallback is what to keep if it cannot be.
    private sealed record PendingBitmap(byte[] Bitmap, ClipContent? Fallback, string? Source) : Collected;

    private abstract record Outcome;

    private sealed record Found(ClipContent Content) : Outcome;

    private sealed record FoundBitmap(byte[] Bitmap) : Outcome;

    private sealed record Nothing : Outcome;

    private sealed record Damaged : Outcome;

    private sealed record TooLarge : Outcome;

    private sealed record FormatIds(uint? Html, uint? Rtf, uint? CanInclude, bool HasText, bool HasFiles, uint? Bitmap);

    private Collected Collect(IClipboardSession session, CaptureFilter filter, long sequence)
    {
        var formats = session.Formats();
        var ids = Identify(formats);
        var candidates = Candidates(ids);
        if (candidates.Count == 0)
        {
            return new Ended(Failure(ErrorCode.ClipboardEmpty, "The clipboard holds nothing ClipBench keeps."));
        }

        // The filter sees the names of the formats, who owns them and the privacy flag, and nothing else.
        var source = _api.OwnerProcessName();
        var request = new CaptureRequest
        {
            Format = ToClipFormat(candidates[0]),
            SizeInBytes = 0,
            FormatNames = [.. formats.Where(format => format.Name is not null).Select(format => format.Name!)],
            SourceProcess = source,
            CanIncludeInClipboardHistory = ReadHistoryFlag(session, ids.CanInclude),
        };

        var decision = filter.Evaluate(request);
        if (!decision.IsAccepted)
        {
            return new Ended(Result.Success(ClipboardCapture.Rejected(sequence, decision.Rejection)));
        }

        return Decode(session, filter, candidates, ids, source, sequence);
    }

    private static Collected Decode(IClipboardSession session, CaptureFilter filter, List<Candidate> candidates, FormatIds ids, string? source, long sequence)
    {
        byte[]? bitmap = null;
        var sawDamage = false;
        foreach (var candidate in candidates)
        {
            switch (Try(session, filter, candidate, ids))
            {
                case Found found:
                    return bitmap is null ? new Decoded(found.Content, source) : new PendingBitmap(bitmap, found.Content, source);
                case FoundBitmap found:
                    bitmap = found.Bitmap;
                    break;
                case TooLarge:
                    return new Ended(Result.Success(ClipboardCapture.Rejected(sequence, CaptureRejection.TooLarge)));
                case Damaged:
                    sawDamage = true;
                    break;
            }
        }

        if (bitmap is not null)
        {
            return new PendingBitmap(bitmap, null, source);
        }

        return sawDamage
            ? new Ended(Failure(ErrorCode.ClipboardUnreadable, "The content on the clipboard is damaged."))
            : new Ended(Failure(ErrorCode.ClipboardEmpty, "The clipboard holds nothing ClipBench keeps."));
    }

    // Runs after the clipboard is closed.
    private static Result<ClipboardCapture> Finish(Collected collected, CaptureFilter filter, long sequence)
    {
        switch (collected)
        {
            case Ended ended:
                return ended.Result;
            case Decoded decoded:
                return Judge(decoded.Content, decoded.Source, filter, sequence);
            case PendingBitmap pending:
                var png = Dib.ToPng(pending.Bitmap);
                if (png.IsSuccess)
                {
                    return Judge(new ImageContent(png.Value), pending.Source, filter, sequence);
                }

                return pending.Fallback is { } fallback
                    ? Judge(fallback, pending.Source, filter, sequence)
                    : Result.Failure<ClipboardCapture>(png.Error);
            default:
                throw new InvalidOperationException("Unknown read state.");
        }
    }

    // Now the real size is known, so the filter is asked again. Only the size can have changed its mind.
    private static Result<ClipboardCapture> Judge(ClipContent content, string? source, CaptureFilter filter, long sequence)
    {
        var decision = filter.Evaluate(new CaptureRequest { Format = content.Format, SizeInBytes = content.SizeInBytes, SourceProcess = source });
        return Result.Success(decision.IsAccepted
            ? ClipboardCapture.Accepted(sequence, content, source)
            : ClipboardCapture.Rejected(sequence, decision.Rejection));
    }

    private static Outcome Try(IClipboardSession session, CaptureFilter filter, Candidate candidate, FormatIds ids)
    {
        var textCap = TextCap(filter);
        switch (candidate)
        {
            case Candidate.Files:
                {
                    var bytes = Copy(session, ClipboardFormats.Hdrop, textCap, out var outcome);
                    if (bytes is null)
                    {
                        return outcome!;
                    }

                    var paths = DropFiles.Parse(bytes, textCap);
                    return paths.IsSuccess ? new Found(new FilesContent(paths.Value)) : new Damaged();
                }

            case Candidate.Text:
                {
                    var text = ReadText(session, textCap, out var outcome);
                    if (text is null)
                    {
                        return outcome!;
                    }

                    return string.IsNullOrWhiteSpace(text) ? new Nothing() : new Found(new TextContent(text));
                }

            case Candidate.Html:
                {
                    var bytes = Copy(session, ids.Html!.Value, textCap, out var outcome);
                    if (bytes is null)
                    {
                        return outcome!;
                    }

                    var fragment = HtmlFormat.ExtractFragment(bytes, textCap);
                    if (fragment.IsFailure)
                    {
                        return new Damaged();
                    }

                    if (string.IsNullOrWhiteSpace(fragment.Value))
                    {
                        return new Nothing();
                    }

                    // The text alternative is what search and previews use. Most programs supply one; when none
                    // is there (or it cannot be read) it is made by removing the tags.
                    var text = ids.HasText ? ReadText(session, textCap, out outcome) : null;
                    if (outcome is TooLarge)
                    {
                        return outcome;
                    }

                    return new Found(new HtmlContent(fragment.Value, string.IsNullOrWhiteSpace(text) ? HtmlText.Strip(fragment.Value) : text));
                }

            case Candidate.Rtf:
                {
                    var bytes = Copy(session, ids.Rtf!.Value, textCap, out var outcome);
                    if (bytes is null)
                    {
                        return outcome!;
                    }

                    var rtf = CutAtNul(Encoding.Latin1.GetString(bytes));
                    if (string.IsNullOrWhiteSpace(rtf))
                    {
                        return new Nothing();
                    }

                    var text = ReadText(session, textCap, out outcome);
                    return outcome is TooLarge ? outcome : new Found(new RtfContent(rtf, text ?? string.Empty));
                }

            default:
                {
                    var bytes = Copy(session, ids.Bitmap!.Value, Dib.MaxBytes, out var outcome);
                    return bytes is null ? outcome! : new FoundBitmap(bytes);
                }
        }
    }

    // Copies a format's bytes, unless they are larger than the cap (then nothing is copied).
    private static byte[]? Copy(IClipboardSession session, uint format, int cap, out Outcome? failure)
    {
        failure = null;
        var size = session.SizeOf(format);
        if (size is null)
        {
            failure = new Damaged();
            return null;
        }

        if (size > cap)
        {
            failure = new TooLarge();
            return null;
        }

        var bytes = session.Read(format, cap);
        if (bytes is null)
        {
            failure = new Damaged();
        }

        return bytes;
    }

    private static string? ReadText(IClipboardSession session, int cap, out Outcome? failure)
    {
        var bytes = Copy(session, ClipboardFormats.UnicodeText, cap, out failure);

        // Decoding replaces a lone surrogate, which would be refused later when the clip is stored.
        return bytes is null ? null : CutAtNul(Encoding.Unicode.GetString(bytes));
    }

    private static string CutAtNul(string text)
    {
        var end = text.IndexOf('\0', StringComparison.Ordinal);
        return end < 0 ? text : text[..end];
    }

    private static int TextCap(CaptureFilter filter) =>
        (int)Math.Min(int.MaxValue / 2, filter.Limits.MaxTextBytes * RawBytesPerLimitByte);

    // Reads the privacy flag, a 4-byte number. Windows may report a larger block than was asked for, so the
    // number is the first four bytes. Anything shorter, or a flag that cannot be read, is "present but
    // unreadable", which the filter treats as private.
    private static int? ReadHistoryFlag(IClipboardSession session, uint? format)
    {
        if (format is null)
        {
            return null;
        }

        var bytes = session.Read(format.Value, MaxHistoryFlagBytes);
        return bytes is { Length: >= 4 } ? BitConverter.ToInt32(bytes, 0) : null;
    }

    private static FormatIds Identify(IReadOnlyList<ClipboardFormat> formats)
    {
        uint? Named(string name) => formats.Where(format => string.Equals(format.Name, name, StringComparison.OrdinalIgnoreCase)).Select(format => (uint?)format.Id).FirstOrDefault();
        uint? Standard(uint id) => formats.Any(format => format.Id == id) ? id : null;

        return new FormatIds(
            Named(ClipboardFormats.Html),
            Named(ClipboardFormats.Rtf),
            Named(ClipboardFormatNames.CanIncludeInClipboardHistory),
            Standard(ClipboardFormats.UnicodeText) is not null,
            Standard(ClipboardFormats.Hdrop) is not null,
            Standard(ClipboardFormats.DibV5) ?? Standard(ClipboardFormats.Dib));
    }

    // What to try, best first. A file list wins; with text, the richest text form wins and a picture is the
    // last resort; without text, a picture beats HTML, because a browser's "Copy image" puts an <img> tag
    // beside the picture and the picture is what was meant.
    private static List<Candidate> Candidates(FormatIds ids)
    {
        List<Candidate> order = [];
        if (ids.HasFiles)
        {
            order.Add(Candidate.Files);
        }

        if (ids.HasText)
        {
            if (ids.Html is not null)
            {
                order.Add(Candidate.Html);
            }

            if (ids.Rtf is not null)
            {
                order.Add(Candidate.Rtf);
            }

            order.Add(Candidate.Text);
        }

        if (ids.Bitmap is not null)
        {
            order.Add(Candidate.Image);
        }

        if (!ids.HasText && ids.Html is not null)
        {
            order.Add(Candidate.Html);
        }

        return order;
    }

    private static ClipFormat ToClipFormat(Candidate candidate) => candidate switch
    {
        Candidate.Files => ClipFormat.Files,
        Candidate.Html => ClipFormat.Html,
        Candidate.Rtf => ClipFormat.Rtf,
        Candidate.Text => ClipFormat.Text,
        _ => ClipFormat.Image,
    };

    private static Result<ClipboardCapture> Failure(ErrorCode code, string message) =>
        Result.Failure<ClipboardCapture>(new ResultError(code, message));
}
