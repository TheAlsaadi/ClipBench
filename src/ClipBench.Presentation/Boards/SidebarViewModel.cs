using System.Collections.ObjectModel;
using ClipBench.Core.Boards;
using ClipBench.Core.Clips;
using ClipBench.Core.History;
using ClipBench.Core.Results;
using ClipBench.Core.Snippets;
using ClipBench.Presentation.Popup;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClipBench.Presentation.Boards;

/// <summary>
/// The popup's sidebar: History, Pinned, the boards and Snippets, which one is shown, and making, renaming and deleting boards.
/// It tells the popup when the list it shows must be rebuilt. Used on the UI thread only.
/// </summary>
public sealed partial class SidebarViewModel : ObservableObject, IDisposable
{
    private readonly BoardCatalog _boards;
    private readonly SnippetCatalog _snippets;
    private readonly ClipLibrary _library;
    private readonly IUiDispatcher _dispatcher;
    private volatile bool _disposed;
    private bool _quiet;

    /// <summary>Creates the sidebar and starts following the boards and the clips.</summary>
    /// <param name="boards">The boards.</param>
    /// <param name="snippets">The snippets, for the count.</param>
    /// <param name="library">The clips, for the counts.</param>
    /// <param name="dispatcher">Moves updates to the UI thread.</param>
    public SidebarViewModel(BoardCatalog boards, SnippetCatalog snippets, ClipLibrary library, IUiDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(boards);
        ArgumentNullException.ThrowIfNull(snippets);
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(dispatcher);
        _boards = boards;
        _snippets = snippets;
        _library = library;
        _dispatcher = dispatcher;
        Entries = [];
        Rebuild();
        _library.Changed += OnSourceChanged;
        _boards.Changed += OnSourceChanged;
        _snippets.Changed += OnSourceChanged;
    }

    /// <summary>Raised when the list the popup shows must be rebuilt: another entry was chosen, or the chosen board is gone.</summary>
    public event EventHandler? ScopeChanged;

    /// <summary>History, Pinned, the boards and then Snippets.</summary>
    public ObservableCollection<SidebarEntryViewModel> Entries { get; }

    /// <summary>True while the sidebar is shown.</summary>
    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    /// <summary>The chosen entry.</summary>
    [ObservableProperty]
    public partial int SelectedIndex { get; set; }

    /// <summary>True while the name box is shown, for a new board or a rename.</summary>
    [ObservableProperty]
    public partial bool IsEditing { get; set; }

    /// <summary>True when the name box renames the chosen board instead of making a new one.</summary>
    [ObservableProperty]
    public partial bool IsRenaming { get; set; }

    /// <summary>What is typed in the name box.</summary>
    [ObservableProperty]
    public partial string EditText { get; set; } = string.Empty;

    /// <summary>Why the name was not accepted, or null.</summary>
    [ObservableProperty]
    public partial string? EditError { get; set; }

    /// <summary>True after the delete key was pressed on a board, until the user confirms or backs out.</summary>
    [ObservableProperty]
    public partial bool IsConfirmingDelete { get; set; }

    /// <summary>The chosen entry.</summary>
    public SidebarEntryViewModel Current => Entries[SelectedIndex >= 0 && SelectedIndex < Entries.Count ? SelectedIndex : 0];

    /// <summary>What the list shows.</summary>
    public PopupScope Scope => Current.Scope;

    /// <summary>The chosen board, or null when History or Pinned is chosen.</summary>
    public BoardId? CurrentBoard => Current.Board;

    /// <summary>The board entries only.</summary>
    public IReadOnlyList<SidebarEntryViewModel> BoardEntries => [.. Entries.Where(entry => entry.IsBoard)];

    /// <summary>The question that asks to confirm a delete.</summary>
    public string DeleteQuestion => $"Delete the board \"{Current.Name}\"? Its clips stay pinned. Delete again to confirm · Esc cancels";

    /// <summary>Goes back to History, forgets any typing or question, and refreshes the counts. The sidebar stays open or closed as it was.</summary>
    public void Reset()
    {
        IsEditing = false;
        IsRenaming = false;
        EditText = string.Empty;
        EditError = null;
        IsConfirmingDelete = false;
        Rebuild();
        if (SelectedIndex != 0)
        {
            ChooseQuietly(0);
        }
    }

