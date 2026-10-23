using System.Collections.ObjectModel;
using System.Globalization;
using ClipBench.Core.Clips;
using ClipBench.Core.Diagnostics;
using ClipBench.Core.Transforms;
using ClipBench.Presentation.Rows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClipBench.Presentation.Transforms;

/// <summary>
/// The transform picker: a filterable list of transforms, a chain of the ones chosen and a preview of what the paste
/// would be. It works on the text it was opened with and never touches a clip. Pasting is the popup's job.
/// </summary>
public sealed partial class TransformPickerViewModel : ObservableObject
{
    /// <summary>The most transforms in one chain.</summary>
    public const int MaxChainLength = 10;

    /// <summary>The longest source text that is previewed as the choice changes. Longer text is only transformed on Enter.</summary>
    public const int LivePreviewLimit = 1_000_000;

    /// <summary>What the preview says for a text too long to preview.</summary>
    public const string PreviewSkipped = "The text is large, so there is no preview.";

    private const string FindReplaceId = "text.find-replace";
    private const string Arrow = " → ";
    private const string HintGap = " · ";

    private static readonly TransformItemViewModel _findReplaceItem =
        new(FindReplaceId, "Find and replace…", "Replace text, or use a pattern", NeedsInput: true);

    private readonly TransformRegistry _registry;
    private readonly TransformPipeline _pipeline;
    private readonly IActivityLog _log;
    private readonly List<TransformItemViewModel> _all;
    private readonly List<TextTransform> _chain = [];

    private string _source = string.Empty;
    private bool _sensitive;
    private bool _applyAfterInput;
    private bool _resetting;

