using System.ComponentModel;
using ClipBench.Core.Boards;
using ClipBench.Core.Clips;
using ClipBench.Presentation.Boards;
using CommunityToolkit.Mvvm.Input;

namespace ClipBench.Presentation.Popup;

/// <summary>The sidebar: History, Pinned and the boards, and putting clips on boards.</summary>
public sealed partial class PopupViewModel
{
    /// <summary>The sidebar.</summary>
    public SidebarViewModel Sidebar => _sidebar;

    // The marked clips in the order they were marked, or the highlighted clip when none are marked.
    private List<ClipId> TargetIds() =>
        [.. (_marks.Count > 0 ? MarkedClips() : HighlightedClip()).Select(clip => clip.Id)];

    [RelayCommand]
    private async Task AddToBoardAsync(BoardId board, CancellationToken cancellationToken)
    {
        ErrorMessage = null;
        var ids = TargetIds();
        if (ids.Count == 0)
        {
            return;
        }

        var added = await _sidebar.AddClipsAsync(board, ids, cancellationToken);
        if (added.IsFailure)
        {
            ErrorMessage = added.Error.Message;
            return;
        }

        ClearMarks();
    }

    [RelayCommand]
    private async Task RemoveFromBoardAsync(CancellationToken cancellationToken)
    {
        ErrorMessage = null;
        var ids = TargetIds();
        if (ids.Count == 0)
        {
            return;
        }

        var removed = await _sidebar.RemoveClipsAsync(ids, cancellationToken);
        if (removed.IsFailure)
        {
            ErrorMessage = removed.Error.Message;
            return;
        }

        ClearMarks();
    }

    [RelayCommand]
    private Task MoveUpInBoardAsync(CancellationToken cancellationToken) => MoveInBoardAsync(-1, cancellationToken);

    [RelayCommand]
    private Task MoveDownInBoardAsync(CancellationToken cancellationToken) => MoveInBoardAsync(1, cancellationToken);

    // The order of a board is shown as it is only when nothing is typed: a search ranks the clips instead.
    private async Task MoveInBoardAsync(int step, CancellationToken cancellationToken)
    {
        if (_sidebar.Scope != PopupScope.Board || !string.IsNullOrEmpty(SearchText) || Current is not { } row)
        {
            return;
        }

        var target = SelectedIndex + step;
        if (target < 0 || target >= Rows.Count)
        {
            return;
        }

        ErrorMessage = null;
        var moved = await _sidebar.MoveClipAsync(row.Id, target, cancellationToken);
        if (moved.IsFailure)
        {
            ErrorMessage = moved.Error.Message;
        }
    }

    private void ClearMarks()
    {
        _marks.Clear();
        ShowMarks();
        OnPropertyChanged(nameof(MarkedCount));
        OnPropertyChanged(nameof(PasteLabel));
        OnPropertyChanged(nameof(StatusText));
    }

    // Another entry was chosen, or the shown board is gone: the list is rebuilt from the top.
    private void OnScopeChanged(object? sender, EventArgs e)
    {
        if (_open && !_disposed)
        {
            Refresh(keepSelection: false);
        }
    }

    private void OnSidebarChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SidebarViewModel.IsEditing) or nameof(SidebarViewModel.IsConfirmingDelete))
        {
            OnPropertyChanged(nameof(StatusText));
        }
    }
}
