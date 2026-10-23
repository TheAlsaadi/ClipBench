using ClipBench.Core.Clips;
using ClipBench.Core.Pasting;
using CommunityToolkit.Mvvm.Input;

namespace ClipBench.Presentation.Popup;

/// <summary>Pasting the marked clips together.</summary>
public sealed partial class PopupViewModel
{
    private readonly PasteComposer _composer = new();

    private string MarkedHints => string.Join(HintGap, $"Enter paste {MarkedCount}", $"Ctrl+S {SeparatorLabel}", "Ctrl+Q queue", "Ctrl+T table", "Esc close");

    // The marked clips that are still in the history, in the order they were marked.
    private List<Clip> MarkedClips()
    {
        var byId = _library.Clips.ToDictionary(clip => clip.Id);
        return [.. _marks.Where(byId.ContainsKey).Select(id => byId[id])];
    }

    private async Task PasteMarkedAsync(CancellationToken cancellationToken)
    {
        var clips = MarkedClips();
        if (clips.Count == 0)
        {
            Refresh(keepSelection: true);
            return;
        }

        // One clip is pasted as it is, so rich text and pictures keep their formats.
        if (clips.Count == 1)
        {
            Dismiss();
            await _paste.PasteAsync(clips[0], cancellationToken);
            return;
        }

        var joined = _composer.ComposeBatch(clips, _settings.Current.ResolveSeparator());
        if (joined.IsFailure)
        {
            ErrorMessage = joined.Error.Message;
            return;
        }

        // Hide first: the paste needs the window underneath to have focus again.
        Dismiss();
        await _paste.PasteContentAsync(new TextContent(joined.Value), cancellationToken);
    }

    [RelayCommand]
    private async Task PasteTableAsync(TableLayout layout, CancellationToken cancellationToken)
    {
        var clips = _marks.Count > 0 ? MarkedClips() : HighlightedClip();
        if (clips.Count == 0)
        {
            return;
        }

        var table = _composer.ComposeTable(clips, layout);
        if (table.IsFailure)
        {
            ErrorMessage = table.Error.Message;
            return;
        }

        Dismiss();
        await _paste.PasteContentAsync(new TextContent(table.Value), cancellationToken);
    }

    private List<Clip> HighlightedClip() =>
        Current is { } row && _library.Clips.FirstOrDefault(clip => clip.Id == row.Id) is { } highlighted ? [highlighted] : [];

    [RelayCommand]
    private async Task QueueAsync(CancellationToken cancellationToken)
    {
        var clips = _marks.Count > 0 ? MarkedClips() : HighlightedClip();
        if (clips.Count == 0)
        {
            return;
        }

        ErrorMessage = null;
        var started = await _queue.StartAsync(clips, cancellationToken);
        if (started.IsFailure)
        {
            ErrorMessage = started.Error.Message;
            return;
        }

        // The queue pastes into whatever has focus once the popup is gone.
        Dismiss(leaveFocus: true);
    }

    /// <summary>The label of the paste button: how many clips Enter pastes.</summary>
    public string PasteLabel => MarkedCount > 1 ? $"Paste {MarkedCount}" : "Paste";

    /// <summary>The label of the collect button: what choosing it does.</summary>
    public string CollectLabel => IsCollecting ? $"Finish ({_collect.Collect.Count})" : "Collect";

    /// <summary>True while a collect session runs.</summary>
    public bool IsCollecting => _collect.Collect.IsActive;

    [RelayCommand]
    private async Task ToggleCollectAsync(CancellationToken cancellationToken)
    {
        // Starting sends the user off to copy, so the popup goes away. Finishing keeps it open: the collected clip
        // arrives at the top of the list, ready to paste.
        var starting = !_collect.Collect.IsActive;
        await _collect.ToggleAsync(cancellationToken);
        if (starting)
        {
            Dismiss(leaveFocus: true);
        }
    }

    // Runs on whatever thread collected a copy or started or ended the session.
    private void OnCollectChanged(object? sender, EventArgs e)
    {
        if (!_disposed)
        {
            _dispatcher.Post(() =>
            {
                OnPropertyChanged(nameof(IsCollecting));
                OnPropertyChanged(nameof(CollectLabel));
                OnPropertyChanged(nameof(StatusText));
            });
        }
    }
}
