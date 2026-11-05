using ClipBench.Core.Settings;
using ClipBench.Presentation.Popup;
using CommunityToolkit.Mvvm.Input;

namespace ClipBench.Presentation.Preferences;

/// <summary>The General section: starting with Windows, and the question asked the first time ClipBench runs.</summary>
public sealed partial class GeneralSettingsViewModel : SettingsSectionViewModel
{
    /// <summary>What is shown when starting with Windows cannot be changed here.</summary>
    public const string StartupUnavailable = "ClipBench cannot tell Windows where it is installed, so it cannot be set to start with Windows from here.";

    private readonly StartWithWindowsService? _startup;

    /// <summary>Creates the section.</summary>
    /// <param name="settings">The settings in force.</param>
    /// <param name="startup">Writes the start-up entry, or null when ClipBench does not know its own path.</param>
    /// <param name="dispatcher">Moves updates to the UI thread.</param>
    public GeneralSettingsViewModel(SettingsService settings, StartWithWindowsService? startup, IUiDispatcher dispatcher)
        : base(settings, dispatcher)
    {
        _startup = startup;
    }

    /// <summary>True when the choice can be made.</summary>
    public bool CanStartWithWindows => _startup is not null;

    /// <summary>True when ClipBench starts when the user signs in. Setting it writes or removes Windows' entry.</summary>
    public bool StartWithWindows
    {
        get => Current.StartWithWindows;
        set => _ = SetStartWithWindowsAsync(value, CancellationToken.None);
    }

    /// <summary>True until the first-run question has been answered.</summary>
    public bool ShowFirstRun => !Current.FirstRunCompleted;

    /// <summary>Turns starting with Windows on or off.</summary>
    /// <param name="enabled">True to start with Windows.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <returns>A task that completes when the choice is made or refused.</returns>
    public async Task SetStartWithWindowsAsync(bool enabled, CancellationToken cancellationToken)
    {
        ErrorMessage = null;
        if (_startup is null)
        {
            ErrorMessage = StartupUnavailable;
            Refresh();
            return;
        }

        var result = await _startup.SetAsync(enabled, cancellationToken);
        if (result.IsFailure)
        {
            ErrorMessage = result.Error.Message;
        }

        // The switch shows what is really in place, so a refused change snaps back.
        Refresh();
    }

    [RelayCommand]
    private async Task AcceptFirstRunAsync(CancellationToken cancellationToken)
    {
        ErrorMessage = null;
        if (_startup is null)
        {
            // Not asking again is kinder than asking something that cannot be done.
            ErrorMessage = StartupUnavailable;
        }
        else
        {
            var result = await _startup.SetAsync(true, cancellationToken);
            if (result.IsFailure)
            {
                ErrorMessage = result.Error.Message;
                Refresh();
                return;
            }
        }

        await CompleteFirstRunAsync(cancellationToken);
    }

    [RelayCommand]
    private Task DeclineFirstRunAsync(CancellationToken cancellationToken) => CompleteFirstRunAsync(cancellationToken);

    private async Task CompleteFirstRunAsync(CancellationToken cancellationToken)
    {
        var message = ErrorMessage;
        _ = await ChangeAsync(settings => settings with { FirstRunCompleted = true }, cancellationToken);
        ErrorMessage ??= message;
        Refresh();
    }
}
