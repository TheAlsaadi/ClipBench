using System.Security.Cryptography;
using ClipBench.Core.Results;

namespace ClipBench.Core.Screen;

/// <summary>
/// The pixels of a region of the screen, in memory only. Four bytes a pixel (blue, green, red, alpha), row after row with
/// no padding. Letting go of the image overwrites the pixels with zeros: what is on a screen can be anything, and
/// it must not outlive the moment its text was read.
/// </summary>
public sealed class ScreenImage : IDisposable
{
    private byte[] _pixels;

    /// <summary>Wraps a buffer. The image takes it over: it is zeroed when the image is let go of.</summary>
    /// <param name="width">The width in pixels. Greater than zero.</param>
    /// <param name="height">The height in pixels. Greater than zero.</param>
    /// <param name="pixels">Exactly <c>width * height * 4</c> bytes.</param>
    public ScreenImage(int width, int height, byte[] pixels)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(width, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(height, 0);
        if (pixels.LongLength != (long)width * height * 4)
        {
            throw new ArgumentOutOfRangeException(nameof(pixels), pixels.Length, "The buffer must hold four bytes for every pixel.");
        }

        Width = width;
        Height = height;
        _pixels = pixels;
    }

    /// <summary>The width in pixels.</summary>
    public int Width { get; }

    /// <summary>The height in pixels.</summary>
    public int Height { get; }

    /// <summary>The pixels, or an empty array once the image has been let go of.</summary>
    public byte[] Pixels => _pixels;

    /// <summary>True once the pixels have been zeroed.</summary>
    public bool IsDisposed { get; private set; }

    /// <summary>Overwrites the pixels with zeros. Safe to call twice.</summary>
    public void Dispose()
    {
        if (IsDisposed)
        {
            return;
        }

        IsDisposed = true;
        CryptographicOperations.ZeroMemory(_pixels);
        _pixels = [];
    }
}

/// <summary>Lets the user choose a region of the screen.</summary>
public interface IRegionPicker
{
    /// <summary>Shows the selection overlay and waits for a drag.</summary>
    /// <param name="cancellationToken">Closes the overlay.</param>
    /// <returns>
    /// The region in device pixels, <see cref="ErrorCode.SelectionCancelled"/> when the user pressed Esc or let go of nothing,
    /// <see cref="ErrorCode.RegionTooSmall"/> or <see cref="ErrorCode.RegionTooLarge"/> when the drag cannot be read.
    /// </returns>
    Task<Result<PixelRect>> PickAsync(CancellationToken cancellationToken);
}

/// <summary>Reads the pixels of a region of the screen, and nothing else.</summary>
public interface IScreenCapturer
{
    /// <summary>Copies the pixels of a region into memory.</summary>
    /// <param name="region">The region in device pixels on the virtual screen.</param>
    /// <param name="cancellationToken">Cancels the capture.</param>
    /// <returns>The image, which the caller must dispose, or <see cref="ErrorCode.ScreenCaptureFailed"/>.</returns>
    Task<Result<ScreenImage>> CaptureAsync(PixelRect region, CancellationToken cancellationToken);
}

/// <summary>Reads the text in a picture.</summary>
public interface ITextRecognizer
{
    /// <summary>Recognizes the text. The image is not kept.</summary>
    /// <param name="image">The picture.</param>
    /// <param name="cancellationToken">Cancels the reading.</param>
    /// <returns>
    /// The text (empty when there is none), <see cref="ErrorCode.OcrLanguageMissing"/> when Windows has no recognition
    /// language for the user's languages, or <see cref="ErrorCode.OcrFailed"/>. The messages say what to do.
    /// </returns>
    Task<Result<string>> RecognizeAsync(ScreenImage image, CancellationToken cancellationToken);
}
