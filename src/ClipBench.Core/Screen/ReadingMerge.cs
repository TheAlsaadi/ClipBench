using ClipBench.Core.Results;

namespace ClipBench.Core.Screen;

/// <summary>A word the recognizer found and where, in pixels of the picture it was given.</summary>
/// <param name="Text">The word's letters in reading order.</param>
/// <param name="X">The left edge.</param>
/// <param name="Y">The top edge.</param>
/// <param name="Width">The width.</param>
/// <param name="Height">The height.</param>
public readonly record struct RecognizedWord(string Text, double X, double Y, double Width, double Height);

/// <summary>A line of words as the recognizer returned it.</summary>
/// <param name="Text">The line's text as the recognizer gave it.</param>
/// <param name="Words">The words with their places.</param>
public sealed record RecognizedLine(string Text, IReadOnlyList<RecognizedWord> Words);

/// <summary>A recognizer that says where the words are, so readings in different languages can be combined.</summary>
public interface IWordRecognizer
{
    /// <summary>Reads the lines and words in a picture. The image is not kept.</summary>
    /// <param name="image">The picture.</param>
    /// <param name="cancellationToken">Cancels the reading.</param>
    /// <returns>The lines (none when there is no text), or <see cref="ErrorCode.OcrFailed"/>.</returns>
    Task<Result<IReadOnlyList<RecognizedLine>>> ReadAsync(ScreenImage image, CancellationToken cancellationToken);
}

/// <summary>
/// Combines readings of one picture made with the engines of different languages. An engine reads the script of its
/// language and guesses at the rest: the Arabic engine reads Arabic and English but turns a chevron into "(", the English
/// engine reads the chevron and English but turns Arabic into noise. So each word is taken from the engine that is
/// good at it: the Arabic words from the reading that has them, everything else from a reading without right-to-left letters.
/// </summary>
public static class ReadingMerge
{
    // A reading with fewer right-to-left letters than this is taken to be noise from an engine of another script.
    private const int MinRightToLeftLetters = 2;

    // Two words that share at least this much of the smaller one's area are the same place read twice.
    private const double OverlapShare = 0.5;

    /// <summary>The text of a reading: lines in order, right-to-left lines in reading order, joined with a line break.</summary>
    /// <param name="lines">The reading.</param>
    /// <returns>The text.</returns>
    public static string ToText(IReadOnlyList<RecognizedLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        return string.Join("\r\n", lines.Select(LineText));
    }

    /// <summary>Chooses the reading to use, combining a right-to-left reading with a reading in another script when there are both.</summary>
    /// <param name="readings">The readings, one per language, the user's first language first.</param>
    /// <returns>
    /// One of the readings as it is (the one with the most letters, the earlier winning a tie), or when one has right-to-left
    /// letters and another has none, the two combined.
    /// </returns>
    public static IReadOnlyList<RecognizedLine> Choose(IReadOnlyList<IReadOnlyList<RecognizedLine>> readings)
    {
        ArgumentNullException.ThrowIfNull(readings);
        if (readings.Count == 0)
        {
            return [];
        }

        var texts = readings.Select(Joined).ToList();
        var rightToLeft = texts.Select(text => text.Count(LineOrder.IsRightToLeftLetter)).ToList();
        var richest = rightToLeft.IndexOf(rightToLeft.Max());
        var others = Enumerable.Range(0, readings.Count).Where(i => rightToLeft[i] == 0).ToList();

        if (rightToLeft[richest] < MinRightToLeftLetters || others.Count == 0)
        {
            return readings[RecognitionChoice.PickBest(texts)];
        }

        var best = others[RecognitionChoice.PickBest([.. others.Select(i => texts[i])])];
        return Merge(readings[richest], readings[best]);
    }

    private static List<RecognizedLine> Merge(IReadOnlyList<RecognizedLine> rightToLeft, IReadOnlyList<RecognizedLine> other)
    {
        var arabic = rightToLeft.SelectMany(line => line.Words).ToList();
        var chosen = arabic.Where(word => LineOrder.ContainsRightToLeft(word.Text)).ToList();

        foreach (var word in other.SelectMany(line => line.Words))
        {
            if (!chosen.Any(kept => Overlap(kept, word)))
            {
                chosen.Add(word);
            }
        }

        // What only the right-to-left engine read, kept when it is a word and not a symbol it mirrored.
        foreach (var word in arabic.Where(word => !LineOrder.ContainsRightToLeft(word.Text) && word.Text.Any(char.IsLetterOrDigit)))
        {
            if (!chosen.Any(kept => Overlap(kept, word)))
            {
                chosen.Add(word);
            }
        }

        return Group(chosen);
    }

    // Words that lie on the same line: a word joins a line when its middle is between the line's top and bottom.
    private static List<RecognizedLine> Group(List<RecognizedWord> words)
    {
        var lines = new List<(double Top, double Bottom, List<RecognizedWord> Words)>();
        foreach (var word in words.OrderBy(w => w.Y + (w.Height / 2)).ThenBy(w => w.X))
        {
            var middle = word.Y + (word.Height / 2);
            var index = lines.FindIndex(line => middle >= line.Top && middle <= line.Bottom);
            if (index < 0)
            {
                lines.Add((word.Y, word.Y + word.Height, [word]));
            }
            else
            {
                var line = lines[index];
                line.Words.Add(word);
                lines[index] = (Math.Min(line.Top, word.Y), Math.Max(line.Bottom, word.Y + word.Height), line.Words);
            }
        }

        return
        [
            .. lines.OrderBy(line => line.Top).Select(line =>
            {
                var ordered = line.Words.OrderBy(w => w.X).ToList();
                return new RecognizedLine(string.Join(' ', ordered.Select(w => w.Text)), ordered);
            }),
        ];
    }

    private static bool Overlap(RecognizedWord a, RecognizedWord b)
    {
        var width = Math.Min(a.X + a.Width, b.X + b.Width) - Math.Max(a.X, b.X);
        var height = Math.Min(a.Y + a.Height, b.Y + b.Height) - Math.Max(a.Y, b.Y);
        if (width <= 0 || height <= 0)
        {
            return false;
        }

        var smaller = Math.Min(a.Width * a.Height, b.Width * b.Height);
        return smaller > 0 && (width * height) / smaller >= OverlapShare;
    }

    private static string LineText(RecognizedLine line) =>
        LineOrder.ContainsRightToLeft(line.Text)
            ? LineOrder.ToLogical([.. line.Words.OrderBy(word => word.X).Select(word => word.Text)])
            : line.Text;

    private static string Joined(IReadOnlyList<RecognizedLine> lines) => string.Join("\n", lines.Select(line => line.Text));
}