    /// <summary>Rebuilds the entries from the boards and the clips, keeping the chosen entry when it still exists.</summary>
    public void Rebuild()
    {
        // One pass over the clips gives every count. This runs for every clip that is captured, so it must not sort each board.
        var clips = _library.Clips;
        var pinned = 0;
        var onBoard = new Dictionary<BoardId, int>();
        foreach (var clip in clips)
        {
            if (clip.IsPinned)
            {
                pinned++;
            }

            if (clip.OnBoard is { } board)
            {
                onBoard[board] = onBoard.GetValueOrDefault(board) + 1;
            }
        }

        List<SidebarEntryViewModel> wanted =
        [
            new(PopupScope.History, null, "History", clips.Length),
            new(PopupScope.Pinned, null, "Pinned", pinned),
            .. _boards.Boards.Select(board => new SidebarEntryViewModel(PopupScope.Board, board.Id, board.Name, onBoard.GetValueOrDefault(board.Id))),
            new(PopupScope.Snippets, null, "Snippets", _snippets.Snippets.Length),
        ];

        var chosen = Entries.Count > 0 ? Current : null;
        if (SameEntries(wanted))
        {
            for (var i = 0; i < wanted.Count; i++)
            {
                Entries[i].Count = wanted[i].Count;
            }

            return;
        }

        var index = chosen is null ? 0 : wanted.FindIndex(entry => entry.Scope == chosen.Scope && entry.Board == chosen.Board);
        var lostChoice = chosen is not null && index < 0;

        _quiet = true;
        try
        {
            Entries.Clear();
            foreach (var entry in wanted)
            {
                Entries.Add(entry);
            }

            SelectedIndex = Math.Max(index, 0);
        }
        finally
        {
            _quiet = false;
        }

        IsConfirmingDelete = IsConfirmingDelete && !lostChoice;
        NotifyCurrentChanged();
        if (lostChoice)
        {
            RaiseScopeChanged();
        }
    }

    /// <summary>Backs out of the name box or the delete question.</summary>
    /// <returns>True when there was something to back out of.</returns>
    public bool Cancel()
    {
        if (IsEditing)
        {
            CancelEdit();
            return true;
        }

        if (IsConfirmingDelete)
        {
            IsConfirmingDelete = false;
            return true;
        }

        return false;
    }

    /// <summary>Puts clips on a board, in the order given. It stops at the first failure.</summary>
    /// <param name="board">The board.</param>
    /// <param name="clips">The clips.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Success or the first failure.</returns>
    public async Task<Result> AddClipsAsync(BoardId board, IReadOnlyList<ClipId> clips, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(clips);

        foreach (var clip in clips)
        {
            var added = await _boards.AddClipAsync(board, clip, cancellationToken);
            if (added.IsFailure)
            {
                return added;
            }
        }

        return Result.Success();
    }

    /// <summary>Takes clips off their boards. They stay pinned.</summary>
    /// <param name="clips">The clips.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Success or the first failure.</returns>
    public async Task<Result> RemoveClipsAsync(IReadOnlyList<ClipId> clips, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(clips);

        foreach (var clip in clips)
        {
            var removed = await _boards.RemoveClipAsync(clip, cancellationToken);
            if (removed.IsFailure)
            {
                return removed;
            }
        }

        return Result.Success();
    }

    /// <summary>Moves a clip to a place in its board.</summary>
    /// <param name="clip">The clip.</param>
    /// <param name="newIndex">Where it should come, counting from 0.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Success or why it could not be moved.</returns>
    public Task<Result> MoveClipAsync(ClipId clip, int newIndex, CancellationToken cancellationToken) =>
        _boards.MoveClipAsync(clip, newIndex, cancellationToken);

