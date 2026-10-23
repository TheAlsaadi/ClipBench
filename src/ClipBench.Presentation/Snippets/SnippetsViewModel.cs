using System.Collections.ObjectModel;
using System.Text;
using ClipBench.Core.Clips;
using ClipBench.Core.Diagnostics;
using ClipBench.Core.Results;
using ClipBench.Core.Snippets;
using ClipBench.Presentation.Rows;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClipBench.Presentation.Snippets;

/// <summary>
/// The snippets in the popup: the list and its search, and pasting one, which may first ask for values. Used on the
/// UI thread only, except that <see cref="Changed"/> can be raised from any thread.
/// </summary>
public sealed partial class SnippetsViewModel : ObservableObject, IDisposable
{
    /// <summary>The longest preview line, in characters.</summary>
    public const int PreviewLength = PreviewText.DefaultMaxLineLength;

    private readonly SnippetCatalog _catalog;
    private readonly SnippetEngine _engine;
    private readonly IActivityLog _log;
    private volatile bool _disposed;

    // The snippet whose values are being asked for, and the clipboard text from the moment the paste began.
    private ParsedSnippet? _pending;
    private string? _pendingClipboard;

    /// <summary>Creates the view model and starts following the catalog.</summary>
    /// <param name="catalog">The snippets.</param>
    /// <param name="engine">Reads and fills in snippets.</param>
    /// <param name="log">Records that a snippet was pasted or could not be, without any text.</param>
    public SnippetsViewModel(SnippetCatalog catalog, SnippetEngine engine, IActivityLog log)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(log);
        _catalog = catalog;
        _engine = engine;
        _log = log;
        Rows = [];
        Fields = [];
        _catalog.Changed += OnCatalogChanged;
    }

    /// <summary>Raised, on whatever thread changed them, after the snippets changed.</summary>
    public event EventHandler? Changed;

    /// <summary>The rows shown.</summary>
    public ObservableCollection<SnippetRowViewModel> Rows { get; }

    /// <summary>The values asked for.</summary>
    public ObservableCollection<SnippetFieldViewModel> Fields { get; }

    /// <summary>How many snippets there are.</summary>
    public int Count => _catalog.Snippets.Length;

    /// <summary>True while the values are being asked for.</summary>
    [ObservableProperty]
    public partial bool IsPrompting { get; set; }

    /// <summary>The name of the snippet whose values are being asked for.</summary>
    [ObservableProperty]
    public partial string PromptTitle { get; set; } = string.Empty;

    /// <summary>Why the values could not be used, or null.</summary>
    [ObservableProperty]
    public partial string? PromptError { get; set; }

    /// <summary>Lists the snippets whose name or text holds the query, those with the query in the name first.</summary>
    /// <param name="query">What the user typed. Empty lists all of them.</param>
    public void Refresh(string query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var wanted = query.Trim();
        var snippets = _catalog.Snippets;
        IEnumerable<Snippet> matches = wanted.Length == 0
            ? snippets
            : snippets.Where(snippet => snippet.Name.Contains(wanted, StringComparison.OrdinalIgnoreCase))
                .Concat(snippets.Where(snippet =>
                    !snippet.Name.Contains(wanted, StringComparison.OrdinalIgnoreCase)
                    && snippet.Body.Contains(wanted, StringComparison.OrdinalIgnoreCase)));

        var rows = matches.Select(Row).ToList();
        Rows.Clear();
        foreach (var row in rows)
        {
            Rows.Add(row);
        }
    }

    /// <summary>Starts pasting a snippet: makes the text, or opens the prompt for its values.</summary>
    /// <param name="id">The snippet.</param>
    /// <param name="clipboardText">What <c>{clipboard}</c> stands for, or null.</param>
    /// <returns>The text, that the prompt is open, or why the snippet cannot be pasted.</returns>
    public SnippetStart Begin(SnippetId id, string? clipboardText)
    {
        CancelPrompt();

        if (_catalog.Find(id) is not { } snippet)
        {
            return Failed(new ResultError(ErrorCode.SnippetNotFound, "That snippet no longer exists."));
        }

        var parsed = SnippetEngine.Parse(snippet.Body);
        if (parsed.IsFailure)
        {
            return Failed(parsed.Error);
        }

        if (parsed.Value.InputNames.Count == 0)
        {
            var text = _engine.Expand(parsed.Value, new SnippetValues(clipboardText, null));
            if (text.IsFailure)
            {
                return Failed(text.Error);
            }

            LogPasted(0, text.Value);
            return new SnippetStart(SnippetStartKind.Ready, text.Value, null);
        }

        _pending = parsed.Value;
        _pendingClipboard = clipboardText;
        foreach (var name in parsed.Value.InputNames)
        {
            Fields.Add(new SnippetFieldViewModel(name));
        }

        PromptTitle = snippet.Name;
        IsPrompting = true;
        return new SnippetStart(SnippetStartKind.NeedsInput, string.Empty, null);
    }

    /// <summary>Makes the text from the values typed in the prompt.</summary>
    /// <param name="text">The text, when this returns true.</param>
    /// <returns>True when there is text to paste. False when the prompt is not open, or the values cannot be used (see <see cref="PromptError"/>).</returns>
    public bool TrySubmit(out string text)
    {
        text = string.Empty;
        if (!IsPrompting || _pending is null)
        {
            return false;
        }

        var typed = Fields.ToDictionary(field => field.Name, field => field.Value, StringComparer.OrdinalIgnoreCase);
        var expanded = _engine.Expand(_pending, new SnippetValues(_pendingClipboard, typed));
        if (expanded.IsFailure)
        {
            PromptError = expanded.Error.Message;
            _log.Write(ActivityLevel.Warning, ActivityEvent.SnippetFailed, new ActivityDetails { Error = expanded.Error.Code });
            return false;
        }

        text = expanded.Value;
        LogPasted(Fields.Count, text);
        CancelPrompt();
        return true;
    }

    /// <summary>Closes the prompt and forgets what was typed.</summary>
    public void CancelPrompt()
    {
        _pending = null;
        _pendingClipboard = null;
        Fields.Clear();
        PromptTitle = string.Empty;
        PromptError = null;
        IsPrompting = false;
    }

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

    private SnippetStart Failed(ResultError error)
    {
        _log.Write(ActivityLevel.Warning, ActivityEvent.SnippetFailed, new ActivityDetails { Error = error.Code });
        return new SnippetStart(SnippetStartKind.Failed, string.Empty, error.Message);
    }

    // The log gets how many values the snippet asked for and how big the text is, never the text.
    private void LogPasted(int values, string text) =>
        _log.Write(ActivityLevel.Information, ActivityEvent.SnippetPasted, new ActivityDetails { Count = values, Bytes = Encoding.UTF8.GetByteCount(text) });

    private static SnippetRowViewModel Row(Snippet snippet)
    {
        var parsed = SnippetEngine.Parse(snippet.Body);
        return new SnippetRowViewModel(
            snippet.Id,
            snippet.Name,
            PreviewText.For(new TextContent(snippet.Body)),
            parsed.IsSuccess ? parsed.Value.InputNames.Count : 0);
    }

    private void OnCatalogChanged(object? sender, EventArgs e)
    {
        if (!_disposed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
