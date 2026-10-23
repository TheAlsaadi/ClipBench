using System.Collections.ObjectModel;
using ClipBench.Core.Snippets;
using ClipBench.Presentation.Popup;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClipBench.Presentation.Snippets;

/// <summary>One snippet in the list of the editor.</summary>
/// <param name="Id">The snippet id.</param>
/// <param name="Name">What the snippet is called.</param>
public sealed record SnippetListItem(SnippetId Id, string Name)
{
    /// <summary>Printing never shows the name: it is what the user typed.</summary>
    /// <returns>The id.</returns>
    public override string ToString() => $"Snippet {Id.Value:N}";
}

/// <summary>
/// The snippet editor: the list of snippets, the name and the text of the chosen one, what is wrong with the text and
/// what it would paste. Changes are kept until Save; choosing another snippet with unsaved changes is refused. Used on
/// the UI thread only.
/// </summary>
public sealed partial class SnippetEditorViewModel : ObservableObject, IDisposable
{
    /// <summary>What a screen shows to explain the variables.</summary>
    public const string HelpText =
        "{date} and {time} are today's date and the time (for example {date:dd/MM/yyyy}). {clipboard} is your latest copied text. " +
        "{input:Name} asks for a value when you paste. Type {{ or }} for a brace itself.";

    /// <summary>The longest preview, in characters.</summary>
    public const int PreviewLimit = 300;

    private const int ExcerptLength = 30;
    private const string Ellipsis = "…";
    private const string Unsaved = "Save or discard your changes first.";

    private readonly SnippetCatalog _catalog;
    private readonly SnippetEngine _engine;
    private readonly IUiDispatcher _dispatcher;
    private volatile bool _disposed;
    private bool _quiet;

    // The snippet the boxes were filled from, as it was saved; null while a new one is being written.
    private Snippet? _loaded;

    /// <summary>Creates the editor and starts following the catalog.</summary>
    /// <param name="catalog">The snippets.</param>
    /// <param name="engine">Reads and fills in snippets.</param>
    /// <param name="dispatcher">Moves updates to the UI thread.</param>
    public SnippetEditorViewModel(SnippetCatalog catalog, SnippetEngine engine, IUiDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(dispatcher);
        _catalog = catalog;
        _engine = engine;
        _dispatcher = dispatcher;
        Items = [];
        Warnings = [];
        FillItems();
        Reset();
        _catalog.Changed += OnCatalogChanged;
    }

    /// <summary>The snippets, in the order they were made.</summary>
    public ObservableCollection<SnippetListItem> Items { get; }

    /// <summary>What is wrong with the text, one line each.</summary>
    public ObservableCollection<string> Warnings { get; }

    /// <summary>The chosen snippet in <see cref="Items"/>, or -1 while a new one is being written.</summary>
    [ObservableProperty]
    public partial int SelectedIndex { get; set; } = -1;

    /// <summary>True while a snippet that is not saved yet is being written.</summary>
    [ObservableProperty]
    public partial bool IsNew { get; set; }

    /// <summary>The name being edited.</summary>
    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    /// <summary>The text being edited.</summary>
    [ObservableProperty]
    public partial string Body { get; set; } = string.Empty;

    /// <summary>True when the name or the text differs from what is saved.</summary>
    [ObservableProperty]
    public partial bool IsDirty { get; set; }

    /// <summary>What the text would paste, with placeholders for the values that are asked for.</summary>
    [ObservableProperty]
    public partial string Preview { get; set; } = string.Empty;

    /// <summary>A message about the last action that did not work, or null.</summary>
    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    /// <summary>True after delete was chosen, until the user confirms or backs out.</summary>
    [ObservableProperty]
    public partial bool IsConfirmingDelete { get; set; }

    /// <summary>True while a name and a text can be written: a saved snippet is chosen or a new one is started.</summary>
    public bool CanEdit => IsNew || HasSelection;

    /// <summary>True while a saved snippet is chosen.</summary>
    public bool HasSelection => _loaded is not null;

