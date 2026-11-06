using System.Text;
using ClipBench.Core.Results;

namespace ClipBench.Core.Screen;

/// <summary>A word the recognizer found and where, in pixels of the picture it was given.</summary>
/// <param name="Text">The word's letters in reading order.</param>
/// <param name="X">The left edge.</param>
/// <param name="Y">The top edge.</param>
/// <param name="Width">The width.</param>
/// <param name="Height">The height.</param>
/// <param name="Confidence">How sure the engine is of the word, from 0 to 100, or null when the engine gives no figure.</param>
public readonly record struct RecognizedWord(string Text, double X, double Y, double Width, double Height, double? Confidence = null);

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
/// A recognizer that reads Latin letters, digits and symbols (code, paths, prose) better than the engines of Windows, but only that
/// script. Its reading is preferred for everything that is not Arabic or Hebrew (<see cref="ReadingMerge.Choose(IReadOnlyList{IReadOnlyList{RecognizedLine}}, int)"/>).
/// </summary>
public interface ILatinWordRecognizer : IWordRecognizer
{
    /// <summary>
    /// The enlargement this engine reads best (1 to <see cref="ImageScaler.MaxFactor"/>), or 0 to read the picture the other engines read.
    /// Where it differs, the reading is made on its own enlarged copy and its word places are moved to the shared picture.
    /// </summary>
    int PreferredScale => 0;
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

    // A Windows reading beats the preferred Latin one when it has at least this many more letters that the English model cannot read
    // (another alphabet, or a letter with an accent) and so guessed at.
    private const int UnreadableLead = 2;

    // Letters up to here are Latin, with or without accents; beyond it are the Latin extensions and other alphabets.
    private const int LastLatinLetter = 0x24F;

    // A word that an engine read with at least this confidence (0 to 100) is not replaced by right-to-left letters read in the same place.
    // The Arabic engine makes up Arabic letters for tiny Latin text; the English model gave 0 to what it made of a real Arabic name,
    // and under 30 to 1% of the words it read right.
    private const double TrustedConfidence = 30;

    // A word the Latin engine read with less confidence than this can be replaced by what another engine read in the same place (see Replaces).
    // The English model reads a chevron icon as ">>" with about 42 and a blue "[80," as "(86," with 59; clean symbols in code are read with 70 and more.
    private const double SureOfSymbols = 60;

    // Two words that share at least this much of the smaller one's area are the same place read twice.
    private const double OverlapShare = 0.5;

    /// <summary>
    /// The text of a reading: lines in order and indented as far as their places show, right-to-left lines in reading order, joined with a line
    /// break. A list bullet read as a letter and the zero of a code font read as "@" are put right.
    /// </summary>
    /// <param name="lines">The reading.</param>
    /// <returns>The text.</returns>
    public static string ToText(IReadOnlyList<RecognizedLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        return ReadingText.Write(lines);
    }

    /// <summary>Chooses the reading to use, combining a right-to-left reading with a reading in another script when there are both.</summary>
    /// <param name="readings">The readings, one per language, the user's first language first.</param>
    /// <returns>
    /// One of the readings as it is (the one with the most letters, the earlier winning a tie), or when one has right-to-left
    /// letters and another has none, the two combined.
    /// </returns>
    public static IReadOnlyList<RecognizedLine> Choose(IReadOnlyList<IReadOnlyList<RecognizedLine>> readings) => Choose(readings, -1);

    /// <summary>Chooses the reading to use, preferring the reading of a Latin-script engine.</summary>
    /// <param name="readings">The readings, one per language.</param>
    /// <param name="latinIndex">The index of the reading made by an <see cref="ILatinWordRecognizer"/>, or -1 when there is none.</param>
    /// <returns>The reading to use.</returns>
    public static IReadOnlyList<RecognizedLine> Choose(IReadOnlyList<IReadOnlyList<RecognizedLine>> readings, int latinIndex)
    {
        ArgumentNullException.ThrowIfNull(readings);
        if (readings.Count == 0)
        {
            return [];
        }

        var texts = readings.Select(Joined).ToList();
        var rightToLeft = texts.Select(text => text.Count(LineOrder.IsRightToLeftLetter)).ToList();
        var latin = latinIndex >= 0 && latinIndex < readings.Count && rightToLeft[latinIndex] == 0 && texts[latinIndex].Any(c => !char.IsWhiteSpace(c))
            ? latinIndex
            : -1;
        if (latin >= 0)
        {
            var rivals = Enumerable.Range(0, readings.Count).Where(i => i != latin && rightToLeft[i] == 0).Select(i => readings[i]).ToList();
            var refined = Refine(readings[latin], rivals);
            if (!ReferenceEquals(refined, readings[latin]))
            {
                readings = [.. readings.Select((reading, i) => i == latin ? refined : reading)];
                texts[latin] = Joined(refined);
            }
        }

        var richest = rightToLeft.IndexOf(rightToLeft.Max());
        var others = Enumerable.Range(0, readings.Count).Where(i => rightToLeft[i] == 0).ToList();

        if (rightToLeft[richest] < MinRightToLeftLetters || others.Count == 0)
        {
            return readings[Pick(texts, [.. Enumerable.Range(0, readings.Count)], latin)];
        }

        return Merge(readings[richest], readings[Pick(texts, others, latin)]);
    }

