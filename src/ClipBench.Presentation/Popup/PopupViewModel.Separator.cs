using ClipBench.Core.Pasting;
using ClipBench.Core.Settings;
using CommunityToolkit.Mvvm.Input;

namespace ClipBench.Presentation.Popup;

/// <summary>The separator picker: what joins the clips of a multi-paste. The choice is kept in the settings.</summary>
public sealed partial class PopupViewModel
{
    /// <summary>The presets offered in the separator list.</summary>
    public static IReadOnlyList<SeparatorOption> SeparatorOptions => SeparatorOption.Presets;

    /// <summary>The separator in use, in words.</summary>
    public string SeparatorLabel => SeparatorOption.LabelFor(_settings.Current);

    [RelayCommand]
    private Task NextSeparatorAsync(CancellationToken cancellationToken)
    {
        // The custom separator takes its turn only once the user has typed one.
        var current = _settings.Current;
        List<SeparatorChoice> choices = [.. SeparatorOption.Presets.Select(option => option.Choice)];
        if (current.CustomSeparator.Length > 0)
        {
            choices.Add(SeparatorChoice.Custom);
        }

        var next = choices[(choices.IndexOf(current.Separator) + 1) % choices.Count];
        return ChooseSeparatorAsync(next, cancellationToken);
    }

    [RelayCommand]
    private async Task ChooseSeparatorAsync(SeparatorChoice choice, CancellationToken cancellationToken)
    {
        ErrorMessage = null;
        var result = await _settings.UpdateAsync(settings => settings with { Separator = choice }, cancellationToken);
        ReportFailure(result);
    }

    [RelayCommand]
    private async Task SetCustomSeparatorAsync(string typed, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(typed);
        ErrorMessage = null;
        if (typed.Length == 0)
        {
            await ChooseSeparatorAsync(SeparatorChoice.None, cancellationToken);
            return;
        }

        var parsed = Separator.Parse(typed);
        if (parsed.IsFailure)
        {
            ErrorMessage = parsed.Error.Message;
            return;
        }

        var result = await _settings.UpdateAsync(
            settings => settings with { Separator = SeparatorChoice.Custom, CustomSeparator = typed },
            cancellationToken);
        ReportFailure(result);
    }

    // Runs on whatever thread changed the settings.
    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        if (!_disposed)
        {
            _dispatcher.Post(() => OnPropertyChanged(nameof(SeparatorLabel)));
        }
    }
}
