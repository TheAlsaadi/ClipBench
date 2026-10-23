using ClipBench.Core.Snippets;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClipBench.Presentation.Snippets;

/// <summary>One snippet as a row of the popup list.</summary>
public sealed class SnippetRowViewModel
{
    /// <summary>Creates the row for a snippet.</summary>
    /// <param name="id">The snippet id.</param>
    /// <param name="name">What the snippet is called.</param>
    /// <param name="preview">The first lines of its text, cut short.</param>
    /// <param name="inputCount">How many values it asks for before it is pasted.</param>
    public SnippetRowViewModel(SnippetId id, string name, string preview, int inputCount)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(preview);
        Id = id;
        Name = name;
        Preview = preview;
        InputCount = inputCount;
    }

    /// <summary>The snippet id.</summary>
    public SnippetId Id { get; }

    /// <summary>What the snippet is called.</summary>
    public string Name { get; }

    /// <summary>The first lines of its text, cut short.</summary>
    public string Preview { get; }

    /// <summary>How many values it asks for before it is pasted.</summary>
    public int InputCount { get; }

    /// <summary>A line under the preview: how many values it asks for, or nothing.</summary>
    public string Details => InputCount switch
    {
        0 => string.Empty,
        1 => "Asks for 1 value",
        _ => $"Asks for {InputCount} values",
    };

    /// <summary>True when there is something to say under the preview.</summary>
    public bool HasDetails => InputCount > 0;

    /// <summary>The text a screen reader says.</summary>
    public string AutomationName => HasDetails ? $"Snippet {Name}, {Preview.Replace('\n', ' ')}, {Details}" : $"Snippet {Name}, {Preview.Replace('\n', ' ')}";

    /// <summary>Printing never shows the name or the text: they are what the user typed.</summary>
    /// <returns>The id.</returns>
    public override string ToString() => $"Snippet row {Id.Value:N}";
}

/// <summary>One value the user is asked for before a snippet is pasted.</summary>
public sealed partial class SnippetFieldViewModel : ObservableObject
{
    /// <summary>Creates a field.</summary>
    /// <param name="name">What the snippet calls the value.</param>
    public SnippetFieldViewModel(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        Name = name;
    }

    /// <summary>What the snippet calls the value.</summary>
    public string Name { get; }

    /// <summary>What the user typed.</summary>
    [ObservableProperty]
    public partial string Value { get; set; } = string.Empty;

    /// <summary>Printing never shows what was typed.</summary>
    /// <returns>The type name.</returns>
    public override string ToString() => nameof(SnippetFieldViewModel);
}

/// <summary>How starting to paste a snippet went.</summary>
public enum SnippetStartKind
{
    /// <summary>The text is made and can be pasted.</summary>
    Ready,

    /// <summary>The snippet asks for values, and the prompt is open.</summary>
    NeedsInput,

    /// <summary>The snippet cannot be pasted.</summary>
    Failed,
}

/// <summary>The outcome of starting to paste a snippet.</summary>
/// <param name="Kind">What happened.</param>
/// <param name="Text">The text to paste, when <paramref name="Kind"/> is <see cref="SnippetStartKind.Ready"/>.</param>
/// <param name="Error">Why it failed, when <paramref name="Kind"/> is <see cref="SnippetStartKind.Failed"/>.</param>
public readonly record struct SnippetStart(SnippetStartKind Kind, string Text, string? Error)
{
    /// <summary>Printing never shows the text.</summary>
    /// <returns>The kind.</returns>
    public override string ToString() => $"SnippetStart ({Kind})";
}
