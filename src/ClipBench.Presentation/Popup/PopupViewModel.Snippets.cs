using System.ComponentModel;
using ClipBench.Core.Clips;
using ClipBench.Presentation.Boards;
using ClipBench.Presentation.Snippets;
using CommunityToolkit.Mvvm.Input;

namespace ClipBench.Presentation.Popup;

/// <summary>The Snippets view: a list of snippets, and pasting one with its variables filled in.</summary>
public sealed partial class PopupViewModel
{
    /// <summary>Raised when the user asked to edit the snippets. The popup is hidden first.</summary>
    public event EventHandler? EditSnippetsRequested;

    /// <summary>The snippets and the prompt for their values.</summary>
    public SnippetsViewModel Snippets => _snippets;

    /// <summary>True when the list shows snippets.</summary>
    public bool IsSnippetsView => _sidebar.Scope == PopupScope.Snippets;

    /// <summary>True when the list shows clips: the history, the pinned clips or a board.</summary>
    public bool IsClipsView => !IsSnippetsView;

    /// <summary>True while a snippet asks for values.</summary>
    public bool IsPromptingSnippet => _snippets.IsPrompting;

    /// <summary>How many rows the list has now: clips or snippets, whichever is shown.</summary>
    public int ItemCount => _sidebar.Scope == PopupScope.Snippets ? _snippets.Rows.Count : Rows.Count;

    /// <summary>The highlighted snippet, or null when the snippets are not shown.</summary>
    public SnippetRowViewModel? CurrentSnippet =>
        _sidebar.Scope == PopupScope.Snippets && SelectedIndex >= 0 && SelectedIndex < _snippets.Rows.Count ? _snippets.Rows[SelectedIndex] : null;

    [RelayCommand]
    private void EditSnippets()
    {
        // The editor is a window of its own, so the popup goes away before it opens.
        Close();
        EditSnippetsRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private async Task SubmitSnippetPromptAsync(CancellationToken cancellationToken)
    {
        if (_snippets.TrySubmit(out var text))
        {
            await PasteChangedAsync(text, cancellationToken);
        }
    }

    private async Task PasteSnippetAsync(CancellationToken cancellationToken)
    {
        if (CurrentSnippet is not { } row)
        {
            return;
        }

        ErrorMessage = null;
        var start = _snippets.Begin(row.Id, NewestText());
        switch (start.Kind)
        {
            case SnippetStartKind.Ready:
                await PasteChangedAsync(start.Text, cancellationToken);
                break;
            case SnippetStartKind.Failed:
                ErrorMessage = start.Error;
                break;
            default:
                // The prompt is open and asks for the values.
                break;
        }
    }

    // What {clipboard} stands for: the text of the newest clip that has text. A picture or a list of files is skipped.
    private string? NewestText() =>
        _library.Clips.FirstOrDefault(clip => clip.Content is TextContent or HtmlContent or RtfContent)?.Content.PlainText;

    private void RefreshSnippets(bool keepSelection)
    {
        var previousId = CurrentSnippet?.Id;
        var previousIndex = SelectedIndex;

        Rows.Clear();
        _snippets.Refresh(SearchText);

        var count = _snippets.Rows.Count;
        var index = -1;
        if (count > 0)
        {
            index = 0;
            if (keepSelection && previousId is { } id)
            {
                var same = _snippets.Rows.ToList().FindIndex(row => row.Id == id);
                index = same >= 0 ? same : Math.Clamp(previousIndex, 0, count - 1);
            }
        }

        SelectedIndex = index;
        IsConfirmingDelete = false;
        NotifyListChanged();
    }

    private void OnSnippetsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SnippetsViewModel.IsPrompting))
        {
            OnPropertyChanged(nameof(IsPromptingSnippet));
            OnPropertyChanged(nameof(IsMainEnabled));
            OnPropertyChanged(nameof(StatusText));
        }
    }

    // Runs on whatever thread changed the snippets.
    private void OnSnippetsChanged(object? sender, EventArgs e)
    {
        if (_open && !_disposed)
        {
            _dispatcher.Post(() =>
            {
                if (_open && !_disposed && _sidebar.Scope == PopupScope.Snippets)
                {
                    RefreshSnippets(keepSelection: true);
                }
            });
        }
    }
}
