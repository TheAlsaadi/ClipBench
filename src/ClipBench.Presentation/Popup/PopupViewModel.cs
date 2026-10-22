using System.Collections.ObjectModel;
using ClipBench.Core.Clips;
using ClipBench.Core.History;
using ClipBench.Core.Results;
using ClipBench.Presentation.Rows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClipBench.Presentation.Popup;

/// <summary>Everything the popup shows and does: the search, the list, the selection and the keys.</summary>
public sealed partial class PopupViewModel : ObservableObject, IDisposable
{
    /// <summary>How long typing must pause before the list is searched.</summary>
    public static readonly TimeSpan SearchDelay = TimeSpan.FromMilliseconds(150);

    private const string SaveFailed = "That change could not be saved.";
    private const string Separator = " · ";

    private readonly ClipLibrary _library;
    private readonly IPasteFlow _paste;
    private readonly IUiDispatcher _dispatcher;
    private readonly TimeProvider _timeProvider;
    private readonly ITimer _searchTimer;

    // The marked clips in the order they were marked. Used on the UI thread only.
    private readonly List<ClipId> _marks = [];
    private volatile bool _open;
    private volatile bool _disposed;
    private bool _searchTextIsSetByCode;

    /// <summary>Creates the view model. It does nothing until <see cref="Open"/>.</summary>
    /// <param name="library">The clips.</param>
    /// <param name="paste">Pastes the chosen clip.</param>
    /// <param name="dispatcher">Moves work to the UI thread.</param>
    /// <param name="timeProvider">The clock and timers, for the age shown and the search delay.</param>
    public PopupViewModel(ClipLibrary library, IPasteFlow paste, IUiDispatcher dispatcher, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(paste);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _library = library;
        _paste = paste;
        _dispatcher = dispatcher;
        _timeProvider = timeProvider;
        Rows = [];
        _searchTimer = timeProvider.CreateTimer(OnSearchTimer, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _library.Changed += OnLibraryChanged;
    }

    /// <summary>Raised when the popup should be hidden.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>The rows shown, best match or newest first.</summary>
    public ObservableCollection<ClipRowViewModel> Rows { get; }

    /// <summary>What the user typed in the search box.</summary>
    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    /// <summary>The highlighted row, or -1 when there are none.</summary>
    [ObservableProperty]
    public partial int SelectedIndex { get; set; } = -1;

    /// <summary>True while the popup is shown.</summary>
    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    /// <summary>True after Delete was pressed on a pinned clip, until the user confirms or backs out.</summary>
    [ObservableProperty]
    public partial bool IsConfirmingDelete { get; set; }

    /// <summary>A short message about the last action that did not work, or null.</summary>
    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    /// <summary>True while there is an <see cref="ErrorMessage"/> to show.</summary>
    public bool HasError => ErrorMessage is not null;

    /// <summary>The highlighted row, or null.</summary>
    public ClipRowViewModel? Current => SelectedIndex >= 0 && SelectedIndex < Rows.Count ? Rows[SelectedIndex] : null;

    /// <summary>How many rows are marked for a multi-paste.</summary>
    public int MarkedCount => _marks.Count;

    /// <summary>The marked clips, in the order they were marked, including any the search hides.</summary>
    public IReadOnlyList<ClipId> MarkedIds => [.. _marks];

    /// <summary>True when there is nothing to list.</summary>
    public bool ShowEmptyState => Rows.Count == 0;

    /// <summary>What to say when there is nothing to list.</summary>
    public string EmptyText => string.IsNullOrWhiteSpace(SearchText)
        ? "Nothing copied yet. Copy something and it will show up here."
        : "No clips match your search.";

    /// <summary>The key hints, or the question when a delete needs confirming.</summary>
    public string StatusText
    {
        get
        {
            if (Rows.Count == 0)
            {
                return "Esc close";
            }

            if (IsConfirmingDelete)
            {
                return "Press Delete again to remove this pinned clip" + Separator + "Esc cancels";
            }

            var second = Current is { IsSensitive: true } ? "Ctrl+R reveal" : "Space select";
            return string.Join(Separator, "Enter paste", second, "Ctrl+P pin", "Del delete", "Esc close");
        }
    }

    /// <summary>Shows the popup: remembers where to paste, clears the search and lists the clips.</summary>
    /// <param name="pasteIntoPreviousWindow">
    /// True (the default) to paste into the window that has focus now. False when it is the taskbar, because the popup
    /// was opened from the tray icon: the paste then goes to whatever has focus once the popup is hidden.
    /// </param>
    public void Open(bool pasteIntoPreviousWindow = true)
    {
        // The window that has focus now is where the paste goes. The popup must not have taken focus yet.
        _paste.Begin(pasteIntoPreviousWindow);

        SetSearchTextFromCode(string.Empty);
        IsConfirmingDelete = false;
        ErrorMessage = null;
        _marks.Clear();
        _open = true;
        IsOpen = true;
        Refresh(keepSelection: false);
    }

    /// <summary>Hides the popup and forgets the search, the marks and any question.</summary>
    public void Close()
    {
        if (!_open)
        {
            return;
        }

        _open = false;
        IsOpen = false;
        _ = _searchTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        SetSearchTextFromCode(string.Empty);
        IsConfirmingDelete = false;
        ErrorMessage = null;
        _marks.Clear();
        Rows.Clear();
        SelectedIndex = -1;
        NotifyListChanged();
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Stops watching the library.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _library.Changed -= OnLibraryChanged;
        _searchTimer.Dispose();
    }

    partial void OnSearchTextChanged(string value)
    {
        if (_searchTextIsSetByCode || !_open)
        {
            return;
        }

        ErrorMessage = null;
        _ = _searchTimer.Change(SearchDelay, Timeout.InfiniteTimeSpan);
    }

    partial void OnSelectedIndexChanged(int value)
    {
        IsConfirmingDelete = false;
        OnPropertyChanged(nameof(Current));
        OnPropertyChanged(nameof(StatusText));
    }

    partial void OnIsConfirmingDeleteChanged(bool value) => OnPropertyChanged(nameof(StatusText));

    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(HasError));

