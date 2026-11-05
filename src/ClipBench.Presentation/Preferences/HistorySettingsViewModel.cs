using ClipBench.Core.Settings;
using ClipBench.Presentation.Popup;

namespace ClipBench.Presentation.Preferences;

/// <summary>The History section: how much is kept and for how long. A number outside its range is moved to the nearest end.</summary>
public sealed class HistorySettingsViewModel : SettingsSectionViewModel
{
    /// <summary>Creates the section.</summary>
    /// <param name="settings">The settings in force.</param>
    /// <param name="dispatcher">Moves updates to the UI thread.</param>
    public HistorySettingsViewModel(SettingsService settings, IUiDispatcher dispatcher)
        : base(settings, dispatcher)
    {
    }

    /// <summary>The most unpinned clips kept.</summary>
    public int MaxCount
    {
        get => Current.HistoryMaxCount;
        set => _ = ChangeAsync(settings => settings with { HistoryMaxCount = value }, CancellationToken.None);
    }

    /// <summary>The allowed values of <see cref="MaxCount"/>.</summary>
    public static SettingRange MaxCountRange => AppSettings.HistoryMaxCountRange;

    /// <summary>How many days an unpinned clip is kept.</summary>
    public int MaxAgeDays
    {
        get => Current.HistoryMaxAgeDays;
        set => _ = ChangeAsync(settings => settings with { HistoryMaxAgeDays = value }, CancellationToken.None);
    }

    /// <summary>The allowed values of <see cref="MaxAgeDays"/>.</summary>
    public static SettingRange MaxAgeDaysRange => AppSettings.HistoryMaxAgeDaysRange;

    /// <summary>The most megabytes the unpinned clips may take together.</summary>
    public int MaxMegabytes
    {
        get => Current.HistoryMaxMegabytes;
        set => _ = ChangeAsync(settings => settings with { HistoryMaxMegabytes = value }, CancellationToken.None);
    }

    /// <summary>The allowed values of <see cref="MaxMegabytes"/>.</summary>
    public static SettingRange MaxMegabytesRange => AppSettings.HistoryMaxMegabytesRange;
}
