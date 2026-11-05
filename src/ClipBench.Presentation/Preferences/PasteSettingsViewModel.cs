using ClipBench.Core.Pasting;
using ClipBench.Core.Settings;
using ClipBench.Presentation.Popup;
using CommunityToolkit.Mvvm.Input;

namespace ClipBench.Presentation.Preferences;

/// <summary>The Paste section: putting the clipboard back after a paste, collecting, and what joins the clips of a multi-paste.</summary>
public sealed partial class PasteSettingsViewModel : SettingsSectionViewModel
{
    /// <summary>Creates the section.</summary>
    /// <param name="settings">The settings in force.</param>
    /// <param name="dispatcher">Moves updates to the UI thread.</param>
    public PasteSettingsViewModel(SettingsService settings, IUiDispatcher dispatcher)
        : base(settings, dispatcher)
    {
    }

    /// <summary>The separators offered as presets.</summary>
    public static IReadOnlyList<SeparatorOption> SeparatorOptions => SeparatorOption.Presets;

    /// <summary>True to put the user's clipboard back after a paste.</summary>
    public bool RestorePreviousClipboard
    {
        get => Current.RestorePreviousClipboard;
        set => _ = ChangeAsync(settings => settings with { RestorePreviousClipboard = value }, CancellationToken.None);
    }

    /// <summary>True to also keep each copy as its own clip while collecting.</summary>
    public bool KeepCopiesWhileCollecting
    {
        get => Current.KeepCopiesWhileCollecting;
        set => _ = ChangeAsync(settings => settings with { KeepCopiesWhileCollecting = value }, CancellationToken.None);
    }

    /// <summary>The separator in use.</summary>
    public SeparatorChoice SelectedSeparator
    {
        get => Current.Separator;
        set => _ = ChangeAsync(settings => settings with { Separator = value }, CancellationToken.None);
    }

    /// <summary>True when the separator in use is the one the user typed.</summary>
    public bool IsCustomSelected => Current.Separator == SeparatorChoice.Custom;

    /// <summary>The custom separator as it was typed, with escapes such as \t unresolved.</summary>
    public string CustomSeparator => Current.CustomSeparator;

    [RelayCommand]
    private async Task ApplyCustomSeparatorAsync(string typed, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(typed);
        ErrorMessage = null;
        if (typed.Length == 0)
        {
            _ = await ChangeAsync(settings => settings with { Separator = SeparatorChoice.None }, cancellationToken);
            return;
        }

        var parsed = Separator.Parse(typed);
        if (parsed.IsFailure)
        {
            ErrorMessage = parsed.Error.Message;
            return;
        }

        _ = await ChangeAsync(settings => settings with { Separator = SeparatorChoice.Custom, CustomSeparator = typed }, cancellationToken);
    }
}