    [RelayCommand]
    private void MoveDown()
    {
        ErrorMessage = null;
        IsConfirmingDelete = false;
        if (Rows.Count > 0)
        {
            SelectedIndex = SelectedIndex < 0 ? 0 : (SelectedIndex + 1) % Rows.Count;
        }
    }

    [RelayCommand]
    private void MoveUp()
    {
        ErrorMessage = null;
        IsConfirmingDelete = false;
        if (Rows.Count > 0)
        {
            SelectedIndex = SelectedIndex <= 0 ? Rows.Count - 1 : SelectedIndex - 1;
        }
    }

    [RelayCommand]
    private void ToggleMark()
    {
        if (Current is { } row)
        {
            if (!_marks.Remove(row.Id))
            {
                _marks.Add(row.Id);
            }

            ShowMarks();
            OnPropertyChanged(nameof(MarkedCount));
            OnPropertyChanged(nameof(StatusText));
        }
    }

    // Each row shows whether it is marked and where it comes in the paste order.
    private void ShowMarks()
    {
        foreach (var row in Rows)
        {
            var place = _marks.IndexOf(row.Id) + 1;
            row.IsMarked = place > 0;
            row.MarkNumber = place;
        }
    }

    [RelayCommand]
    private void Reveal()
    {
        if (Current is { IsSensitive: true } row)
        {
            row.IsRevealed = !row.IsRevealed;
            OnPropertyChanged(nameof(StatusText));
        }
    }

    [RelayCommand]
    private void Escape()
    {
        if (IsConfirmingDelete)
        {
            IsConfirmingDelete = false;
            return;
        }

        Close();
    }

    [RelayCommand]
    private void CloseNow() => Close();