    // The Latin reading with each symbol it is unsure of swapped for the symbol another reading has there. The same reading when nothing changes.
    private static IReadOnlyList<RecognizedLine> Refine(IReadOnlyList<RecognizedLine> latin, List<IReadOnlyList<RecognizedLine>> rivals)
    {
        var others = rivals.SelectMany(reading => reading).SelectMany(line => line.Words).ToList();
        if (others.Count == 0)
        {
            return latin;
        }

        var changed = false;
        var lines = new List<RecognizedLine>(latin.Count);
        foreach (var line in latin)
        {
            var words = new List<RecognizedWord>(line.Words.Count);
            var lineChanged = false;
            foreach (var word in line.Words)
            {
                var swap = word.Confidence < SureOfSymbols ? others.FindIndex(known => Overlap(known, word) && Replaces(known, word)) : -1;
                words.Add(swap >= 0 ? others[swap] : word);
                lineChanged |= swap >= 0;
            }

            changed |= lineChanged;
            lines.Add(lineChanged ? new RecognizedLine(string.Join(' ', words.Select(word => word.Text)), words) : line);
        }

        return changed ? lines : latin;
    }

    // Another engine's word replaces one the Latin engine is unsure of when both are only symbols (a chevron icon read as ">>" by the English
    // model and as ">" by Windows), or when they have the same letters in the same places and differ only in digits and symbols: the English
    // model read the blue "[80," of a dark JSON block as "(86," with confidence 59, and Windows read it right. A word with a letter of its own
    // is never swapped, so the Windows engine's "1" for "l" and "O" for "0" do not come in.
    private static bool Replaces(RecognizedWord other, RecognizedWord latin)
    {
        if (IsSymbolsOnly(latin))
        {
            return IsSymbolsOnly(other);
        }

        if (other.Text.Length != latin.Text.Length || other.Text == latin.Text)
        {
            return false;
        }

        for (var i = 0; i < latin.Text.Length; i++)
        {
            if (other.Text[i] != latin.Text[i] && !SameKind(other.Text[i], latin.Text[i]))
            {
                return false;
            }
        }

        return true;
    }

    // Two characters that may stand for each other: two digits, or two symbols that are not an opening and a closing bracket. The Arabic
    // engine mirrors brackets ("[i]" comes back as "]i["), and a digit for a symbol is a different kind of mistake, not a better reading.
    // Quotes are never given up: the English model reads them well, and the Windows engine made a dash of the closing quote of "log".
    private static bool SameKind(char one, char two)
    {
        if (char.IsLetter(one) || char.IsLetter(two) || one is '"' or '\'' || two is '"' or '\'')
        {
            return false;
        }

        if (char.IsDigit(one) || char.IsDigit(two))
        {
            return char.IsDigit(one) && char.IsDigit(two);
        }

        const string Opening = "([{<";
        const string Closing = ")]}>";
        return !(Opening.Contains(one, StringComparison.Ordinal) && Closing.Contains(two, StringComparison.Ordinal))
            && !(Closing.Contains(one, StringComparison.Ordinal) && Opening.Contains(two, StringComparison.Ordinal));
    }

    private static bool IsSymbolsOnly(RecognizedWord word) => word.Text.Any(c => !char.IsWhiteSpace(c)) && !word.Text.Any(char.IsLetterOrDigit);

    // The reading to use among the candidates: the Latin one, unless another reading has clearly more of a script it cannot read.
    private static int Pick(List<string> texts, List<int> candidates, int latin)
    {
        var rivals = candidates.Where(i => i != latin).ToList();
        var best = rivals.Count == 0 ? -1 : rivals[RecognitionChoice.PickBest([.. rivals.Select(i => texts[i])])];
        if (latin < 0)
        {
            return best;
        }

        return best >= 0 && UnreadableLetters(texts[best]) >= UnreadableLetters(texts[latin]) + UnreadableLead ? best : latin;
    }

    // Letters of another alphabet, and Latin letters with an accent. Not the o-slash and its like: they have no accent, and the Arabic
    // engine reads a slashed zero as one, so counting them would hand a terminal back to the engine that misread it.
    private static int UnreadableLetters(string text) =>
        text.Count(c => char.IsLetter(c) && (c > LastLatinLetter || (c > 0x7F && c.ToString().Normalize(NormalizationForm.FormD).Length > 1)));

    private static List<RecognizedLine> Merge(IReadOnlyList<RecognizedLine> rightToLeft, IReadOnlyList<RecognizedLine> other)
    {
        var arabic = rightToLeft.SelectMany(line => line.Words).ToList();
        var trusted = other.SelectMany(line => line.Words).Where(word => word.Confidence >= TrustedConfidence).ToList();
        var chosen = arabic.Where(word => LineOrder.ContainsRightToLeft(word.Text) && !trusted.Any(known => Overlap(known, word))).ToList();

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

    private static string Joined(IReadOnlyList<RecognizedLine> lines) => string.Join("\n", lines.Select(line => line.Text));
}
