using System.Buffers;
using System.Globalization;
using System.Text;
using ClipBench.Core.Results;

namespace ClipBench.Core.Boards;

/// <summary>The rules for what a board may be called.</summary>
public static class BoardName
{
    /// <summary>The longest name, in characters.</summary>
    public const int MaxLength = 64;

    /// <summary>The most boards there can be.</summary>
    public const int MaxBoards = 100;

    /// <summary>Checks a name and trims it.</summary>
    /// <param name="name">What the user typed.</param>
    /// <returns>The name without white space at either end, or why it cannot be used.</returns>
    public static Result<string> Validate(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var trimmed = name.Trim();
        if (trimmed.Length == 0)
        {
            return Refused("Give the board a name.");
        }

        if (trimmed.Length > MaxLength)
        {
            return Refused($"A board name can have at most {MaxLength} characters.");
        }

        return HasUnusableCharacter(trimmed)
            ? Refused("A board name cannot hold line breaks or other control characters.")
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

    private static Result<string> Refused(string message) =>
        Result.Failure<string>(new ResultError(ErrorCode.InvalidBoardName, message));
}
