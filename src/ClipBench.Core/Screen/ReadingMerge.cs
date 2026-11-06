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

    // A word made only of symbols that the Latin engine read with less confidence than this is replaced by the symbols another engine read in the
    // same place. The English model reads a chevron icon as ">>" with about 42; clean symbols in code are read with 70 and more.
    private const double SureOfSymbols = 60;

    // A font is taken to be monospaced when the width of a letter, over the words of three letters or more, varies by less than this share
    // (0.03 in the pictures measured, 0.15 and more for proportional text), judged on at least this many words.
    private const double MonospacedSpread = 0.08;
    private const int MinWordsToJudgeFont = 3;

    // In a monospaced font two words closer than this many letters have no space between them (a real space is a letter wide; the narrow dot
    // that separated "Model." from "from_pretrained(" left 0.74 of a letter, the narrowest real space measured 1.1).
    private const double JoinBelowLetters = 0.9;

    // The most spaces put before a line, so a word placed wrongly cannot make a line of thousands of spaces.
    private const int MaxIndent = 40;

    // Two words that share at least this much of the smaller one's area are the same place read twice.
    private const double OverlapShare = 0.5;

    /// <summary>The text of a reading: lines in order, right-to-left lines in reading order, joined with a line break.</summary>
    /// <param name="lines">The reading.</param>
    /// <returns>The text.</returns>
    public static string ToText(IReadOnlyList<RecognizedLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var letter = LetterWidth(lines);
        var indents = Indents(lines, letter);
        var joinBelow = letter is { Monospaced: true } ? letter.Value.Width * JoinBelowLetters : 0;
        return string.Join("\r\n", lines.Select((line, i) => new string(' ', indents[i]) + LineText(line, joinBelow)));
    }

    // The width of a letter in a reading (the median over its words) and whether the letters all have the same width, as in the fonts code is shown in.
    private static (double Width, bool Monospaced)? LetterWidth(IReadOnlyList<RecognizedLine> lines)
    {
        var widths = lines.SelectMany(line => line.Words).Where(word => word.Text.Length >= 3 && word.Width > 0).Select(word => word.Width / word.Text.Length).Order().ToList();
        if (widths.Count == 0)
        {
            return null;
        }

        var mean = widths.Average();
        var spread = Math.Sqrt(widths.Sum(width => (width - mean) * (width - mean)) / widths.Count) / mean;
        return (widths[widths.Count / 2], widths.Count >= MinWordsToJudgeFont && spread < MonospacedSpread);
    }

    // How many spaces each line starts with, from where its first word is. Indentation is part of code (Python, YAML, JSON) and is lost
    // when every line is flush left. The width of a letter is the typical width of a word's letters, so it is exact for the monospaced
    // fonts code is shown in and close enough for others, where a gap of under half a letter counts as no indent.
    private static int[] Indents(IReadOnlyList<RecognizedLine> lines, (double Width, bool Monospaced)? measured)
    {
        var indents = new int[lines.Count];
        if (measured is not { } size)
        {
            return indents;
        }

        var letter = size.Width;
        var lefts = lines.Select(line => line.Words.Count == 0 || LineOrder.ContainsRightToLeft(line.Text) ? double.NaN : line.Words.Min(word => word.X)).ToList();
        var known = lefts.Where(left => !double.IsNaN(left)).Order().ToList();
        if (known.Count < 2)
        {
            return indents;
        }

        // The left edge of the text is the leftmost place that two lines share. A lone mark further left (a border, a bullet read as a letter)
        // does not move it, and when no two lines share an edge it is simply the leftmost.
        var margin = known.FirstOrDefault(left => known.Count(other => Math.Abs(other - left) < letter / 2) >= 2, known[0]);
        for (var i = 0; i < indents.Length; i++)
        {
            if (!double.IsNaN(lefts[i]))
            {
                indents[i] = (int)Math.Clamp(Math.Round((lefts[i] - margin) / letter), 0, MaxIndent);
            }
        }

        return indents;
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
        var symbols = rivals.SelectMany(reading => reading).SelectMany(line => line.Words).Where(IsSymbolsOnly).ToList();
        if (symbols.Count == 0)
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
                var swap = word.Confidence < SureOfSymbols && IsSymbolsOnly(word) ? symbols.FindIndex(known => Overlap(known, word)) : -1;
                words.Add(swap >= 0 ? symbols[swap] : word);
                lineChanged |= swap >= 0;
            }

            changed |= lineChanged;
            lines.Add(lineChanged ? new RecognizedLine(string.Join(' ', words.Select(word => word.Text)), words) : line);
        }

        return changed ? lines : latin;
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

    private static string LineText(RecognizedLine line, double joinBelow)
    {
        if (LineOrder.ContainsRightToLeft(line.Text))
        {
            return LineOrder.ToLogical([.. line.Words.OrderBy(word => word.X).Select(word => word.Text)]);
        }

        // The engine puts a space between two words it sees apart, and in code it sees "Model." and "from_pretrained(" apart because a dot is
        // narrow. In a monospaced font a real space is a whole letter wide, so less than that between two words is no space.
        if (joinBelow <= 0 || line.Words.Count < 2 || line.Text != string.Join(' ', line.Words.Select(word => word.Text)))
        {
            return line.Text;
        }

        var text = new StringBuilder(line.Words[0].Text);
        for (var i = 1; i < line.Words.Count; i++)
        {
            var gap = line.Words[i].X - (line.Words[i - 1].X + line.Words[i - 1].Width);
            _ = gap < joinBelow ? text.Append(line.Words[i].Text) : text.Append(' ').Append(line.Words[i].Text);
        }

        return text.ToString();
    }

    private static string Joined(IReadOnlyList<RecognizedLine> lines) => string.Join("\n", lines.Select(line => line.Text));
}
