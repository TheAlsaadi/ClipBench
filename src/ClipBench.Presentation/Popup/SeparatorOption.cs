using ClipBench.Core.Settings;

namespace ClipBench.Presentation.Popup;

/// <summary>A separator the user can pick, with the words the popup shows for it.</summary>
/// <param name="Choice">The choice saved in the settings.</param>
/// <param name="Label">What the popup shows.</param>
public sealed record SeparatorOption(SeparatorChoice Choice, string Label)
{
    /// <summary>The presets, in the order Ctrl+S moves through them.</summary>
    public static IReadOnlyList<SeparatorOption> Presets { get; } =
    [
        new(SeparatorChoice.NewLine, "New line"),
        new(SeparatorChoice.BlankLine, "Blank line"),
        new(SeparatorChoice.CommaSpace, "Comma and space"),
        new(SeparatorChoice.Comma, "Comma"),
        new(SeparatorChoice.Semicolon, "Semicolon"),
        new(SeparatorChoice.Tab, "Tab"),
        new(SeparatorChoice.Space, "Space"),
        new(SeparatorChoice.None, "Nothing"),
    ];

    /// <summary>What the popup shows for the separator in the settings.</summary>
    /// <param name="settings">The settings.</param>
    /// <returns>The preset's label, or "Custom" and the separator as it was typed, in quotes so spaces show.</returns>
    public static string LabelFor(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.Separator == SeparatorChoice.Custom
            ? $"Custom \"{settings.CustomSeparator}\""
            : (Presets.FirstOrDefault(option => option.Choice == settings.Separator) ?? Presets[0]).Label;
    }
}
