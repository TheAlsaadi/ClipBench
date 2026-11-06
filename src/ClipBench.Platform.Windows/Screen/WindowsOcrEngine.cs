using ClipBench.Core.Results;
using ClipBench.Core.Screen;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Security.Cryptography;

namespace ClipBench.Platform.Windows.Screen;

/// <summary>
/// Reads text in one language with the text recognition built into Windows (<c>Windows.Media.Ocr</c>). It runs on
/// the device: the picture is handed to the operating system's component and nothing else, with no image parser of
/// ClipBench's own and no network. The component keeps a copy of the pixels in memory until it is done; ClipBench zeroes
/// its own. Use <see cref="WindowsOcr.CreateRecognizer()"/> for the recognizer the app uses, which enlarges small text
/// and tries every language the user has set up.
/// </summary>
public sealed class WindowsOcrEngine : IWordRecognizer, ITextRecognizer
{
    private readonly OcrEngine _engine;

    /// <summary>Wraps the engine of one language.</summary>
    /// <param name="engine">The engine.</param>
    public WindowsOcrEngine(OcrEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        _engine = engine;
    }

    /// <summary>The tag of the language this engine reads, such as <c>en-US</c>.</summary>
    internal string LanguageTag => _engine.RecognizerLanguage.LanguageTag;

    /// <inheritdoc/>
    public async Task<Result<string>> RecognizeAsync(ScreenImage image, CancellationToken cancellationToken)
    {
        var lines = await ReadAsync(image, cancellationToken).ConfigureAwait(false);
        return lines.IsSuccess ? Result.Success(ReadingMerge.ToText(lines.Value)) : Result.Failure<string>(lines.Error);
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<RecognizedLine>>> ReadAsync(ScreenImage image, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(image.IsDisposed, image);

        if (image.Width > OcrEngine.MaxImageDimension || image.Height > OcrEngine.MaxImageDimension)
        {
            return Result.Failure<IReadOnlyList<RecognizedLine>>(new ResultError(ErrorCode.OcrFailed, "That area is larger than Windows can read. Choose a smaller area."));
        }

        try
        {
            using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(
                CryptographicBuffer.CreateFromByteArray(image.Pixels),
                BitmapPixelFormat.Bgra8,
                image.Width,
                image.Height,
                BitmapAlphaMode.Ignore);
            var result = await _engine.RecognizeAsync(bitmap).AsTask(cancellationToken).ConfigureAwait(false);
            return Result.Success<IReadOnlyList<RecognizedLine>>([.. result.Lines.Select(ToLine)]);
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or OutOfMemoryException))
        {
            // The message names nothing from the picture; the type is not shown either.
            return Result.Failure<IReadOnlyList<RecognizedLine>>(new ResultError(ErrorCode.OcrFailed, "Windows could not read the text in that area."));
        }
    }

    private static RecognizedLine ToLine(OcrLine line) =>
        new(
            line.Text,
            [.. line.Words.Select(word => new RecognizedWord(word.Text, word.BoundingRect.X, word.BoundingRect.Y, word.BoundingRect.Width, word.BoundingRect.Height))]);
}
