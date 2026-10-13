using System.Globalization;
using System.Text;

namespace ClipBench.Core.Search;

/// <summary>
/// Turns text into the form searches compare: lower case, one line, accents and Arabic or Hebrew
/// marks removed, digits written the same way, and invisible direction marks dropped. The same
/// treatment is applied to the clip text and to the query, so both always meet in the middle.
/// </summary>
internal static class SearchText
{
    public static string Normalize(string text, int maxCharacters)
    {
        var head = text.Length > maxCharacters ? text[..maxCharacters] : text;

        // Normalization throws on invalid UTF-16 (a lone surrogate), and clipboard text can contain it.
        // Enumerating runes replaces each invalid unit with U+FFFD instead.
        var valid = new StringBuilder(head.Length);
        foreach (var rune in head.EnumerateRunes())
        {
            valid.Append(rune.ToString());
        }

        var result = new StringBuilder(valid.Length);
        var pendingSpace = false;

        foreach (var rune in valid.ToString().Normalize(NormalizationForm.FormKD).EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(rune))
            {
                pendingSpace = result.Length > 0;
                continue;
            }

            if (IsIgnorable(rune.Value))
            {
                continue;
            }

            if (pendingSpace)
            {
                result.Append(' ');
                pendingSpace = false;
            }

            result.Append(Fold(rune).ToString());
        }

        return result.ToString();
    }

    private static Rune Fold(Rune rune)
    {
        if (Rune.GetUnicodeCategory(rune) == UnicodeCategory.DecimalDigitNumber)
        {
            return new Rune('0' + (int)Rune.GetNumericValue(rune));
        }

        return Rune.ToLowerInvariant(rune);
    }

    // Marks that change how text looks but not what it says, for the scripts where people search without them.
    private static bool IsIgnorable(int value) => value switch
    {
        >= 0x0300 and <= 0x036F => true,   // combining diacritical marks (Latin accents)
        0x061C => true,                    // Arabic letter mark
        0x0640 => true,                    // Arabic tatweel (the stretching stroke)
        >= 0x064B and <= 0x065F => true,   // Arabic vowel marks, hamza above and below
        0x0670 => true,                    // Arabic superscript alef
        >= 0x06D6 and <= 0x06ED => true,   // Quranic annotation marks
        >= 0x0591 and <= 0x05C7 => true,   // Hebrew points and cantillation
        >= 0x200B and <= 0x200F => true,   // zero width space, joiners and direction marks
        >= 0x202A and <= 0x202E => true,   // embedding and override controls
        >= 0x2066 and <= 0x2069 => true,   // isolate controls
        >= 0xFE00 and <= 0xFE0F => true,   // variation selectors (emoji presentation)
        >= 0xFE20 and <= 0xFE2F => true,   // combining half marks
        0xFEFF => true,                    // zero width no-break space
        _ => false,
    };
}
