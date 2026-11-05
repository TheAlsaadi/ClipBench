using System.Collections.Immutable;
using ClipBench.Presentation.Snippets;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClipBench.Presentation.Preferences;

/// <summary>The sections of the Settings window, in the order they are listed.</summary>
public enum SettingsSection
{
    /// <summary>Starting with Windows and the first-run question.</summary>
    General,

    /// <summary>The global shortcuts.</summary>
    Hotkeys,

    /// <summary>How much history is kept and for how long.</summary>
    History,

    /// <summary>Secrets, screen capture, incognito, excluded programs and wiping.</summary>
    Privacy,

    /// <summary>What happens when something is pasted.</summary>
    Paste,

    /// <summary>The snippet editor.</summary>
    Snippets,

    /// <summary>The version, license and the promise not to use the network.</summary>
    About,
}

/// <summary>One entry of the list of sections.</summary>
/// <param name="Section">The section.</param>
/// <param name="Title">What the list shows.</param>
public sealed record SettingsSectionItem(SettingsSection Section, string Title);

/// <summary>The Settings window: the list of sections, the one that is open, and each section's view model.</summary>
public sealed partial class SettingsViewModel : ObservableObject, IDisposable
{
    private int _disposed;

    /// <summary>Creates the window's view model. It owns the five settings sections but not the snippet editor or the about text.</summary>
    /// <param name="general">The General section.</param>
    /// <param name="hotkeys">The Shortcuts section.</param>
    /// <param name="history">The History section.</param>
    /// <param name="privacy">The Privacy section.</param>
    /// <param name="paste">The Paste section.</param>
    /// <param name="snippets">The snippet editor.</param>
    /// <param name="about">The About section.</param>
    public SettingsViewModel(
        GeneralSettingsViewModel general,
        HotkeysSettingsViewModel hotkeys,
        HistorySettingsViewModel history,
        PrivacySettingsViewModel privacy,
        PasteSettingsViewModel paste,
        SnippetEditorViewModel snippets,
        AboutViewModel about)
    {
        ArgumentNullException.ThrowIfNull(general);
        ArgumentNullException.ThrowIfNull(hotkeys);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(privacy);
        ArgumentNullException.ThrowIfNull(paste);
        ArgumentNullException.ThrowIfNull(snippets);
        ArgumentNullException.ThrowIfNull(about);
        General = general;
        Hotkeys = hotkeys;
        History = history;
        Privacy = privacy;
        Paste = paste;
        Snippets = snippets;
        About = about;
    }

    /// <summary>The list of sections, in order.</summary>
    public static ImmutableArray<SettingsSectionItem> Sections { get; } =
    [
        new(SettingsSection.General, "General"),
        new(SettingsSection.Hotkeys, "Shortcuts"),
        new(SettingsSection.History, "History"),
        new(SettingsSection.Privacy, "Privacy"),
        new(SettingsSection.Paste, "Paste"),
        new(SettingsSection.Snippets, "Snippets"),
        new(SettingsSection.About, "About"),
    ];

    /// <summary>The General section.</summary>
    public GeneralSettingsViewModel General { get; }

    /// <summary>The Shortcuts section.</summary>
    public HotkeysSettingsViewModel Hotkeys { get; }

    /// <summary>The History section.</summary>
    public HistorySettingsViewModel History { get; }

    /// <summary>The Privacy section.</summary>
    public PrivacySettingsViewModel Privacy { get; }

    /// <summary>The Paste section.</summary>
    public PasteSettingsViewModel Paste { get; }

    /// <summary>The snippet editor.</summary>
    public SnippetEditorViewModel Snippets { get; }

    /// <summary>The About section.</summary>
    public AboutViewModel About { get; }

    /// <summary>The section that is open.</summary>
    [ObservableProperty]
    public partial SettingsSection SelectedSection { get; set; }

    /// <summary>Tells whether a section is the open one.</summary>
    /// <param name="section">The section.</param>
    /// <returns>True when it is open.</returns>
    public bool IsShown(SettingsSection section) => SelectedSection == section;

    /// <summary>Opens a section.</summary>
    /// <param name="section">The section.</param>
    public void Show(SettingsSection section)
    {
        if (!Enum.IsDefined(section))
        {
            throw new ArgumentOutOfRangeException(nameof(section), section, "Not a section of the Settings window.");
        }

        SelectedSection = section;
    }

    /// <summary>Stops the five sections following the settings.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            General.Dispose();
            Hotkeys.Dispose();
            History.Dispose();
            Privacy.Dispose();
            Paste.Dispose();
        }
    }
}
