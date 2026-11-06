using System.Text;

namespace ClipBench.Core.Screen;

/// <summary>
/// Writes a reading out as the text the user gets: the lines in order, each indented as far as its place shows, the words spaced the way
/// the font spaces them, and two misreadings that come from the shapes of screen fonts put right (a list bullet read as a letter, the
/// dotted or slashed zero of a code font read as "@"). Right-to-left lines are written in reading order and otherwise left alone.
/// </summary>
internal static class ReadingText
{
    // A font is taken to be monospaced when the width of a letter, over the words of three letters or more, varies by less than this share
    // (0.03 in the pictures measured, 0.15 and more for proportional text), judged on at least this many words.
    private const double MonospacedSpread = 0.08;
    private const int MinWordsToJudgeFont = 3;

    // A word whose letters come out more than this many times wider or narrower than the median is a box the engine got wrong (one stretched
    // over the words after it was 1.6 times too wide) and is left out of the judgement, as long as at least the given share of words is left
    // and there are enough words for one odd one to be a wrong box rather than a sign of a proportional font.
    private const double WrongBoxFactor = 1.5;
    private const double MostWordsShare = 0.75;
    private const int MinWordsToSetAsideWrongBoxes = 6;

    // In a monospaced font two words closer than this many letters have no space between them (a real space is a letter wide; the narrow dot
    // that separated "Model." from "from_pretrained(" left 0.74 of a letter, the narrowest real space measured 1.1).
    private const double JoinBelowLetters = 0.9;

    // The most spaces put before a line, so a word placed wrongly cannot make a line of thousands of spaces.
    private const int MaxIndent = 40;

    // A list bullet is a dot well under the height of a lowercase letter: 10 pixels against 16 in the measured chat window. A real "e" or
    // "o" is as tall as the other lowercase letters.
    private const double BulletAtMostOfLetter = 0.75;

    // A bullet is about as wide as it is tall.
    private const double SquareWithin = 1.6;

    // What the English model reads a round list bullet or a terminal's round command mark as: alone, or with a second mark beside it.
    // Written as code points: cent, copyright, left guillemet, registered, degree, middle dot, bullet.
    private static readonly string _bulletLookalikes = "eo@*?" + new string([(char)0xA2, (char)0xA9, (char)0xAB, (char)0xAE, (char)0xB0, (char)0xB7, (char)0x2022]);

    // Lowercase letters that reach neither above nor below the line, so a word made of them shows how tall a lowercase letter is.
    private const string ShortLetters = "acemnorsuvwxz";

    private const char Bullet = (char)0x2022;

    private const string Openers = "([{";
    private const string Closers = ")]}";

    // What a closing bracket alone on a line has been read as: "1" for "]", "d" for ")"; and the other single strokes like them.
    private const string StrokeLookalikes = "1lI|d";

    /// <summary>Writes the reading out.</summary>
    /// <param name="lines">The reading.</param>
    /// <returns>The lines joined with a line break.</returns>
    public static string Write(IReadOnlyList<RecognizedLine> lines)
    {
        var letter = LetterWidth(lines);
        var indents = Indents(lines, letter);
        var joinBelow = letter is { Monospaced: true } ? letter.Value.Width * JoinBelowLetters : 0;
        var lowercase = LowercaseHeight(lines);
        var bodies = lines.Select(line => LineText(line, joinBelow, lowercase)).ToArray();
        if (letter is { Monospaced: true })
        {
            Brackets(lines, bodies, indents);
        }

        return string.Join("\r\n", bodies.Select((body, i) => new string(' ', indents[i]) + body));
    }

    // In code, which closing bracket comes next is known from the ones still open. The English model reads the "]" of a blue JSON list as ")",
    // a lone "]," at the end of a list as "1," and a lone ")" at the end of a call as "d". So, over the lines in order and outside quotes, a
    // closing bracket that matches no open bracket is the one that closes the innermost open bracket; and a line that is only one such stroke
    // (with a comma or a semicolon after it) is that closing bracket when it stands at the indent of the line that opened the bracket.
    // The changes are kept only when they leave fewer brackets unmatched: in "string]]args)" the first "]" is a misread "[", not a ")".
    private static void Brackets(IReadOnlyList<RecognizedLine> lines, string[] bodies, int[] indents)
    {
        var closed = (string[])bodies.Clone();
        Close(lines, closed, indents);
        if (Unmatched(lines, closed) < Unmatched(lines, bodies))
        {
            Array.Copy(closed, bodies, bodies.Length);
        }
    }

