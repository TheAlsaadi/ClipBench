using ClipBench.Core.Clips;
using ClipBench.Core.Pasting;
using CommunityToolkit.Mvvm.Input;

namespace ClipBench.Presentation.Popup;

/// <summary>Pasting the marked clips together.</summary>
public sealed partial class PopupViewModel
{
    private readonly PasteComposer _composer = new();

    private string MarkedHints => string.Join(HintGap, $"Enter paste {MarkedCount}", $"Ctrl+S {SeparatorLabel}", "Ctrl+T table", "Esc close");

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
            Close();
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
        Close();
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

        Close();
        await _paste.PasteContentAsync(new TextContent(table.Value), cancellationToken);
    }

    private List<Clip> HighlightedClip() =>
        Current is { } row && _library.Clips.FirstOrDefault(clip => clip.Id == row.Id) is { } highlighted ? [highlighted] : [];
}
