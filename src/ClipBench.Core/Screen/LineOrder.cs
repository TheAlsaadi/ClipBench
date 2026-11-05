using System.Text;

namespace ClipBench.Core.Screen;

/// <summary>
/// Puts the words of a recognized line into reading order. Windows returns the letters of each word in their
/// reading order but lists the words left to right as they are drawn, which for Arabic, Hebrew and other
/// right-to-left text is backwards: a name written "first family" is returned "family first". This undoes that with the
/// part of the Unicode bidirectional rules that matters for lines of words: the line is right-to-left when its Arabic or
/// Hebrew letters are at least twice the other letters and left-to-right otherwise, runs of the other direction are kept
/// together (a run of English in an Arabic sentence keeps its own order) and a number or punctuation joins the words around it.
/// </summary>
/// <remarks>
/// A line that mixes both directions is ambiguous: "Name: Mohammed Alsaadi" and "Mohammed Alsaadi :Name" can
/// look the same on screen. Right-to-left needs at least twice the letters of the other direction; anything less is read
/// left to right, which is right for an English window with an Arabic name in it and wrong for a short Arabic sentence
/// with a long English word.
/// </remarks>
public static class LineOrder
{
    // Right-to-left letters must be at least this many times the other letters for the line to read right to left.
    private const int RightToLeftDominance = 2;

    private enum Direction
    {
        Neutral,
        LeftToRight,
        RightToLeft,
    }

    /// <summary>True when the text has a letter of a right-to-left script (Hebrew, Arabic, Syriac, Thaana and their forms).</summary>
    /// <param name="text">The text.</param>
    /// <returns>Whether the words need <see cref="ToLogical"/>.</returns>
    public static bool ContainsRightToLeft(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Any(IsRightToLeftLetter);
    }

    /// <summary>Turns the words of a line, listed left to right as drawn, into text in reading order.</summary>
    /// <param name="wordsLeftToRight">The words, left to right on the screen.</param>
    /// <returns>The words joined by single spaces; unchanged in order when no word has a right-to-left letter.</returns>
    public static string ToLogical(IReadOnlyList<string> wordsLeftToRight)
    {
        ArgumentNullException.ThrowIfNull(wordsLeftToRight);

        var words = wordsLeftToRight.Where(word => !string.IsNullOrWhiteSpace(word)).Select(word => word.Trim()).ToList();
        if (words.Count == 0)
        {
            return string.Empty;
        }

        var own = words.Select(Classify).ToList();
        if (!own.Contains(Direction.RightToLeft))
        {
            return string.Join(' ', words);
        }

        // The line is right to left only when its right-to-left letters clearly dominate. Counting words is not enough:
        // an English window with an Arabic name (two short words) next to a long folder name is a left-to-right layout.
        var rightToLeftLetters = words.Sum(word => word.Count(IsRightToLeftLetter));
        var otherLetters = words.Sum(word => word.Count(c => char.IsLetter(c) && !IsRightToLeftLetter(c)));
        var paragraph = rightToLeftLetters >= RightToLeftDominance * otherLetters ? Direction.RightToLeft : Direction.LeftToRight;
        var resolved = Resolve(words, own, paragraph);
        var runs = Split(words, resolved);

        if (paragraph == Direction.RightToLeft)
        {
            runs.Reverse();
        }

        var builder = new StringBuilder();
        foreach (var (direction, run) in runs)
        {
            if (direction == Direction.RightToLeft)
            {
                run.Reverse();
            }

            foreach (var word in run)
            {
                if (builder.Length > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(word);
            }
        }

        return builder.ToString();
    }

    // A word that is only digits and punctuation has no direction of its own and takes one from its surroundings.
    private static Direction[] Resolve(List<string> words, List<Direction> own, Direction paragraph)
    {
        var resolved = own.ToArray();
        for (var i = 0; i < resolved.Length; i++)
        {
            if (resolved[i] != Direction.Neutral)
            {
                continue;
            }

            var previous = i > 0 ? resolved[i - 1] : Direction.Neutral;
            var next = NextStrong(own, i);

            if (previous == Direction.LeftToRight && words[i].Any(char.IsDigit))
            {
                // A number after English text is part of that text.
                resolved[i] = Direction.LeftToRight;
            }
            else if (previous != Direction.Neutral && previous == next)
            {
                resolved[i] = previous;
            }
            else
            {
                resolved[i] = paragraph;
            }
        }

        return resolved;
    }

    private static Direction NextStrong(List<Direction> own, int from)
    {
        for (var i = from + 1; i < own.Count; i++)
        {
            if (own[i] != Direction.Neutral)
            {
                return own[i];
            }
        }

        return Direction.Neutral;
    }

    private static List<(Direction Direction, List<string> Words)> Split(List<string> words, Direction[] resolved)
    {
        var runs = new List<(Direction, List<string>)>();
        for (var i = 0; i < words.Count; i++)
        {
            if (runs.Count == 0 || runs[^1].Item1 != resolved[i])
            {
                runs.Add((resolved[i], []));
            }

            runs[^1].Item2.Add(words[i]);
        }

        return runs;
    }

    // The direction of the first letter decides; a word with no letter is neutral.
    private static Direction Classify(string word)
    {
        foreach (var c in word)
        {
            if (IsRightToLeftLetter(c))
            {
                return Direction.RightToLeft;
            }

            if (char.IsLetter(c))
            {
                return Direction.LeftToRight;
            }
        }

        return Direction.Neutral;
    }

    internal static bool IsRightToLeftLetter(char c) =>
        char.IsLetter(c)
        && (int)c is (>= 0x0590 and <= 0x08FF) // Hebrew, Arabic, Syriac, Thaana, N'Ko, Samaritan, Mandaic, Arabic extended
            or (>= 0xFB1D and <= 0xFDFF) // Hebrew and Arabic presentation forms A
            or (>= 0xFE70 and <= 0xFEFF); // Arabic presentation forms B
}
