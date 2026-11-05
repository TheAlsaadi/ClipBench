using ClipBench.Core.Settings;
using ClipBench.Presentation.Popup;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClipBench.Presentation.Preferences;

/// <summary>
/// The part every section of the Settings window shares: it follows the settings, so a change made anywhere (the tray
/// menu, the popup, another section) shows at once, and it keeps the last thing that went wrong in words for the user.
/// </summary>
public abstract class SettingsSectionViewModel : ObservableObject, IDisposable
{
    private int _disposed;

    /// <summary>Starts following the settings.</summary>
    /// <param name="settings">The settings in force.</param>
    /// <param name="dispatcher">Moves updates to the UI thread.</param>
    protected SettingsSectionViewModel(SettingsService settings, IUiDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(dispatcher);
        Settings = settings;
        Dispatcher = dispatcher;
        settings.Changed += OnSettingsChanged;
    }

    /// <summary>What went wrong with the last change, in words for the user, or null.</summary>
    public string? ErrorMessage
    {
        get;
        protected set => SetProperty(ref field, value);
    }

    /// <summary>Moves updates to the UI thread.</summary>
    protected IUiDispatcher Dispatcher { get; }

    /// <summary>The settings service.</summary>
    protected SettingsService Settings { get; }

    /// <summary>The settings in force.</summary>
    protected AppSettings Current => Settings.Current;

    /// <summary>Stops following the settings.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            Settings.Changed -= OnSettingsChanged;
            Release();
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>Lets a section let go of what else it follows. Called once.</summary>
    protected virtual void Release()
    {
    }

    /// <summary>Changes the settings. A change that could not be saved still applies for this run; the reason is shown.</summary>
    /// <param name="change">Makes the new settings from the current ones.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <returns>True when the change was saved.</returns>
    protected async Task<bool> ChangeAsync(Func<AppSettings, AppSettings> change, CancellationToken cancellationToken)
    {
        ErrorMessage = null;
        var result = await Settings.UpdateAsync(change, cancellationToken).ConfigureAwait(true);
        if (result.IsFailure)
        {
            ErrorMessage = result.Error.Message;
            return false;
        }

        return true;
    }

    /// <summary>Tells every binding to read its property again.</summary>
    protected void Refresh() => OnPropertyChanged(string.Empty);

    // Runs on whatever thread changed the settings.
    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        if (Volatile.Read(ref _disposed) == 0)
        {
            Dispatcher.Post(Refresh);
        }
    }
}
