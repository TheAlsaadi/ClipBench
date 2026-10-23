using ClipBench.Core.Results;

namespace ClipBench.Core.Snippets;

/// <summary>Identifies one snippet. Wraps a GUID so ids cannot be confused with other values.</summary>
/// <param name="Value">The underlying GUID.</param>
public readonly record struct SnippetId(Guid Value)
{
    /// <summary>Creates a new, unique, time-ordered id.</summary>
    public static SnippetId New() => new(Guid.CreateVersion7());
}

/// <summary>A reusable piece of text with variables, such as the date, filled in when it is pasted.</summary>
/// <param name="Id">Identifies the snippet.</param>
/// <param name="Name">What the user calls it.</param>
/// <param name="Body">The text, with variables in braces.</param>
/// <param name="Position">Where it comes in the list. Snippets are numbered as they are made.</param>
public sealed record Snippet(SnippetId Id, string Name, string Body, int Position)
{
    /// <summary>What the user calls the snippet.</summary>
    public string Name
    {
        get;
        init => field = value ?? throw new ArgumentNullException(nameof(value));
    } = Name ?? throw new ArgumentNullException(nameof(Name));

    /// <summary>The text, with variables in braces.</summary>
    public string Body
    {
        get;
        init => field = value ?? throw new ArgumentNullException(nameof(value));
    } = Body ?? throw new ArgumentNullException(nameof(Body));

    /// <summary>Printing shows the id only: a name and a body are what the user typed.</summary>
    /// <returns>The id.</returns>
    public override string ToString() => $"Snippet {Id.Value:N}";
}

/// <summary>The rules for what a snippet may be called.</summary>
public static class SnippetName
{
    /// <summary>The longest name, in characters.</summary>
    public const int MaxLength = 64;

    /// <summary>The most snippets there can be.</summary>
    public const int MaxSnippets = 500;

    /// <summary>Checks a name and trims it.</summary>
    /// <param name="name">What the user typed.</param>
    /// <returns>The name without white space at either end, or why it cannot be used.</returns>
    public static Result<string> Validate(string name) => NameRules.Validate(name, MaxLength, "snippet", ErrorCode.InvalidSnippetName);
}
