using System.ComponentModel;
using ClipBench.Core.Clips;
using ClipBench.Core.Privacy;
using ClipBench.Presentation.Transforms;
using CommunityToolkit.Mvvm.Input;

namespace ClipBench.Presentation.Popup;

/// <summary>Shift+Enter: changing the text on its way out.</summary>
public sealed partial class PopupViewModel
{
    /// <summary>The transform picker.</summary>
    public TransformPickerViewModel Transforms => _transforms;

    /// <summary>True while the transform picker is shown over the list.</summary>
    public bool IsTransformsOpen => _transforms.IsOpen;

    // What would be pasted without a transform is what the picker works on: the marked clips joined, or the highlighted clip.
    [RelayCommand]
    private void OpenTransforms()
    {
        ErrorMessage = null;
        var clips = _marks.Count > 0 ? MarkedClips() : HighlightedClip();
        if (clips.Count == 0)
        {
            return;
        }

        string text;
        if (clips.Count == 1)
        {
            if (clips[0].Content.PlainText is not { } single)
            {
                ErrorMessage = "An image has no text to change. Choose a text clip.";
                return;
            }

            text = single;
        }
        else
        {
            var joined = _composer.ComposeBatch(clips, _settings.Current.ResolveSeparator());
            if (joined.IsFailure)
            {
                ErrorMessage = joined.Error.Message;
                return;
            }

            text = joined.Value;
        }

        _transforms.Open(text, clips.Any(clip => _library.GetSensitivity(clip) != SensitiveKind.None));
    }

    [RelayCommand]
    private async Task ConfirmTransformAsync(CancellationToken cancellationToken)
    {
        if (_transforms.TryConfirm(out var text))
        {
            await PasteChangedAsync(text, cancellationToken);
        }
    }

    [RelayCommand]
    private async Task SubmitFindReplaceAsync(CancellationToken cancellationToken)
    {
        if (_transforms.TrySubmitFindReplace(out var text))
        {
            await PasteChangedAsync(text, cancellationToken);
        }
    }

    private async Task PasteChangedAsync(string text, CancellationToken cancellationToken)
    {
        // Hide first: the paste needs the window underneath to have focus again.
        Close();
        await _paste.PasteContentAsync(new TextContent(text), cancellationToken);
    }

    // Runs on the thread that changed the picker, which is the UI thread: the picker is only driven from the popup.
    private void OnTransformsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TransformPickerViewModel.IsOpen))
        {
            OnPropertyChanged(nameof(IsTransformsOpen));
            OnPropertyChanged(nameof(StatusText));
        }
        else if (e.PropertyName == nameof(TransformPickerViewModel.StatusText))
        {
            OnPropertyChanged(nameof(StatusText));
        }
    }
}
