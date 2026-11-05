using ClipBench.Core.Results;
using ClipBench.Core.Screen;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Security.Cryptography;

namespace ClipBench.Platform.Windows.Screen;

/// <summary>
/// Reads text with the text recognition built into Windows (<c>Windows.Media.Ocr</c>), in the languages the user has set up
/// in Windows. It runs on the device: the picture is handed to the operating system's component and nothing else, with
/// no image parser of ClipBench's own and no network. The component keeps a copy of the pixels in memory until it
/// is done; ClipBench zeroes its own.
/// </summary>
public sealed class WindowsOcrEngine : ITextRecognizer
{
    private const string LanguageMessage =
        "Windows has no text-recognition language for your languages. Open Settings → Time & language → Language & region, "
        + "choose a language, then Language options, and add its Text recognition feature. Then try again.";

    private readonly Func<OcrEngine?> _createEngine;

    /// <summary>Creates the recognizer for the languages in the user's Windows profile.</summary>
    public WindowsOcrEngine()
        : this(OcrEngine.TryCreateFromUserProfileLanguages)
    {
    }

    internal WindowsOcrEngine(Func<OcrEngine?> createEngine)
    {
        ArgumentNullException.ThrowIfNull(createEngine);
        _createEngine = createEngine;
    }

    /// <inheritdoc/>
    public async Task<Result<string>> RecognizeAsync(ScreenImage image, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(image.IsDisposed, image);

        if (image.Width > OcrEngine.MaxImageDimension || image.Height > OcrEngine.MaxImageDimension)
        {
            return Result.Failure<string>(new ResultError(ErrorCode.OcrFailed, "That area is larger than Windows can read. Choose a smaller area."));
        }

        var engine = _createEngine();
        if (engine is null)
        {
            return Result.Failure<string>(new ResultError(ErrorCode.OcrLanguageMissing, LanguageMessage));
        }

        try
        {
            using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(
                CryptographicBuffer.CreateFromByteArray(image.Pixels),
                BitmapPixelFormat.Bgra8,
                image.Width,
                image.Height,
                BitmapAlphaMode.Ignore);
            var result = await engine.RecognizeAsync(bitmap).AsTask(cancellationToken).ConfigureAwait(false);
            return Result.Success(string.Join("\r\n", result.Lines.Select(line => line.Text)));
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or OutOfMemoryException))
        {
            // The message names nothing from the picture; the type is not shown either.
            return Result.Failure<string>(new ResultError(ErrorCode.OcrFailed, "Windows could not read the text in that area."));
        }
    }
}
