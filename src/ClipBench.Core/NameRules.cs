using System.Buffers;
using System.Globalization;
using System.Text;
using ClipBench.Core.Results;

namespace ClipBench.Core;

/// <summary>What a name the user types for a board or a snippet may be. Both follow the same rules.</summary>
internal static class NameRules
{
    /// <summary>Checks a name and trims it.</summary>
    /// <param name="name">What the user typed.</param>
    /// <param name="maxLength">The longest name, in characters, after trimming.</param>
    /// <param name="noun">What is being named, for the messages: "board" or "snippet".</param>
    /// <param name="code">The error code for a name that is refused.</param>
    /// <returns>The name without white space at either end, or why it cannot be used.</returns>
    public static Result<string> Validate(string name, int maxLength, string noun, ErrorCode code)
    {
        ArgumentNullException.ThrowIfNull(name);

        var trimmed = name.Trim();
        if (trimmed.Length == 0)
        {
            return Refused($"Give the {noun} a name.", code);
        }

        if (trimmed.Length > maxLength)
        {
            return Refused($"A {noun} name can have at most {maxLength} characters.", code);
        }

        return HasUnusableCharacter(trimmed)
            ? Refused($"A {noun} name cannot hold line breaks or other control characters.", code)
            : Result.Success(trimmed);
    }

    // Control characters and line or paragraph separators, and half of a surrogate pair, which cannot be saved as text.
    private static bool HasUnusableCharacter(string name)
    {
        var index = 0;
        while (index < name.Length)
        {
            if (Rune.DecodeFromUtf16(name.AsSpan(index), out var rune, out var consumed) != OperationStatus.Done)
            {
                return true;
            }

            if (Rune.IsControl(rune) || Rune.GetUnicodeCategory(rune) is UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator)
            {
                return true;
            }

            index += consumed;
        }

        return false;
    }

    private static Result<string> Refused(string message, ErrorCode code) =>
        Result.Failure<string>(new ResultError(code, message));
}
