using System.Collections.Immutable;
using ClipBench.Core.Clips;
using ClipBench.Core.Detection;
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
    private readonly Detection _detection;

    /// <summary>Creates the row for a clip.</summary>
    /// <param name="clip">The clip.</param>
    /// <param name="sensitivity">Whether the clip looks like a secret. Such a clip is hidden until revealed.</param>
    /// <param name="now">The current time, for the age shown.</param>
    /// <param name="detection">What the clip's text is, or null for nothing in particular. It is not shown while a secret is hidden.</param>
    public ClipRowViewModel(Clip clip, SensitiveKind sensitivity, DateTimeOffset now, Detection? detection = null)
    {
        ArgumentNullException.ThrowIfNull(clip);
        _detection = detection ?? Detection.None;
        Id = clip.Id;
        Format = clip.Content.Format;
        IsSensitive = sensitivity != SensitiveKind.None;
        IsPinned = clip.IsPinned;
        IsOnBoard = clip.OnBoard is not null;
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

    /// <summary>True when the clip is on a board.</summary>
    public bool IsOnBoard { get; }

    /// <summary>True when the user marked the row for a multi-paste.</summary>
    [ObservableProperty]
    public partial bool IsMarked { get; set; }

    /// <summary>True when a sensitive clip is shown in full.</summary>
    [ObservableProperty]
    public partial bool IsRevealed { get; set; }

    /// <summary>Where the clip comes in the paste order of the marked clips, from 1, or 0 when it is not marked.</summary>
    [ObservableProperty]
    public partial int MarkNumber { get; set; }

    /// <summary>True when the row shows its place in the paste order.</summary>
    public bool HasMarkNumber => MarkNumber > 0;

    /// <summary>What the row shows: the preview, or <see cref="Mask"/> for a hidden sensitive clip.</summary>
    public string DisplayText => IsSensitive && !IsRevealed ? Mask : _preview;

    /// <summary>True when the row says what the clip is (and the clip is not a hidden secret).</summary>
    public bool HasDetection => Shown.Kind != DetectedKind.None;

    /// <summary>What the clip is, in a word or two for the row: "Link", "Email", "Phone", "Code · Python", "Color #1E90FF". Empty for nothing.</summary>
    public string DetectionLabel => Shown.Kind switch
    {
        DetectedKind.Color => $"Color {Shown.Color!.Value.Hex}",
        DetectedKind.Link => "Link",
        DetectedKind.Email => "Email",
        DetectedKind.Phone => "Phone",
        DetectedKind.Code => Shown.Language is { } language ? $"Code \u00B7 {language}" : "Code",
        _ => string.Empty,
    };

    /// <summary>True when the clip is a color and the row shows a swatch of it.</summary>
    public bool HasSwatch => Shown.Kind == DetectedKind.Color;

    /// <summary>The red channel of the swatch.</summary>
    public byte SwatchRed => Shown.Color?.Red ?? 0;

    /// <summary>The green channel of the swatch.</summary>
    public byte SwatchGreen => Shown.Color?.Green ?? 0;

    /// <summary>The blue channel of the swatch.</summary>
    public byte SwatchBlue => Shown.Color?.Blue ?? 0;

    /// <summary>The alpha channel of the swatch.</summary>
    public byte SwatchAlpha => Shown.Color?.Alpha ?? 255;

    /// <summary>The web address to open when the clip is a link, or null. It is only opened when the user asks.</summary>
    public string? LinkAddress => Shown.Kind == DetectedKind.Link ? Shown.Address : null;

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
            if (HasDetection)
            {
                // "Color #FF0000" is read as "color #FF0000": only the first letter changes, so the hex stays as it is.
                var spoken = DetectionLabel.Replace('\u00B7', ',');
                parts.Add(char.ToLowerInvariant(spoken[0]) + spoken[1..]);
            }

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

            if (HasMarkNumber)
            {
                parts.Add($"number {MarkNumber} in paste order");
            }

            return string.Join(", ", parts);
        }
    }

    // A secret that is hidden shows nothing about what it is: a link with a token in it would give itself away.
    private Detection Shown => IsSensitive && !IsRevealed ? Detection.None : _detection;

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
        OnPropertyChanged(nameof(HasDetection));
        OnPropertyChanged(nameof(DetectionLabel));
        OnPropertyChanged(nameof(HasSwatch));
        OnPropertyChanged(nameof(LinkAddress));
    }

    partial void OnIsMarkedChanged(bool value) => OnPropertyChanged(nameof(AutomationName));

    partial void OnMarkNumberChanged(int value)
    {
        OnPropertyChanged(nameof(HasMarkNumber));
        OnPropertyChanged(nameof(AutomationName));
    }
}
