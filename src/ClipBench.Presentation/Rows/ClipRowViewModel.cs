using System.Collections.Immutable;
using ClipBench.Core.Clips;
using ClipBench.Core.Privacy;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClipBench.Presentation.Rows;

/// <summary>One clip as a row of the popup list.</summary>
public sealed partial class ClipRowViewModel : ObservableObject
{
    /// <summary>What a hidden sensitive clip shows. Its length does not depend on the clip.</summary>
    public const string Mask = "••••••••";

    private readonly string _preview;
    private readonly string _age;
    private readonly string? _source;

    /// <summary>Creates the row for a clip.</summary>
    /// <param name="clip">The clip.</param>
    /// <param name="sensitivity">Whether the clip looks like a secret. Such a clip is hidden until revealed.</param>
    /// <param name="now">The current time, for the age shown.</param>
    public ClipRowViewModel(Clip clip, SensitiveKind sensitivity, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(clip);
        Id = clip.Id;
        Format = clip.Content.Format;
        IsSensitive = sensitivity != SensitiveKind.None;
        IsPinned = clip.IsPinned;
        ImagePng = clip.Content is ImageContent image ? image.Png : [];
        _preview = PreviewText.For(clip.Content);
        _age = RelativeTime.Format(clip.CapturedAt, now);
        _source = SourceName.Friendly(clip.SourceApp);
    }

    /// <summary>The clip id.</summary>
    public ClipId Id { get; }

    /// <summary>The kind of content.</summary>
    public ClipFormat Format { get; }

    /// <summary>True when the clip looks like a secret.</summary>
    public bool IsSensitive { get; }

    /// <summary>True when the history limits do not apply to the clip.</summary>
    public bool IsPinned { get; }

    /// <summary>True when the user marked the row for a multi-paste.</summary>
    [ObservableProperty]
    public partial bool IsMarked { get; set; }

    /// <summary>True when a sensitive clip is shown in full.</summary>
    [ObservableProperty]
    public partial bool IsRevealed { get; set; }

    /// <summary>What the row shows: the preview, or <see cref="Mask"/> for a hidden sensitive clip.</summary>
    public string DisplayText => IsSensitive && !IsRevealed ? Mask : _preview;

    /// <summary>The source program and age, such as "Notepad · 5 min ago".</summary>
    public string Details => _source is null ? _age : $"{_source} · {_age}";

    /// <summary>True when the clip is a picture.</summary>
    public bool HasImage => Format == ClipFormat.Image;

    /// <summary>The PNG data of a picture, empty for other clips.</summary>
    public ImmutableArray<byte> ImagePng { get; }

    /// <summary>The text a screen reader says for the row. A hidden clip is not read out.</summary>
    public string AutomationName
    {
        get
        {
            var shown = IsSensitive && !IsRevealed ? "hidden sensitive content" : _preview.Replace('\n', ' ');
            var parts = new List<string> { KindName(Format), shown };
            if (_source is not null)
            {
                parts.Add($"from {_source}");
            }

            parts.Add(_age);
            if (IsPinned)
            {
                parts.Add("pinned");
            }

            if (IsMarked)
            {
                parts.Add("selected");
            }

            return string.Join(", ", parts);
        }
    }

    private static string KindName(ClipFormat format) => format switch
    {
        ClipFormat.Text => "Text",
        ClipFormat.Html => "HTML",
        ClipFormat.Rtf => "Rich text",
        ClipFormat.Image => "Image",
        ClipFormat.Files => "Files",
        _ => "Clip",
    };

    partial void OnIsRevealedChanged(bool value)
    {
        OnPropertyChanged(nameof(DisplayText));
        OnPropertyChanged(nameof(AutomationName));
    }

    partial void OnIsMarkedChanged(bool value) => OnPropertyChanged(nameof(AutomationName));
}