    // How many brackets in the reading, outside quotes, close nothing or are left open.
    private static int Unmatched(IReadOnlyList<RecognizedLine> lines, string[] bodies)
    {
        var open = new List<char>();
        var unmatched = 0;
        for (var i = 0; i < bodies.Length; i++)
        {
            if (LineOrder.ContainsRightToLeft(lines[i].Text))
            {
                continue;
            }

            foreach (var character in OutsideQuotes(bodies[i]))
            {
                if (Openers.Contains(character, StringComparison.Ordinal))
                {
                    open.Add(character);
                }
                else if (Closers.IndexOf(character, StringComparison.Ordinal) is var closes and >= 0)
                {
                    if (open.Count > 0 && open[^1] == Openers[closes])
                    {
                        open.RemoveAt(open.Count - 1);
                    }
                    else
                    {
                        unmatched++;
                    }
                }
            }
        }

        return unmatched + open.Count;
    }

    private static IEnumerable<char> OutsideQuotes(string text)
    {
        var quote = '\0';
        for (var c = 0; c < text.Length; c++)
        {
            var character = text[c];
            if (quote != '\0')
            {
                quote = character == quote && (c == 0 || text[c - 1] != '\\') ? '\0' : quote;
            }
            else if (character is '"' or '\'')
            {
                quote = character;
            }
            else
            {
                yield return character;
            }
        }
    }

    private static void Close(IReadOnlyList<RecognizedLine> lines, string[] bodies, int[] indents)
    {
        var open = new List<(char Bracket, int Line)>();
        for (var i = 0; i < bodies.Length; i++)
        {
            if (LineOrder.ContainsRightToLeft(lines[i].Text))
            {
                continue;
            }

            var body = bodies[i];
            if (open.Count > 0 && IsLoneStroke(body) && indents[i] == indents[open[^1].Line])
            {
                bodies[i] = CloserOf(open[^1].Bracket) + body[1..];
                open.RemoveAt(open.Count - 1);
                continue;
            }

            var characters = body.ToCharArray();
            var quote = '\0';
            for (var c = 0; c < characters.Length; c++)
            {
                var character = characters[c];
                if (quote != '\0')
                {
                    quote = character == quote && (c == 0 || characters[c - 1] != '\\') ? '\0' : quote;
                    continue;
                }

                if (character is '"' or '\'')
                {
                    quote = character;
                    continue;
                }

                if (Openers.Contains(character, StringComparison.Ordinal))
                {
                    open.Add((character, i));
                    continue;
                }

                var closes = Closers.IndexOf(character, StringComparison.Ordinal);
                if (closes < 0 || open.Count == 0)
                {
                    continue;
                }

                var expected = Openers[closes];
                if (open[^1].Bracket == expected)
                {
                    open.RemoveAt(open.Count - 1);
                }
                else if (!open.Exists(item => item.Bracket == expected))
                {
                    characters[c] = CloserOf(open[^1].Bracket);
                    open.RemoveAt(open.Count - 1);
                }
            }

            bodies[i] = new string(characters);
        }
    }

    private static char CloserOf(char opener) => Closers[Openers.IndexOf(opener, StringComparison.Ordinal)];

    private static bool IsLoneStroke(string body) =>
        body.Length is 1 or 2 && StrokeLookalikes.Contains(body[0], StringComparison.Ordinal) && (body.Length == 1 || body[1] is ',' or ';');

    // The width of a letter in a reading (the median over its words) and whether the letters all have the same width, as in the fonts code is shown in.
    private static (double Width, bool Monospaced)? LetterWidth(IReadOnlyList<RecognizedLine> lines)
    {
        var widths = lines.SelectMany(line => line.Words).Where(word => word.Text.Length >= 3 && word.Width > 0).Select(word => word.Width / word.Text.Length).Order().ToList();
        if (widths.Count == 0)
        {
            return null;
        }

        var median = widths[widths.Count / 2];
        if (widths.Count < MinWordsToJudgeFont)
        {
            return (median, false);
        }

        if (Spread(widths) < MonospacedSpread)
        {
            return (median, true);
        }

        var kept = widths.Where(width => width <= median * WrongBoxFactor && width >= median / WrongBoxFactor).ToList();
        return (median, widths.Count >= MinWordsToSetAsideWrongBoxes && kept.Count >= widths.Count * MostWordsShare && Spread(kept) < MonospacedSpread);
    }