    /// <summary>Stops following the boards and the clips.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _library.Changed -= OnSourceChanged;
        _boards.Changed -= OnSourceChanged;
        _snippets.Changed -= OnSourceChanged;
    }

    partial void OnSelectedIndexChanged(int value)
    {
        if (_quiet || value < 0 || value >= Entries.Count)
        {
            return;
        }

        IsConfirmingDelete = false;
        NotifyCurrentChanged();
        RaiseScopeChanged();
    }

    partial void OnIsConfirmingDeleteChanged(bool value) => OnPropertyChanged(nameof(DeleteQuestion));

    [RelayCommand]
    private void Toggle() => IsOpen = !IsOpen;

    [RelayCommand]
    private void SelectEntry(int index)
    {
        if (index >= 0 && index < Entries.Count && index != SelectedIndex)
        {
            SelectedIndex = index;
        }
    }

    [RelayCommand]
    private void Next() => SelectedIndex = (SelectedIndex + 1) % Entries.Count;

    [RelayCommand]
    private void Previous() => SelectedIndex = (SelectedIndex + Entries.Count - 1) % Entries.Count;

    [RelayCommand]
    private void StartNew()
    {
        IsConfirmingDelete = false;
        IsRenaming = false;
        EditText = string.Empty;
        EditError = null;
        IsEditing = true;
    }

    [RelayCommand]
    private void StartRename()
    {
        if (!Current.IsBoard)
        {
            return;
        }

        IsConfirmingDelete = false;
        IsRenaming = true;
        EditText = Current.Name;
        EditError = null;
        IsEditing = true;
    }

    [RelayCommand]
    private async Task SubmitEditAsync(CancellationToken cancellationToken)
    {
        if (!IsEditing)
        {
            return;
        }

        EditError = null;
        Result outcome;
        BoardId? made = null;
        if (IsRenaming && Current.Board is { } renamed)
        {
            outcome = await _boards.RenameAsync(renamed, EditText, cancellationToken);
        }
        else
        {
            var created = await _boards.CreateAsync(EditText, cancellationToken);
            outcome = created.IsSuccess ? Result.Success() : Result.Failure(created.Error);
            made = created.IsSuccess ? created.Value.Id : null;
        }

        // The list may not have heard of the change yet: it is rebuilt now, so a new board can be chosen.
        Rebuild();
        if (outcome.IsFailure)
        {
            EditError = outcome.Error.Message;
            return;
        }

        IsEditing = false;
        IsRenaming = false;
        EditText = string.Empty;
        if (made is { } id)
        {
            SelectEntry(Entries.ToList().FindIndex(entry => entry.Board == id));
        }
    }

    [RelayCommand]
    private void CancelEdit()
    {
        IsEditing = false;
        IsRenaming = false;
        EditText = string.Empty;
        EditError = null;
    }

    [RelayCommand]
    private async Task DeleteBoardAsync(CancellationToken cancellationToken)
    {
        if (Current.Board is not { } board)
        {
            return;
        }

        if (!IsConfirmingDelete)
        {
            IsConfirmingDelete = true;
            return;
        }

        IsConfirmingDelete = false;

        // A store that fails still leaves the board off the list, as everywhere else: the list is rebuilt either way.
        _ = await _boards.DeleteAsync(board, cancellationToken);
        Rebuild();
    }

    // Runs on whatever thread changed the boards or the clips.
    private void OnSourceChanged(object? sender, EventArgs e)
    {
        if (!_disposed)
        {
            _dispatcher.Post(() =>
            {
                if (!_disposed)
                {
                    Rebuild();
                }
            });
        }
    }

    private bool SameEntries(List<SidebarEntryViewModel> wanted)
    {
        if (Entries.Count != wanted.Count)
        {
            return false;
        }

        for (var i = 0; i < wanted.Count; i++)
        {
            if (Entries[i].Scope != wanted[i].Scope || Entries[i].Board != wanted[i].Board || Entries[i].Name != wanted[i].Name)
            {
                return false;
            }
        }

        return true;
    }

    private void ChooseQuietly(int index)
    {
        _quiet = true;
        try
        {
            SelectedIndex = index;
        }
        finally
        {
            _quiet = false;
        }

        NotifyCurrentChanged();
    }

    private void NotifyCurrentChanged()
    {
        OnPropertyChanged(nameof(Current));
        OnPropertyChanged(nameof(Scope));
        OnPropertyChanged(nameof(CurrentBoard));
        OnPropertyChanged(nameof(BoardEntries));
        OnPropertyChanged(nameof(DeleteQuestion));
    }

    private void RaiseScopeChanged() => ScopeChanged?.Invoke(this, EventArgs.Empty);
}