    /// <summary>Creates the picker.</summary>
    /// <param name="registry">The transforms to offer.</param>
    /// <param name="pipeline">Runs the chain.</param>
    /// <param name="log">Records that a chain ran or failed, without any text.</param>
    public TransformPickerViewModel(TransformRegistry registry, TransformPipeline pipeline, IActivityLog log)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(log);
        _registry = registry;
        _pipeline = pipeline;
        _log = log;
        _all = [.. registry.All.Select(transform => new TransformItemViewModel(transform.Id, transform.Name, transform.Description)), _findReplaceItem];
        Items = [];
    }

    /// <summary>The transforms that match the filter.</summary>
    public ObservableCollection<TransformItemViewModel> Items { get; }

    /// <summary>True while the picker is shown.</summary>
    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    /// <summary>What the user typed to narrow the list.</summary>
    [ObservableProperty]
    public partial string FilterText { get; set; } = string.Empty;

    /// <summary>The highlighted line, or -1.</summary>
    [ObservableProperty]
    public partial int SelectedIndex { get; set; } = -1;

    /// <summary>A short message about what did not work, or null.</summary>
    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    /// <summary>What the paste would be, cut short, or the reason it cannot be made.</summary>
    [ObservableProperty]
    public partial string Preview { get; set; } = string.Empty;

    /// <summary>True when <see cref="Preview"/> is a reason and not a result.</summary>
    [ObservableProperty]
    public partial bool PreviewIsError { get; set; }

    /// <summary>The transforms chosen so far, in the order they run, joined by arrows. Empty when none.</summary>
    [ObservableProperty]
    public partial string ChainText { get; set; } = string.Empty;

    /// <summary>True while the find-and-replace fields are shown.</summary>
    [ObservableProperty]
    public partial bool IsAskingFindReplace { get; set; }

    /// <summary>The text to find.</summary>
    [ObservableProperty]
    public partial string FindText { get; set; } = string.Empty;

    /// <summary>The text to put in its place.</summary>
    [ObservableProperty]
    public partial string ReplaceText { get; set; } = string.Empty;

    /// <summary>True to read the text to find as a pattern.</summary>
    [ObservableProperty]
    public partial bool UseRegex { get; set; }

    /// <summary>True to ignore capital and small letters.</summary>
    [ObservableProperty]
    public partial bool IgnoreCase { get; set; }

    /// <summary>True while there is an <see cref="ErrorMessage"/> to show.</summary>
    public bool HasError => ErrorMessage is not null;

    /// <summary>True while the list is shown, which is whenever the find-and-replace fields are not.</summary>
    public bool IsChoosing => !IsAskingFindReplace;

    /// <summary>True when <see cref="Preview"/> is a result and not a reason.</summary>
    public bool PreviewIsResult => !PreviewIsError;

    /// <summary>The highlighted line, or null.</summary>
    public TransformItemViewModel? Current => SelectedIndex >= 0 && SelectedIndex < Items.Count ? Items[SelectedIndex] : null;

    /// <summary>The key hints for what is shown now.</summary>
    public string StatusText
    {
        get
        {
            if (IsAskingFindReplace)
            {
                return string.Join(HintGap, _applyAfterInput ? "Enter apply" : "Enter add to chain", "Tab next field", "Esc back");
            }

            return _chain.Count > 0
                ? string.Join(HintGap, "Enter apply chain", "Ctrl+Space add", "Backspace remove last", "Esc back")
                : string.Join(HintGap, "Enter apply", "Ctrl+Space add to chain", "Esc back");
        }
    }

    /// <summary>Shows the picker for a text.</summary>
    /// <param name="sourceText">The text that would be pasted without a transform.</param>
    /// <param name="isSensitive">True when the text looks like a secret: the preview is then hidden.</param>
    public void Open(string sourceText, bool isSensitive)
    {
        ArgumentNullException.ThrowIfNull(sourceText);
        Reset();
        _source = sourceText;
        _sensitive = isSensitive;
        IsOpen = true;
        Refilter();
    }

    /// <summary>Hides the picker and forgets the text, the chain and everything typed.</summary>
    public void Close()
    {
        Reset();
        IsOpen = false;
        Items.Clear();
        SelectedIndex = -1;
        Preview = string.Empty;
        PreviewIsError = false;
    }

    /// <summary>Goes back one step: out of the find-and-replace fields, or out of the picker.</summary>
    public void Cancel()
    {
        if (IsAskingFindReplace)
        {
            StopAskingForFindReplace();
            ErrorMessage = null;
            return;
        }

        Close();
    }

    /// <summary>Runs the chain, or the highlighted transform when the chain is empty.</summary>
    /// <param name="text">The changed text, when this returns true.</param>
    /// <returns>True when there is text to paste. False when more input is needed, or the text could not be changed (see <see cref="ErrorMessage"/>).</returns>
    public bool TryConfirm(out string text)
    {
        text = string.Empty;
        if (!IsOpen)
        {
            return false;
        }

        ErrorMessage = null;
        List<TextTransform> chain = [.. _chain];
        if (chain.Count == 0)
        {
            if (Current is not { } current)
            {
                return false;
            }

            if (current.NeedsInput)
            {
                AskForFindReplace(applyAfter: true);
                return false;
            }

            chain.Add(_registry.Find(current.Id)!);
        }

        var result = _pipeline.Run(_source, chain);
        if (result.IsFailure)
        {
            ErrorMessage = result.Error.Message;
            _log.Write(ActivityLevel.Warning, ActivityEvent.TransformFailed, new ActivityDetails { Error = result.Error.Code, Count = chain.Count });
            return false;
        }

        _log.Write(ActivityLevel.Information, ActivityEvent.TransformApplied, new ActivityDetails { Count = chain.Count });
        text = result.Value;
        Close();
        return true;
    }

    /// <summary>Builds the find-and-replace transform from the fields. If it was asked for with Enter, it then runs the chain.</summary>
    /// <param name="text">The changed text, when this returns true.</param>
    /// <returns>True when there is text to paste.</returns>
    public bool TrySubmitFindReplace(out string text)
    {
        text = string.Empty;
        if (!IsOpen || !IsAskingFindReplace)
        {
            return false;
        }

        ErrorMessage = null;
        if (_chain.Count >= MaxChainLength)
        {
            ErrorMessage = ChainFullMessage;
            return false;
        }

        var created = FindReplace.Create(new FindReplaceOptions { Find = FindText, Replace = ReplaceText, UseRegex = UseRegex, IgnoreCase = IgnoreCase });
        if (created.IsFailure)
        {
            ErrorMessage = created.Error.Message;
            return false;
        }

        _chain.Add(created.Value);
        var applyNow = _applyAfterInput;
        StopAskingForFindReplace();
        ShowChain();
        if (applyNow)
        {
            return TryConfirm(out text);
        }

        UpdatePreview();
        return false;
    }

    private static string ChainFullMessage => $"A chain holds at most {MaxChainLength} transforms.";

    partial void OnFilterTextChanged(string value)
    {
        if (!_resetting && IsOpen)
        {
            ErrorMessage = null;
            Refilter();
        }
    }

    partial void OnSelectedIndexChanged(int value)
    {
        OnPropertyChanged(nameof(Current));
        if (!_resetting && IsOpen)
        {
            UpdatePreview();
        }
    }

    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(HasError));

    partial void OnIsAskingFindReplaceChanged(bool value)
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(IsChoosing));
    }

    partial void OnPreviewIsErrorChanged(bool value) => OnPropertyChanged(nameof(PreviewIsResult));

    [RelayCommand]
    private void MoveDown()
    {
        ErrorMessage = null;
        if (Items.Count > 0)
        {
            SelectedIndex = SelectedIndex < 0 ? 0 : (SelectedIndex + 1) % Items.Count;
        }
    }

    [RelayCommand]
    private void MoveUp()
    {
        ErrorMessage = null;
        if (Items.Count > 0)
        {
            SelectedIndex = SelectedIndex <= 0 ? Items.Count - 1 : SelectedIndex - 1;
        }
    }

    [RelayCommand]
    private void AddToChain()
    {
        if (!IsOpen || Current is not { } current)
        {
            return;
        }

        ErrorMessage = null;
        if (_chain.Count >= MaxChainLength)
        {
            ErrorMessage = ChainFullMessage;
            return;
        }

        if (current.NeedsInput)
        {
            AskForFindReplace(applyAfter: false);
            return;
        }

        _chain.Add(_registry.Find(current.Id)!);
        ShowChain();
        UpdatePreview();
    }

    [RelayCommand]
    private void RemoveLastFromChain()
    {
        if (_chain.Count == 0)
        {
            return;
        }

        _chain.RemoveAt(_chain.Count - 1);
        ErrorMessage = null;
        ShowChain();
        UpdatePreview();
    }

    private void AskForFindReplace(bool applyAfter)
    {
        _applyAfterInput = applyAfter;
        IsAskingFindReplace = true;
        OnPropertyChanged(nameof(StatusText));
    }

    private void StopAskingForFindReplace()
    {
        _applyAfterInput = false;
        IsAskingFindReplace = false;
        OnPropertyChanged(nameof(StatusText));
    }

    private void ShowChain()
    {
        ChainText = string.Join(Arrow, _chain.Select(transform => transform.Name));
        OnPropertyChanged(nameof(StatusText));
    }

    // Forgets the text, the chain and everything typed, without touching what is listed.
    private void Reset()
    {
        _resetting = true;
        try
        {
            _source = string.Empty;
            _sensitive = false;
            _chain.Clear();
            _applyAfterInput = false;
            IsAskingFindReplace = false;
            FilterText = string.Empty;
            FindText = string.Empty;
            ReplaceText = string.Empty;
            UseRegex = false;
            IgnoreCase = false;
            ErrorMessage = null;
            ShowChain();
        }
        finally
        {
            _resetting = false;
        }
    }

    private void Refilter()
    {
        var wanted = FilterText.Trim();
        _resetting = true;
        try
        {
            Items.Clear();
            foreach (var item in _all.Where(item => Matches(item, wanted)))
            {
                Items.Add(item);
            }

            SelectedIndex = Items.Count > 0 ? 0 : -1;
        }
        finally
        {
            _resetting = false;
        }

        OnPropertyChanged(nameof(Current));
        UpdatePreview();
    }

    private static bool Matches(TransformItemViewModel item, string wanted)
    {
        if (wanted.Length == 0)
        {
            return true;
        }

        var compare = CultureInfo.InvariantCulture.CompareInfo;
        return compare.IndexOf(item.Name, wanted, CompareOptions.IgnoreCase) >= 0
            || compare.IndexOf(item.Description, wanted, CompareOptions.IgnoreCase) >= 0;
    }

    // What Enter would paste: the chain, or else the highlighted transform.
    private void UpdatePreview()
    {
        PreviewIsError = false;
        if (_sensitive)
        {
            Preview = ClipRowViewModel.Mask;
            return;
        }

        if (_source.Length > LivePreviewLimit)
        {
            Preview = PreviewSkipped;
            return;
        }

        List<TextTransform> chain = [.. _chain];
        if (chain.Count == 0 && Current is { } current)
        {
            if (current.NeedsInput)
            {
                Preview = "Enter asks what to find and what to put in its place.";
                return;
            }

            chain.Add(_registry.Find(current.Id)!);
        }

        var result = _pipeline.Run(_source, chain);
        if (result.IsFailure)
        {
            Preview = result.Error.Message;
            PreviewIsError = true;
            return;
        }

        Preview = Rows.PreviewText.For(new TextContent(result.Value));
    }
}
