using ClipBench.Core.Results;

namespace ClipBench.Core.Transforms;

/// <summary>
/// A change applied to text as it is pasted. It is a pure function: the same text always gives the same result, and
/// the stored clip is never touched. Text that a transform cannot handle comes back as a failure, never as an exception.
/// </summary>
public sealed class TextTransform
{
    private readonly Func<string, Result<string>> _apply;

    /// <summary>Creates a transform.</summary>
    /// <param name="id">A short, stable identifier for logs and settings, such as <c>case.upper</c>. Never user text.</param>
    /// <param name="name">What the picker shows.</param>
    /// <param name="description">One line that says what the transform does.</param>
    /// <param name="apply">The change.</param>
    public TextTransform(string id, string name, string description, Func<string, Result<string>> apply)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(apply);
        Id = id;
        Name = name;
        Description = description;
        _apply = apply;
    }

    /// <summary>A short, stable identifier.</summary>
    public string Id { get; }

    /// <summary>What the picker shows.</summary>
    public string Name { get; }

    /// <summary>One line that says what the transform does.</summary>
    public string Description { get; }

    /// <summary>Changes the text.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The changed text, or why the text could not be changed.</returns>
    public Result<string> Apply(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return _apply(text);
    }

    /// <summary>Printing shows the identifier only.</summary>
    /// <returns>The identifier.</returns>
    public override string ToString() => Id;
}
