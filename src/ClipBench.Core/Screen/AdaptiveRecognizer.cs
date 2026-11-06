using ClipBench.Core.Results;

namespace ClipBench.Core.Screen;

/// <summary>
/// Reads a picture of text well on a real desktop: it gives the picture a margin and enlarges it (<see cref="ImageScaler"/>) and reads the picture
/// with the engine of every language the user has set up, keeping the reading with the most letters, except that the words of a
/// right-to-left script are taken from the engine of that script and the rest from an engine of another (<see cref="ReadingMerge"/>), preferring a Latin-script
/// engine (<see cref="ILatinWordRecognizer"/>) for the rest. One engine
/// reads one language only: Arabic text through an English engine comes out as a few digits, and a chevron through an
/// Arabic engine as "(", so the languages are tried and compared rather than guessed. Every enlarged copy is
/// zeroed as soon as the reading is done.
/// </summary>
public sealed class AdaptiveRecognizer : ITextRecognizer
{
    private readonly Func<IReadOnlyList<IWordRecognizer>> _engines;
    private readonly string _noLanguageMessage;

    /// <summary>Creates the recognizer.</summary>
    /// <param name="engines">
    /// The engines to try, one per language, the user's first language first. Asked for every time, because the user can
    /// add a language while ClipBench runs.
    /// </param>
    /// <param name="noLanguageMessage">What to tell the user when there is no engine at all.</param>
    public AdaptiveRecognizer(Func<IReadOnlyList<IWordRecognizer>> engines, string noLanguageMessage)
    {
        ArgumentNullException.ThrowIfNull(engines);
        ArgumentException.ThrowIfNullOrWhiteSpace(noLanguageMessage);
        _engines = engines;
        _noLanguageMessage = noLanguageMessage;
    }

    /// <inheritdoc/>
    public async Task<Result<string>> RecognizeAsync(ScreenImage image, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(image.IsDisposed, image);

        var engines = _engines();
        if (engines.Count == 0)
        {
            return Result.Failure<string>(new ResultError(ErrorCode.OcrLanguageMissing, _noLanguageMessage));
        }

        var prepared = ImageScaler.Prepare(image);
        ScreenImage? latinCopy = null;
        try
        {
            var picture = prepared ?? image;
            var readings = new List<IReadOnlyList<RecognizedLine>>(engines.Count);
            var latinIndex = -1;
            ResultError? firstFailure = null;

            foreach (var engine in engines)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // A Latin engine may read best at its own enlargement. It reads a copy made for it, and its word places are moved to the
                // shared picture so the readings can be compared place by place.
                var seen = picture;
                var toShared = 1.0;
                if (engine is ILatinWordRecognizer { PreferredScale: > 0 and var scale })
                {
                    var own = ImageScaler.Prepare(image, scale);
                    var ownFactor = ImageScaler.FactorOf(image, own);
                    var sharedFactor = ImageScaler.FactorOf(image, prepared);
                    if (own is not null && ownFactor != sharedFactor)
                    {
                        latinCopy?.Dispose();
                        latinCopy = own;
                        seen = own;
                        toShared = (double)sharedFactor / ownFactor;
                    }
                    else
                    {
                        own?.Dispose();
                    }
                }

                var result = await engine.ReadAsync(seen, cancellationToken).ConfigureAwait(false);
                if (result.IsSuccess)
                {
                    if (latinIndex < 0 && engine is ILatinWordRecognizer)
                    {
                        latinIndex = readings.Count;
                    }

                    readings.Add(toShared == 1.0 ? result.Value : Rescale(result.Value, toShared));
                }
                else
                {
                    firstFailure ??= result.Error;
                }
            }

            if (readings.Count == 0)
            {
                // No reading and at least one engine: every engine failed, so a failure was recorded.
                return Result.Failure<string>(firstFailure!.Value);
            }

            return Result.Success(ReadingMerge.ToText(ReadingMerge.Choose(readings, latinIndex)));
        }
        finally
        {
            prepared?.Dispose();
            latinCopy?.Dispose();
        }
    }

    private static List<RecognizedLine> Rescale(IReadOnlyList<RecognizedLine> lines, double by) =>
        [
            .. lines.Select(line => new RecognizedLine(
                line.Text,
                [.. line.Words.Select(word => word with { X = word.X * by, Y = word.Y * by, Width = word.Width * by, Height = word.Height * by })])),
        ];
}

/// <summary>Chooses between readings of the same picture made in different languages.</summary>
public static class RecognitionChoice
{
    /// <summary>
    /// The index of the reading with the most letters. A reading in the wrong language is mostly digits and symbols, so it
    /// loses; on a tie the earlier reading (the user's first language) wins.
    /// </summary>
    /// <param name="readings">The texts.</param>
    /// <returns>The index, or -1 when there are no readings.</returns>
    public static int PickBest(IReadOnlyList<string> readings)
    {
        ArgumentNullException.ThrowIfNull(readings);

        var best = -1;
        var bestLetters = -1;
        for (var i = 0; i < readings.Count; i++)
        {
            var letters = readings[i].Count(char.IsLetter);
            if (letters > bestLetters)
            {
                best = i;
                bestLetters = letters;
            }
        }

        return best;
    }
}
