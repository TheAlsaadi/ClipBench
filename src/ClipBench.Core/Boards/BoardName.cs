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
    public static Result<string> Validate(string name) => NameRules.Validate(name, MaxLength, "board", ErrorCode.InvalidBoardName);
}