    [RelayCommand]
    private async Task PasteAsync(CancellationToken cancellationToken)
    {
        if (Current is not { } row)
        {
            return;
        }

        var clip = _library.Clips.FirstOrDefault(candidate => candidate.Id == row.Id);
        if (clip is null)
        {
            Refresh(keepSelection: true);
            return;
        }

        // Hide first: the paste needs the window underneath to have focus again.
        Close();
        await _paste.PasteAsync(clip, cancellationToken);
    }

    [RelayCommand]
    private async Task TogglePinAsync(CancellationToken cancellationToken)
    {
        ErrorMessage = null;
        if (Current is not { } row)
        {
            return;
        }

        var result = await _library.SetPinnedAsync(row.Id, !row.IsPinned, cancellationToken);
        ReportFailure(result);
    }

    [RelayCommand]
    private async Task DeleteAsync(CancellationToken cancellationToken)
    {
        ErrorMessage = null;
        if (Current is not { } row)
        {
            return;
        }

        if (row.IsPinned && !IsConfirmingDelete)
        {
            IsConfirmingDelete = true;
            return;
        }

        IsConfirmingDelete = false;
        var result = await _library.RemoveAsync(row.Id, cancellationToken);
        ReportFailure(result);
    }

    private void ReportFailure(Result result)
    {
        // A clip that is already gone is not something to report: the list is refreshed and shows it.
        if (result.IsFailure && result.Error.Code != ErrorCode.ClipNotFound)
        {
            ErrorMessage = SaveFailed;
        }
    }

    private void SetSearchTextFromCode(string text)
    {
        _searchTextIsSetByCode = true;
        try
        {
            SearchText = text;
        }
        finally
        {
            _searchTextIsSetByCode = false;
        }
    }

    // Runs on a timer thread.
    private void OnSearchTimer(object? state)
    {
        if (!_disposed)
        {
            _dispatcher.Post(() =>
            {
                if (_open && !_disposed)
                {
                    Refresh(keepSelection: false);
                }
            });
        }
    }

    // Runs on whatever thread changed the library.
    private void OnLibraryChanged(object? sender, EventArgs e)
    {
        if (_open && !_disposed)
        {
            _dispatcher.Post(() =>
            {
                if (_open && !_disposed)
                {
                    Refresh(keepSelection: true);
                }
            });
        }
    }

    private void Refresh(bool keepSelection)
    {
        var previousId = Current?.Id;
        var previousIndex = SelectedIndex;
        var wasConfirming = IsConfirmingDelete;
        var now = _timeProvider.GetUtcNow();

        // A search only hides marked clips, but a clip that left the history leaves the marks.
        var present = _library.Clips.Select(clip => clip.Id).ToHashSet();
        _ = _marks.RemoveAll(id => !present.Contains(id));

        var rows = _library.Search(SearchText)
            .Select(result => new ClipRowViewModel(result.Clip, _library.GetSensitivity(result.Clip), now))
            .ToList();

        Rows.Clear();
        foreach (var row in rows)
        {
            Rows.Add(row);
        }

        ShowMarks();

        var index = -1;
        if (rows.Count > 0)
        {
            index = 0;
            if (keepSelection && previousId is { } id)
            {
                var same = rows.FindIndex(row => row.Id == id);
                index = same >= 0 ? same : Math.Min(Math.Max(previousIndex, 0), rows.Count - 1);
            }
        }

        SelectedIndex = index;

        // Moving the highlight withdraws the question, but a refresh is not the user moving: when the same clip is
        // still highlighted, a clip copied meanwhile (or the list pushing -1 through the binding) must not withdraw it.
        IsConfirmingDelete = wasConfirming && keepSelection && previousId is { } kept && Current?.Id == kept;
        NotifyListChanged();
    }

    private void NotifyListChanged()
    {
        OnPropertyChanged(nameof(Current));
        OnPropertyChanged(nameof(MarkedCount));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(EmptyText));
        OnPropertyChanged(nameof(StatusText));
    }
}
