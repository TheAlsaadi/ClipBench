using ClipBench.Core.Hotkeys;
using ClipBench.Core.Results;
using ClipBench.Core.Settings;
using ClipBench.Presentation.Popup;
using CommunityToolkit.Mvvm.Input;

namespace ClipBench.Presentation.Preferences;

/// <summary>
/// The Shortcuts section: the global shortcuts that open the popup and start or finish collecting. A new shortcut is
/// claimed from Windows first and saved only when Windows gave it, so a refused change leaves the old shortcut working.
/// </summary>
public sealed partial class HotkeysSettingsViewModel : SettingsSectionViewModel
{
    private readonly HotkeyBindings _bindings;

    /// <summary>Creates the section.</summary>
    /// <param name="settings">The settings in force.</param>
    /// <param name="bindings">Claims the shortcuts from Windows.</param>
    /// <param name="dispatcher">Moves updates to the UI thread.</param>
    public HotkeysSettingsViewModel(SettingsService settings, HotkeyBindings bindings, IUiDispatcher dispatcher)
        : base(settings, dispatcher)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        _bindings = bindings;
    }

    /// <summary>The shortcut that opens the popup, as it is typed, for example <c>Win+Alt+V</c>.</summary>
    public string PopupHotkey => Current.PopupHotkey.ToString();

    /// <summary>The shortcut that starts and finishes collecting.</summary>
    public string CollectHotkey => Current.CollectHotkey.ToString();

    /// <summary>The shortcut that copies text from the screen.</summary>
    public string CaptureTextHotkey => Current.CaptureTextHotkey.ToString();

    /// <summary>Gives an action a new shortcut.</summary>
    /// <param name="action">The action.</param>
    /// <param name="typed">The shortcut as typed, such as <c>Ctrl+Alt+K</c>.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>True when the shortcut is in use and saved. Otherwise <see cref="SettingsSectionViewModel.ErrorMessage"/> says why.</returns>
    public async Task<bool> SetAsync(HotkeyAction action, string typed, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(typed);
        ErrorMessage = null;

        var parsed = Hotkey.Parse(typed);
        if (parsed.IsFailure)
        {
            ErrorMessage = parsed.Error.Message;
            return false;
        }

        var hotkey = parsed.Value;
        var bound = await _bindings.BindAsync(action, hotkey, cancellationToken);
        if (bound.IsFailure)
        {
            ErrorMessage = bound.Error.Code == ErrorCode.HotkeyUnavailable
                ? $"Another program already uses {hotkey}. Choose a different shortcut."
                : bound.Error.Message;
            return false;
        }

        return await ChangeAsync(
            settings => action switch
            {
                HotkeyAction.OpenPopup => settings with { PopupHotkey = hotkey },
                HotkeyAction.ToggleCollect => settings with { CollectHotkey = hotkey },
                _ => settings with { CaptureTextHotkey = hotkey },
            },
            cancellationToken);
    }

    [RelayCommand]
    private async Task ResetPopupAsync(CancellationToken cancellationToken) =>
        _ = await SetAsync(HotkeyAction.OpenPopup, Hotkey.Default.ToString(), cancellationToken);

    [RelayCommand]
    private async Task ResetCollectAsync(CancellationToken cancellationToken) =>
        _ = await SetAsync(HotkeyAction.ToggleCollect, Hotkey.DefaultCollect.ToString(), cancellationToken);

    [RelayCommand]
    private async Task ResetCaptureTextAsync(CancellationToken cancellationToken) =>
        _ = await SetAsync(HotkeyAction.CaptureText, Hotkey.DefaultCaptureText.ToString(), cancellationToken);
}