    // How much the widths vary, as a share of their mean.
    private static double Spread(List<double> widths)
    {
        var mean = widths.Average();
        return Math.Sqrt(widths.Sum(width => (width - mean) * (width - mean)) / widths.Count) / mean;
    }

    // How tall a lowercase letter is in the reading: the median height of the words made only of short letters. Null when there are none.
    private static double? LowercaseHeight(IReadOnlyList<RecognizedLine> lines)
    {
        var heights = lines
            .SelectMany(line => line.Words.Select((word, i) => (Word: word, First: i == 0)))
            .Where(item => !(item.First && LooksLikeBullet(item.Word.Text)) && item.Word.Height > 0 && item.Word.Text.All(ShortLetters.Contains))
            .Select(item => item.Word.Height)
            .Order()
            .ToList();
        return heights.Count == 0 ? null : heights[heights.Count / 2];
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

    private static string LineText(RecognizedLine line, double joinBelow, double? lowercase)
    {
        if (LineOrder.ContainsRightToLeft(line.Text))
        {
            return LineOrder.ToLogical([.. line.Words.OrderBy(word => word.X).Select(word => word.Text)]);
        }

        // Where the line's text is not its words (an engine that gives both), the text is used as it is.
        var words = line.Words;
        if (words.Count == 0 || line.Text != string.Join(' ', words.Select(word => word.Text)))
        {
            return Zeros(line.Text);
        }

        var bullet = words.Count > 1 && IsBullet(words[0], lowercase);
        var text = new StringBuilder(bullet ? Bullet.ToString() : words[0].Text);
        for (var i = 1; i < words.Count; i++)
        {
            // The engine puts a space between two words it sees apart, and in code it sees "Model." and "from_pretrained(" apart because a dot
            // is narrow. In a monospaced font a real space is a whole letter wide, so less than that between two words is no space. A bullet is
            // always followed by one.
            var gap = words[i].X - (words[i - 1].X + words[i - 1].Width);
            var joined = joinBelow > 0 && gap < joinBelow && !(bullet && i == 1);
            _ = joined ? text.Append(words[i].Text) : text.Append(' ').Append(words[i].Text);
        }

        return Zeros(text.ToString());
    }

    private static bool LooksLikeBullet(string text) =>
        text.Length is 1 or 2 && text[0] != '?' && text.All(_bulletLookalikes.Contains);

    // The first word of a line is a bullet when it is one or two of the marks a bullet is read as, about square, and either "@" (which is
    // never a word on its own) or clearly smaller than a lowercase letter.
    private static bool IsBullet(RecognizedWord word, double? lowercase) =>
        LooksLikeBullet(word.Text)
        && word.Width > 0
        && word.Height > 0
        && Math.Max(word.Width, word.Height) <= Math.Min(word.Width, word.Height) * SquareWithin
        && (word.Text == "@" || (lowercase is { } height && word.Height <= height * BulletAtMostOfLetter));

    // The zero of a code font has a dot or a slash inside, and the English model reads it as "@". An "@" is put back as "0" where it stands in
    // a number: after a digit, or after a dot that follows a digit, and before a digit, a dot, a separator or the end of the word; or as the
    // first digit of a version ("v@.2"). An "@" in an address, a decorator or an operator never stands there.
    private static string Zeros(string text)
    {
        if (!text.Contains('@', StringComparison.Ordinal))
        {
            return text;
        }

        var characters = text.ToCharArray();
        for (var i = 0; i < characters.Length; i++)
        {
            if (characters[i] == '@' && (InNumber(characters, i) || StartsVersion(characters, i)))
            {
                characters[i] = '0';
            }
        }

        return new string(characters);
    }

    private static bool InNumber(char[] text, int at)
    {
        var before = at > 0 ? text[at - 1] : ' ';
        var afterDigit = char.IsAsciiDigit(before) || (before == '.' && at > 1 && char.IsAsciiDigit(text[at - 2]));
        if (!afterDigit)
        {
            return false;
        }

        var after = at + 1 < text.Length ? text[at + 1] : ' ';
        return char.IsWhiteSpace(after) || char.IsAsciiDigit(after) || ".-\\/,;:)]}\"'".Contains(after, StringComparison.Ordinal);
    }

    private static bool StartsVersion(char[] text, int at) =>
        at > 0
        && text[at - 1] is 'v' or 'V'
        && (at == 1 || char.IsWhiteSpace(text[at - 2]) || text[at - 2] is '(' or '"' or '\'' or '`')
        && at + 2 < text.Length
        && text[at + 1] == '.'
        && char.IsAsciiDigit(text[at + 2]);
}
