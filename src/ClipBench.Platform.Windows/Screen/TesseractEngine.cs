using System.Runtime.InteropServices;
using ClipBench.Core.Results;
using ClipBench.Core.Screen;
using TesseractOCR;
using TesseractOCR.Enums;
using TesseractOCR.Exceptions;
using TessImage = TesseractOCR.Pix.Image;

namespace ClipBench.Platform.Windows.Screen;

/// <summary>
/// Tesseract (ADR 0019): reads Latin letters, digits and symbols, so code, paths and prose come out right where the Windows
/// engines drop symbols. It reads the pixels ClipBench built in memory; no file or image format is ever opened, and the engine is
/// never given a URL. Its own copies of the pixels are freed when the reading is done and cannot be zeroed from here,
/// which is the same as for the Windows engines (threat model section 5).
/// </summary>
public sealed class TesseractEngine : ILatinWordRecognizer, IDisposable
{
    private const string Language = "eng";

    // The same limits as the enlarging step, so a picture that was enlarged is always readable.
    private const int MaxSide = 10_000;
    private const long MaxPixels = 12_000_000;

    // A picture whose top and bottom rows (the margin ClipBench added) average darker than this is light text on a dark ground.
    private const int DarkBelow = 128;

    private readonly Engine _engine;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private TesseractEngine(Engine engine) => _engine = engine;

    /// <summary>
    /// Twofold. Measured on the same pictures it is as good as threefold or better (code 0.960 against 0.958, <c>&gt;</c> kept 95% against 93%), it
    /// reads the thin strokes of underscores in real screenshots that threefold loses, and it has about half the pixels to read.
    /// </summary>
    public int PreferredScale => 2;

    /// <summary>Starts the engine on the model in a folder.</summary>
    /// <param name="dataFolder">The folder that holds <c>eng.traineddata</c>.</param>
    /// <returns>The engine, or null where it cannot start: no model, a model that is not one, no ARM64 build, or no Visual C++ runtime.</returns>
    internal static TesseractEngine? TryCreate(string dataFolder) => TryCreate(dataFolder, folder => new Engine(folder, Language, EngineMode.LstmOnly));

    /// <summary>Starts the engine with the given way of starting it (for tests).</summary>
    /// <param name="dataFolder">The folder that holds <c>eng.traineddata</c>.</param>
    /// <param name="start">Makes the native engine for the folder.</param>
    /// <returns>The engine, or null where it cannot start.</returns>
    internal static TesseractEngine? TryCreate(string dataFolder, Func<string, Engine> start)
    {
        ArgumentNullException.ThrowIfNull(start);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataFolder);
        if (!File.Exists(Path.Combine(dataFolder, Language + ".traineddata")))
        {
            return null;
        }

        try
        {
            return new TesseractEngine(start(dataFolder));
        }
#pragma warning disable CA1031 // Any failure to start means no engine: the wrapper throws plain exceptions for native files that will not load.
        catch (Exception)
#pragma warning restore CA1031
        {
            // The engine is an extra. Windows' own engines still read, so a failure to start is not an error for the user.
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<RecognizedLine>>> ReadAsync(ScreenImage image, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(image.IsDisposed, image);

        if (image.Width > MaxSide || image.Height > MaxSide || (long)image.Width * image.Height > MaxPixels)
        {
            return Result.Failure<IReadOnlyList<RecognizedLine>>(new ResultError(ErrorCode.OcrFailed, "That area is larger than ClipBench can read. Choose a smaller area."));
        }

        // One engine is not safe for two readings at once. Once a reading has started it is finished, even if the caller gives up,
        // because the caller clears the picture as soon as it returns.
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(() => Read(image)).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Frees the engine and its model.</summary>
    public void Dispose()
    {
        _engine.Dispose();
        _gate.Dispose();
    }

    private static bool IsDark(ScreenImage image)
    {
        long sum = 0;
        var count = 0;
        var pixels = image.Pixels;
        foreach (var y in new[] { 0, image.Height - 1 })
        {
            for (var x = 0; x < image.Width; x++)
            {
                var i = ((y * image.Width) + x) * 4;
                sum += (pixels[i] + pixels[i + 1] + pixels[i + 2]) / 3;
                count++;
            }
        }

        return count > 0 && sum / count < DarkBelow;
    }

    // Raw pixels into a Leptonica picture: no file or image-format decoder is involved. Light text on a dark ground is inverted on the way,
    // which reads better, and the original is untouched.
    private static unsafe TessImage ToPix(ScreenImage image, bool invert)
    {
        var pix = TessImage.Create(image.Width, image.Height, 32);
        var data = pix.GetData();
        var words = (uint*)data.PixData;
        var wordsPerLine = data.WordsPerLine;
        var pixels = image.Pixels;
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var i = ((y * image.Width) + x) * 4;
                var blue = pixels[i];
                var green = pixels[i + 1];
                var red = pixels[i + 2];
                words[(y * wordsPerLine) + x] = invert
                    ? TesseractOCR.Pix.Data.EncodeAsRGBA((byte)(255 - red), (byte)(255 - green), (byte)(255 - blue), 255)
                    : TesseractOCR.Pix.Data.EncodeAsRGBA(red, green, blue, 255);
            }
        }

        return pix;
    }

    private static unsafe void Clear(TessImage pix, int height)
    {
        var data = pix.GetData();
        new Span<uint>((uint*)data.PixData, data.WordsPerLine * height).Clear();
    }

    private Result<IReadOnlyList<RecognizedLine>> Read(ScreenImage image)
    {
        try
        {
            using var pix = ToPix(image, IsDark(image));
            try
            {
                // One uniform block of text: what a dragged rectangle over code, a path or a paragraph is.
                using var page = _engine.Process(pix, PageSegMode.SingleBlock);
                return Result.Success<IReadOnlyList<RecognizedLine>>(Lines(page));
            }
            finally
            {
                Clear(pix, image.Height);
            }
        }
        catch (Exception exception) when (exception is TesseractException or InvalidOperationException or ArgumentException or SEHException)
        {
            // The message names nothing from the picture.
            return Result.Failure<IReadOnlyList<RecognizedLine>>(new ResultError(ErrorCode.OcrFailed, "ClipBench could not read the text in that area."));
        }
    }

    // The English model writes typographic quotes where code, JSON and paths have straight ones (a third of the quotes in the measured pictures),
    // so a quote is always put back as a straight one. Text typeset with curly quotes loses them; the words stay right.
    private static string? Straight(string? text) =>
        text?.Replace((char)0x201C, '"').Replace((char)0x201D, '"').Replace((char)0x2018, (char)0x27).Replace((char)0x2019, (char)0x27);

    private static List<RecognizedLine> Lines(Page page)
    {
        var lines = new List<RecognizedLine>();
        foreach (var block in page.Layout)
        {
            foreach (var paragraph in block.Paragraphs)
            {
                foreach (var line in paragraph.TextLines)
                {
                    var words = new List<RecognizedWord>();
                    foreach (var word in line.Words)
                    {
                        var text = Straight(word.Text?.Trim());
                        if (string.IsNullOrEmpty(text) || word.BoundingBox is not { } box)
                        {
                            continue;
                        }

                        words.Add(new RecognizedWord(text, box.X1, box.Y1, box.Width, box.Height, word.Confidence));
                    }

                    if (words.Count > 0)
                    {
                        lines.Add(new RecognizedLine(string.Join(' ', words.Select(word => word.Text)), words));
                    }
                }
            }
        }

        return lines;
    }
}
