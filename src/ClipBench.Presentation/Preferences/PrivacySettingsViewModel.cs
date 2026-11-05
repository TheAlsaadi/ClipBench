using System.Collections.Immutable;
using ClipBench.Core.Capture;
using ClipBench.Core.History;
using ClipBench.Core.Privacy;
using ClipBench.Core.Settings;
using ClipBench.Presentation.Popup;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClipBench.Presentation.Preferences;

/// <summary>
/// The Privacy section: how long secrets are kept and whether they are hidden, whether the windows stay out of screen
/// captures, incognito, the programs that are never recorded, and wiping everything after a confirmation.
/// </summary>
public sealed partial class PrivacySettingsViewModel : SettingsSectionViewModel
{
    /// <summary>What is shown after a wipe that worked.</summary>
    public const string WipeDone = "Everything was deleted: every clip, board and snippet.";

    private readonly ICaptureControl _capture;
    private readonly IWipeAll _wipe;

    /// <summary>Creates the section.</summary>
    /// <param name="settings">The settings in force.</param>
    /// <param name="capture">Turns incognito on and off.</param>
    /// <param name="wipe">Deletes everything.</param>
    /// <param name="dispatcher">Moves updates to the UI thread.</param>
    public PrivacySettingsViewModel(SettingsService settings, ICaptureControl capture, IWipeAll wipe, IUiDispatcher dispatcher)
        : base(settings, dispatcher)
    {
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(wipe);
        _capture = capture;
        _wipe = wipe;
        _capture.PausedChanged += OnPausedChanged;
    }

    /// <summary>The allowed values of <see cref="SensitiveExpirySeconds"/>.</summary>
    public static SettingRange SensitiveExpirySecondsRange => AppSettings.SensitiveExpirySecondsRange;

    /// <summary>How many seconds a clip that looks like a secret is kept.</summary>
    public int SensitiveExpirySeconds
    {
        get => Current.SensitiveExpirySeconds;
        set => _ = ChangeAsync(settings => settings with { SensitiveExpirySeconds = value }, CancellationToken.None);
    }

    /// <summary>True to hide clips that look like secrets until they are revealed.</summary>
    public bool MaskSensitive
    {
        get => Current.MaskSensitive;
        set => _ = ChangeAsync(settings => settings with { MaskSensitive = value }, CancellationToken.None);
    }

    /// <summary>True to keep ClipBench's windows out of screenshots and screen recordings.</summary>
    public bool HideFromScreenCapture
    {
        get => Current.HideFromScreenCapture;
        set => _ = ChangeAsync(settings => settings with { HideFromScreenCapture = value }, CancellationToken.None);
    }

    /// <summary>True while incognito is on and nothing is recorded. It is not kept between runs.</summary>
    public bool IsIncognito
    {
        get => _capture.IsPaused;
        set => _capture.SetPaused(value);
    }

    /// <summary>The programs that are never recorded, in the order they were added.</summary>
    public ImmutableArray<string> ExcludedPrograms => Current.ExcludedPrograms.Names;

    /// <summary>What the user typed to add to the list.</summary>
    [ObservableProperty]
    public partial string NewProgram { get; set; } = string.Empty;

    /// <summary>True while the section asks the user to confirm the wipe.</summary>
    [ObservableProperty]
    public partial bool IsConfirmingWipe { get; set; }

    /// <summary>True while the wipe runs.</summary>
    [ObservableProperty]
    public partial bool IsWiping { get; set; }

    /// <summary>What the last wipe did, in words, or null before one was asked for.</summary>
    [ObservableProperty]
    public partial string? WipeStatus { get; set; }

    /// <inheritdoc/>
    protected override void Release() => _capture.PausedChanged -= OnPausedChanged;

    [RelayCommand]
    private async Task AddProgramAsync(CancellationToken cancellationToken)
    {
        ErrorMessage = null;
        var added = Current.ExcludedPrograms.Add(NewProgram);
        if (added.IsFailure)
        {
            ErrorMessage = added.Error.Message;
            return;
        }

        var list = added.Value;
        NewProgram = string.Empty;
        _ = await ChangeAsync(settings => settings with { ExcludedPrograms = list }, cancellationToken);
    }

    [RelayCommand]
    private async Task RemoveProgramAsync(string program, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(program);
        _ = await ChangeAsync(settings => settings with { ExcludedPrograms = settings.ExcludedPrograms.Remove(program) }, cancellationToken);
    }

    [RelayCommand]
    private async Task RestoreDefaultExclusionsAsync(CancellationToken cancellationToken) =>
        _ = await ChangeAsync(settings => settings with { ExcludedPrograms = ExclusionList.Default }, cancellationToken);

    [RelayCommand]
    private void RequestWipe()
    {
        if (IsWiping)
        {
            return;
        }

        WipeStatus = null;
        IsConfirmingWipe = true;
    }

    [RelayCommand]
    private void CancelWipe() => IsConfirmingWipe = false;

    [RelayCommand]
    private async Task ConfirmWipeAsync(CancellationToken cancellationToken)
    {
        if (!IsConfirmingWipe || IsWiping)
        {
            return;
        }

        IsConfirmingWipe = false;
        IsWiping = true;
        try
        {
            var result = await _wipe.WipeAllAsync(cancellationToken);
            WipeStatus = result.IsSuccess ? WipeDone : result.Error.Message;
        }
        finally
        {
            IsWiping = false;
        }
    }

    // Runs on whatever thread turned incognito on or off, for example the tray menu.
    private void OnPausedChanged(object? sender, EventArgs e) => Dispatcher.Post(Refresh);
}
