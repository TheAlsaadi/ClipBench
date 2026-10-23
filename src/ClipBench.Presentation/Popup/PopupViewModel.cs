using System.Collections.ObjectModel;
using ClipBench.Core.Clips;
using ClipBench.Core.History;
using ClipBench.Core.Pasting;
using ClipBench.Core.Results;
using ClipBench.Core.Settings;
using ClipBench.Presentation.Boards;
using ClipBench.Presentation.Collect;
using ClipBench.Presentation.Rows;
using ClipBench.Presentation.Snippets;
using ClipBench.Presentation.Transforms;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClipBench.Presentation.Popup;

/// <summary>Everything the popup shows and does: the search, the list, the selection and the keys.</summary>
public sealed partial class PopupViewModel : ObservableObject, IDisposable
{
    /// <summary>How long typing must pause before the list is searched.</summary>
    public static readonly TimeSpan SearchDelay = TimeSpan.FromMilliseconds(150);

    private const string SaveFailed = "That change could not be saved.";
    // The most clips a list of pinned clips or of one board shows. The history list is cut shorter, by the search.
    private const int ScopeLimit = 1000;

    private const string HintGap = " · ";

    private readonly ClipLibrary _library;
    private readonly IPasteFlow _paste;
    private readonly IQueueControl _queue;
    private readonly CollectToggle _collect;
    private readonly TransformPickerViewModel _transforms;
    private readonly SidebarViewModel _sidebar;
    private readonly SnippetsViewModel _snippets;
    private readonly SettingsService _settings;
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
    /// <param name="queue">Runs a paste queue of the chosen clips.</param>
    /// <param name="collect">Starts and finishes collect mode.</param>
    /// <param name="transforms">The picker that changes the text before it is pasted.</param>
    /// <param name="sidebar">History, pinned clips, the boards and the snippets, and which of them the list shows.</param>
    /// <param name="snippets">The list of snippets, and pasting one.</param>
    /// <param name="settings">The user's settings, such as the separator for joining clips.</param>
    /// <param name="dispatcher">Moves work to the UI thread.</param>
    /// <param name="timeProvider">The clock and timers, for the age shown and the search delay.</param>
    public PopupViewModel(ClipLibrary library, IPasteFlow paste, IQueueControl queue, CollectToggle collect, TransformPickerViewModel transforms, SidebarViewModel sidebar, SnippetsViewModel snippets, SettingsService settings, IUiDispatcher dispatcher, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(paste);
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(collect);
        ArgumentNullException.ThrowIfNull(transforms);
        ArgumentNullException.ThrowIfNull(sidebar);
        ArgumentNullException.ThrowIfNull(snippets);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _library = library;
        _paste = paste;
        _queue = queue;
        _collect = collect;
        _transforms = transforms;
        _sidebar = sidebar;
        _snippets = snippets;
        _settings = settings;
        _dispatcher = dispatcher;
        _timeProvider = timeProvider;
        Rows = [];
        _searchTimer = timeProvider.CreateTimer(OnSearchTimer, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _library.Changed += OnLibraryChanged;
        _settings.Changed += OnSettingsChanged;
        _collect.Collect.Changed += OnCollectChanged;
        _transforms.PropertyChanged += OnTransformsChanged;
        _sidebar.ScopeChanged += OnScopeChanged;
        _sidebar.PropertyChanged += OnSidebarChanged;
        _snippets.PropertyChanged += OnSnippetsPropertyChanged;
        _snippets.Changed += OnSnippetsChanged;
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
    public bool ShowEmptyState => ItemCount == 0;

    /// <summary>What to say when there is nothing to list.</summary>
    public string EmptyText => !string.IsNullOrWhiteSpace(SearchText)
        ? (_sidebar.Scope == PopupScope.Snippets ? "No snippets match your search." : "No clips match your search.")
        : _sidebar.Scope switch
        {
            PopupScope.Snippets => "No snippets yet. Press Ctrl+E to make one.",
            PopupScope.Pinned => "No pinned clips yet. Press Ctrl+P on a clip to pin it.",
            PopupScope.Board => "This board is empty. Press Ctrl+D on a clip to add it.",
            _ => "Nothing copied yet. Copy something and it will show up here.",
        };

    /// <summary>The key hints, or the question when a delete needs confirming.</summary>
    public string StatusText
    {
        get
        {
            if (_transforms.IsOpen)
            {
                return _transforms.StatusText;
            }

            if (_snippets.IsPrompting)
            {
                return string.Join(HintGap, "Enter paste", "Tab next value", "Esc cancel");
            }

            if (_sidebar.IsEditing)
            {
                return string.Join(HintGap, "Enter save", "Esc cancel");
            }

            if (_sidebar.IsConfirmingDelete)
            {
                return _sidebar.DeleteQuestion;
            }

            if (IsConfirmingDelete)
            {
                return "Press Delete again to remove this pinned clip" + HintGap + "Esc cancels";
            }

            if (_sidebar.Scope == PopupScope.Snippets)
            {
                return string.Join(HintGap, "Enter paste", "Ctrl+E edit snippets", "Esc close");
            }

            if (MarkedCount > 0)
            {
                return MarkedHints;
            }

            if (Rows.Count == 0)
            {
                return "Esc close";
            }

            var second = Current is { IsSensitive: true } ? "Ctrl+R reveal" : "Space select";
            if (!IsCollecting && _sidebar.Scope == PopupScope.Board)
            {
                return string.Join(HintGap, "Enter paste", "Ctrl+Up/Down move", "Ctrl+Shift+D remove", "Esc close");
            }

            return IsCollecting
                ? string.Join(HintGap, $"Collecting {_collect.Collect.Count}", "Ctrl+L finish", "Enter paste", second, "Esc close")
                : string.Join(HintGap, "Enter paste", second, "Ctrl+P pin", "Del delete", "Esc close");
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
        _transforms.Close();
        _snippets.CancelPrompt();
        _sidebar.Reset();
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
        _transforms.Close();
        _snippets.CancelPrompt();
        _ = _sidebar.Cancel();
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
        _settings.Changed -= OnSettingsChanged;
        _collect.Collect.Changed -= OnCollectChanged;
        _transforms.PropertyChanged -= OnTransformsChanged;
        _sidebar.ScopeChanged -= OnScopeChanged;
        _sidebar.PropertyChanged -= OnSidebarChanged;
        _snippets.PropertyChanged -= OnSnippetsPropertyChanged;
        _snippets.Changed -= OnSnippetsChanged;
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
        OnPropertyChanged(nameof(CurrentSnippet));
        OnPropertyChanged(nameof(StatusText));
    }

    partial void OnIsConfirmingDeleteChanged(bool value) => OnPropertyChanged(nameof(StatusText));

    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(HasError));

    [RelayCommand]
    private void MoveDown()
    {
        ErrorMessage = null;
        IsConfirmingDelete = false;
        if (ItemCount > 0)
        {
            SelectedIndex = SelectedIndex < 0 ? 0 : (SelectedIndex + 1) % ItemCount;
        }
    }

    [RelayCommand]
    private void MoveUp()
    {
        ErrorMessage = null;
        IsConfirmingDelete = false;
        if (ItemCount > 0)
        {
            SelectedIndex = SelectedIndex <= 0 ? ItemCount - 1 : SelectedIndex - 1;
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
            OnPropertyChanged(nameof(PasteLabel));
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
        if (_transforms.IsOpen)
        {
            _transforms.Cancel();
            return;
        }

        if (_snippets.IsPrompting)
        {
            _snippets.CancelPrompt();
            return;
        }

        if (_sidebar.Cancel())
        {
            return;
        }

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
        if (_sidebar.Scope == PopupScope.Snippets)
        {
            await PasteSnippetAsync(cancellationToken);
            return;
        }

        if (_marks.Count > 0)
        {
            await PasteMarkedAsync(cancellationToken);
            return;
        }

        if (HighlightedClip() is not [var clip])
        {
            // A highlighted row whose clip has just left the history: show the list as it is now.
            if (Current is not null)
            {
                Refresh(keepSelection: true);
            }

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
        if (_sidebar.Scope == PopupScope.Snippets)
        {
            RefreshSnippets(keepSelection);
            return;
        }

        var previousId = Current?.Id;
        var previousIndex = SelectedIndex;
        var wasConfirming = IsConfirmingDelete;
        var now = _timeProvider.GetUtcNow();

        // A search only hides marked clips, but a clip that left the history leaves the marks.
        var present = _library.Clips.Select(clip => clip.Id).ToHashSet();
        _ = _marks.RemoveAll(id => !present.Contains(id));

        var results = _sidebar.Scope switch
        {
            PopupScope.Pinned => _library.Search(_library.Clips.Where(clip => clip.IsPinned), SearchText, ScopeLimit),
            PopupScope.Board when _sidebar.CurrentBoard is { } board => _library.Search(_library.ClipsOnBoard(board), SearchText, ScopeLimit),
            _ => _library.Search(SearchText),
        };
        var rows = results
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
        OnPropertyChanged(nameof(CurrentSnippet));
        OnPropertyChanged(nameof(ItemCount));
        OnPropertyChanged(nameof(MarkedCount));
        OnPropertyChanged(nameof(PasteLabel));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(EmptyText));
        OnPropertyChanged(nameof(StatusText));
    }
}
