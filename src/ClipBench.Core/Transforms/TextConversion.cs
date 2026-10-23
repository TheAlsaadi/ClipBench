using System.Buffers;
using System.Globalization;
using System.Text;

namespace ClipBench.Core.Transforms;

/// <summary>
/// Title case, sentence case and joining lines. The case changes use the invariant culture, so the result does not depend on the language of
/// Windows, and both read the text one Unicode character at a time, so letters outside the basic plane and combining
/// marks are handled as whole characters. Names and acronyms are not recognised: "NASA" becomes "Nasa".
/// </summary>
internal static class TextConversion
{
    private const char Apostrophe = '\'';
    private const char RightSingleQuote = (char)0x2019;
    private const char Ellipsis = (char)0x2026;
    private const char ArabicQuestionMark = (char)0x061F;
    private const char IdeographicFullStop = (char)0x3002;
    private const char FullwidthExclamation = (char)0xFF01;
    private const char FullwidthQuestion = (char)0xFF1F;
    private const char NextLine = (char)0x0085;

    private static readonly SearchValues<char> _sentenceEnds =
        SearchValues.Create(['.', '!', '?', Ellipsis, ArabicQuestionMark, IdeographicFullStop, FullwidthExclamation, FullwidthQuestion]);

    private static readonly SearchValues<char> _lineBreaks =
        SearchValues.Create(['\r', '\n', NextLine, (char)0x2028, (char)0x2029]);

    /// <summary>Capitalises the first letter of each word and lower-cases the others.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The text in title case.</returns>
    public static string Title(string text)
    {
        var builder = new StringBuilder(text.Length);
        var inWord = false;
        var index = 0;
        while (index < text.Length)
        {
            var status = Rune.DecodeFromUtf16(text.AsSpan(index), out var rune, out var consumed);
            if (status != OperationStatus.Done)
            {
                // A lone surrogate is kept exactly as it came.
                builder.Append(text[index]);
                inWord = false;
                index++;
                continue;
            }

            if (Rune.IsLetter(rune))
            {
                builder.Append((inWord ? Rune.ToLowerInvariant(rune) : Rune.ToUpperInvariant(rune)).ToString());
                inWord = true;
            }
            else
            {
                builder.Append(text, index, consumed);
                inWord = Rune.IsDigit(rune) || (inWord && (IsApostrophe(rune) || IsCombiningMark(rune)));
            }

            index += consumed;
        }

        return builder.ToString();
    }

    /// <summary>Capitalises the first letter of each sentence and lower-cases the others.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The text in sentence case.</returns>
    public static string Sentence(string text)
    {
        var builder = new StringBuilder(text.Length);
        var capitalizeNext = true;
        var index = 0;
        while (index < text.Length)
        {
            var status = Rune.DecodeFromUtf16(text.AsSpan(index), out var rune, out var consumed);
            if (status != OperationStatus.Done)
            {
                builder.Append(text[index]);
                index++;
                continue;
            }

            if (Rune.IsLetter(rune))
            {
                builder.Append((capitalizeNext ? Rune.ToUpperInvariant(rune) : Rune.ToLowerInvariant(rune)).ToString());
                capitalizeNext = false;
            }
            else
            {
                builder.Append(text, index, consumed);
                if (Rune.IsDigit(rune))
                {
                    capitalizeNext = false;
                }
                else if (consumed == 1 && _lineBreaks.Contains(text[index]))
                {
                    capitalizeNext = true;
                }
                else if (consumed == 1 && _sentenceEnds.Contains(text[index]) && EndsSentence(text, index + 1))
                {
                    capitalizeNext = true;
                }
            }

            index += consumed;
        }

        return builder.ToString();
    }

    /// <summary>Joins the lines into one. A run of line breaks, with the spaces around it, becomes one space.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The text on one line. Breaks at the very start or end are dropped.</returns>
    public static string RemoveLineBreaks(string text)
    {
        if (text.AsSpan().IndexOfAny(_lineBreaks) < 0)
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        var index = 0;
        while (index < text.Length)
        {
            var c = text[index];
            if (!_lineBreaks.Contains(c))
            {
                builder.Append(c);
                index++;
                continue;
            }

            // The spaces before the break go with it.
            while (builder.Length > 0 && char.IsWhiteSpace(builder[^1]))
            {
                builder.Length--;
            }

            while (index < text.Length && char.IsWhiteSpace(text[index]))
            {
                index++;
            }

            if (builder.Length > 0 && index < text.Length)
            {
                builder.Append(' ');
            }
        }

        return builder.ToString();
    }

    // A full stop ends a sentence only when white space or the end of the text follows, so "3.14" and "a.b" do not.
    private static bool EndsSentence(string text, int next) => next >= text.Length || char.IsWhiteSpace(text[next]);

    private static bool IsApostrophe(Rune rune) => rune.Value is Apostrophe or RightSingleQuote;

    private static bool IsCombiningMark(Rune rune) =>
        Rune.GetUnicodeCategory(rune) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark;
}