    /// <summary>The question that asks to confirm a delete.</summary>
    public string DeleteQuestion => $"Delete the snippet \"{_loaded?.Name}\"? Delete again to confirm · Esc cancels";

    /// <summary>Stops following the catalog.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _catalog.Changed -= OnCatalogChanged;
    }

    /// <summary>Shows the first snippet, or an empty new one when there are none. Unsaved changes are dropped.</summary>
    public void Reset()
    {
        if (_catalog.Snippets.Length > 0)
        {
            ShowIndex(0);
        }
        else
        {
            StartNew();
        }
    }

    partial void OnSelectedIndexChanged(int oldValue, int newValue)
    {
        if (_quiet)
        {
            return;
        }

        if (IsDirty)
        {
            // The boxes hold changes that are not saved, so the choice goes back and the user is told why.
            SetQuietly(() => SelectedIndex = oldValue);
            ErrorMessage = Unsaved;
            return;
        }

        if (newValue >= 0 && newValue < _catalog.Snippets.Length)
        {
            ShowIndex(newValue);
        }
        else
        {
            StartNew();
        }
    }

    partial void OnNameChanged(string value) => Edited();

    partial void OnBodyChanged(string value) => Edited();

    [RelayCommand]
    private void New()
    {
        if (IsDirty)
        {
            ErrorMessage = Unsaved;
            return;
        }

        StartNew();
    }

    [RelayCommand]
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        if (!IsDirty)
        {
            return;
        }

        ErrorMessage = null;
        if (_loaded is { } existing)
        {
            var updated = await _catalog.UpdateAsync(existing.Id, Name, Body, cancellationToken);
            FillItems();
            if (updated.IsFailure)
            {
                ErrorMessage = updated.Error.Message;
                return;
            }

            ShowIndex(IndexOf(existing.Id));
            return;
        }

        var created = await _catalog.CreateAsync(Name, Body, cancellationToken);
        FillItems();
        if (created.IsFailure)
        {
            ErrorMessage = created.Error.Message;
            return;
        }

        ShowIndex(IndexOf(created.Value.Id));
    }

    [RelayCommand]
    private void Discard()
    {
        if (_loaded is { } existing)
        {
            ShowIndex(IndexOf(existing.Id));
        }
        else
        {
            StartNew();
        }
    }

    [RelayCommand]
    private async Task DeleteAsync(CancellationToken cancellationToken)
    {
        if (_loaded is not { } existing)
        {
            return;
        }

        if (!IsConfirmingDelete)
        {
            IsConfirmingDelete = true;
            return;
        }

        IsConfirmingDelete = false;
        var index = SelectedIndex;

        // A store that fails still leaves the snippet off the list, as everywhere else.
        _ = await _catalog.DeleteAsync(existing.Id, cancellationToken);
        FillItems();
        if (_catalog.Snippets.Length > 0)
        {
            ShowIndex(Math.Min(Math.Max(index, 0), _catalog.Snippets.Length - 1));
        }
        else
        {
            StartNew();
        }
    }

    [RelayCommand]
    private void CancelDelete() => IsConfirmingDelete = false;

    // Called when the user types.
    private void Edited()
    {
        if (_quiet)
        {
            return;
        }

        ErrorMessage = null;
        IsConfirmingDelete = false;
        UpdateDirty();
        Analyze();
    }

    private void UpdateDirty() =>
        IsDirty = _loaded is null ? Name.Length > 0 || Body.Length > 0 : Name != _loaded.Name || Body != _loaded.Body;

    // Shows a saved snippet, dropping whatever was typed.
    private void ShowIndex(int index)
    {
        var snippet = _catalog.Snippets[index];
        SetQuietly(() =>
        {
            _loaded = snippet;
            SelectedIndex = index;
            IsNew = false;
            Name = snippet.Name;
            Body = snippet.Body;
            IsDirty = false;
            ErrorMessage = null;
            IsConfirmingDelete = false;
        });
        NotifyShape();
        Analyze();
    }

    private void StartNew()
    {
        SetQuietly(() =>
        {
            _loaded = null;
            SelectedIndex = -1;
            IsNew = true;
            Name = string.Empty;
            Body = string.Empty;
            IsDirty = false;
            ErrorMessage = null;
            IsConfirmingDelete = false;
        });
        NotifyShape();
        Analyze();
    }

    private void SetQuietly(Action change)
    {
        _quiet = true;
        try
        {
            change();
        }
        finally
        {
            _quiet = false;
        }
    }

    private void NotifyShape()
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(DeleteQuestion));
    }

    private void FillItems()
    {
        var snippets = _catalog.Snippets;
        SetQuietly(() =>
        {
            Items.Clear();
            foreach (var snippet in snippets)
            {
                Items.Add(new SnippetListItem(snippet.Id, snippet.Name));
            }

            SelectedIndex = _loaded is { } shown ? IndexOf(shown.Id) : -1;
        });
    }

    private int IndexOf(SnippetId id)
    {
        for (var i = 0; i < Items.Count; i++)
        {
            if (Items[i].Id == id)
            {
                return i;
            }
        }

        return -1;
    }

    // Runs on whatever thread changed the snippets.
    private void OnCatalogChanged(object? sender, EventArgs e)
    {
        if (!_disposed)
        {
            _dispatcher.Post(() =>
            {
                if (!_disposed)
                {
                    Follow();
                }
            });
        }
    }

    // The snippets changed somewhere else: the list is brought up to date and the boxes are left alone if they hold changes.
    private void Follow()
    {
        var shownId = _loaded?.Id;
        var wasDirty = IsDirty;
        FillItems();
        if (shownId is not { } id)
        {
            return;
        }

        var fresh = _catalog.Find(id);
        if (fresh is null)
        {
            // The snippet was deleted elsewhere. Unsaved text is kept, as a new snippet; otherwise the next one is shown.
            if (wasDirty)
            {
                SetQuietly(() =>
                {
                    _loaded = null;
                    SelectedIndex = -1;
                    IsNew = true;
                    IsDirty = true;
                    IsConfirmingDelete = false;
                });
                NotifyShape();
            }
            else
            {
                Reset();
            }

            return;
        }

        if (wasDirty)
        {
            _loaded = fresh;
            UpdateDirty();
        }
        else
        {
            ShowIndex(IndexOf(id));
        }
    }

    // What is wrong with the text, and what it would paste.
    private void Analyze()
    {
        Warnings.Clear();
        Preview = string.Empty;

        var parsed = SnippetEngine.Parse(Body);
        if (parsed.IsFailure)
        {
            Warnings.Add(parsed.Error.Message);
            return;
        }

        foreach (var warning in parsed.Value.Warnings)
        {
            Warnings.Add(Describe(warning));
        }

        if (Body.Length == 0)
        {
            return;
        }

        var inputs = parsed.Value.InputNames.ToDictionary(name => name, name => $"<{name}>", StringComparer.OrdinalIgnoreCase);
        var expanded = _engine.Expand(parsed.Value, new SnippetValues("<clipboard>", inputs));
        if (expanded.IsSuccess)
        {
            Preview = expanded.Value.Length > PreviewLimit ? expanded.Value[..PreviewLimit] + Ellipsis : expanded.Value;
        }
    }

    private string Describe(SnippetWarning warning)
    {
        var line = 1 + Body.AsSpan(0, warning.Position).Count('\n');
        var shown = Body.Substring(warning.Position, warning.Length);
        var excerpt = shown.Length > ExcerptLength ? shown[..ExcerptLength] + Ellipsis : shown;
        return warning.Kind switch
        {
            SnippetWarningKind.UnknownVariable => $"Line {line}: \"{excerpt}\" is not a variable, so it is pasted as typed.",
            SnippetWarningKind.InvalidFormat => $"Line {line}: \"{excerpt}\" has a date or time format that cannot be used, so it is pasted as typed.",
            SnippetWarningKind.InvalidInputName => $"Line {line}: \"{excerpt}\" needs a name of up to {SnippetEngine.MaxInputNameLength} letters, digits, spaces, hyphens or underscores.",
            _ => $"Line {line}: a {{ is never closed. Type {{{{ to write a brace.",
        };
    }
}
