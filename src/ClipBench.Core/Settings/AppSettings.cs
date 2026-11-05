namespace ClipBench.Core.Settings;

/// <summary>The separator the user picked for joining clips.</summary>
public enum SeparatorChoice
{
    /// <summary>A Windows line break.</summary>
    NewLine,

    /// <summary>An empty line.</summary>
    BlankLine,

    /// <summary>A comma and a space.</summary>
    CommaSpace,

    /// <summary>A comma.</summary>
    Comma,

    /// <summary>A semicolon.</summary>
    Semicolon,

    /// <summary>A tab.</summary>
    Tab,

    /// <summary>A space.</summary>
    Space,

    /// <summary>Nothing.</summary>
    None,

    /// <summary>The separator the user typed, in <see cref="AppSettings.CustomSeparator"/>.</summary>
    Custom,
}

/// <summary>The user's choices that last between runs. Never holds clip content.</summary>
public sealed record AppSettings
{
    /// <summary>The longest custom separator as typed: every character written as a two-character escape.</summary>
    public const int MaxTypedSeparatorLength = Pasting.Separator.MaxCustomLength * 2;

    /// <summary>The allowed values of <see cref="HistoryMaxCount"/>.</summary>
    public static SettingRange HistoryMaxCountRange { get; } = new(10, 10_000);

    /// <summary>The allowed values of <see cref="HistoryMaxAgeDays"/>.</summary>
    public static SettingRange HistoryMaxAgeDaysRange { get; } = new(1, 365);

    /// <summary>The allowed values of <see cref="HistoryMaxMegabytes"/>.</summary>
    public static SettingRange HistoryMaxMegabytesRange { get; } = new(16, 2048);

    /// <summary>The allowed values of <see cref="SensitiveExpirySeconds"/>.</summary>
    public static SettingRange SensitiveExpirySecondsRange { get; } = new(15, 3600);

    /// <summary>The settings of a new installation.</summary>
    public static AppSettings Default { get; } = new();

    /// <summary>The separator used to join clips.</summary>
    public SeparatorChoice Separator { get; init; } = SeparatorChoice.NewLine;

    /// <summary>The custom separator as the user typed it, with escape sequences such as \t unresolved.</summary>
    public string CustomSeparator
    {
        get;
        init => field = value ?? throw new ArgumentNullException(nameof(value));
    } = string.Empty;

    /// <summary>True to put the user's clipboard back after a paste.</summary>
    public bool RestorePreviousClipboard { get; init; } = true;

    /// <summary>True to also keep each copy as its own clip while a collect session runs.</summary>
    public bool KeepCopiesWhileCollecting { get; init; }

    /// <summary>The most unpinned clips kept, within <see cref="HistoryMaxCountRange"/>.</summary>
    public int HistoryMaxCount { get; init; } = 1000;

    /// <summary>How many days an unpinned clip is kept, within <see cref="HistoryMaxAgeDaysRange"/>.</summary>
    public int HistoryMaxAgeDays { get; init; } = 30;

    /// <summary>The most megabytes of content the unpinned clips may hold together, within <see cref="HistoryMaxMegabytesRange"/>.</summary>
    public int HistoryMaxMegabytes { get; init; } = 256;

    /// <summary>How many seconds a clip that looks like a secret is kept, within <see cref="SensitiveExpirySecondsRange"/>.</summary>
    public int SensitiveExpirySeconds { get; init; } = 120;

    /// <summary>True to hide clips that look like secrets behind a mask until they are revealed.</summary>
    public bool MaskSensitive { get; init; } = true;

    /// <summary>True to keep ClipBench's windows out of screen captures and recordings.</summary>
    public bool HideFromScreenCapture { get; init; } = true;

    /// <summary>True to start ClipBench when the user signs in to Windows.</summary>
    public bool StartWithWindows { get; init; }

    /// <summary>True once the first-run questions have been asked, whatever the answers were.</summary>
    public bool FirstRunCompleted { get; init; }

    /// <summary>The separator to join clips with.</summary>
    /// <returns>The chosen separator, or a line break when the choice cannot be used.</returns>
    public Pasting.Separator ResolveSeparator() => Separator switch
    {
        SeparatorChoice.NewLine => Pasting.Separator.NewLine,
        SeparatorChoice.BlankLine => Pasting.Separator.BlankLine,
        SeparatorChoice.CommaSpace => Pasting.Separator.CommaSpace,
        SeparatorChoice.Comma => Pasting.Separator.Comma,
        SeparatorChoice.Semicolon => Pasting.Separator.Semicolon,
        SeparatorChoice.Tab => Pasting.Separator.Tab,
        SeparatorChoice.Space => Pasting.Separator.Space,
        SeparatorChoice.None => Pasting.Separator.None,
        SeparatorChoice.Custom when ReadCustom() is { } custom => custom,
        _ => Pasting.Separator.NewLine,
    };

    /// <summary>Replaces what cannot be used, so the settings can be trusted as they are.</summary>
    /// <returns>The settings, with an unknown choice or an unreadable custom separator reset and every number moved into its range.</returns>
    public AppSettings Normalize()
    {
        var customIsReadable = ReadCustom() is not null;
        var choice = Enum.IsDefined(Separator) && (Separator != SeparatorChoice.Custom || customIsReadable)
            ? Separator
            : SeparatorChoice.NewLine;

        return this with
        {
            Separator = choice,
            CustomSeparator = customIsReadable ? CustomSeparator : string.Empty,
            HistoryMaxCount = HistoryMaxCountRange.Clamp(HistoryMaxCount),
            HistoryMaxAgeDays = HistoryMaxAgeDaysRange.Clamp(HistoryMaxAgeDays),
            HistoryMaxMegabytes = HistoryMaxMegabytesRange.Clamp(HistoryMaxMegabytes),
            SensitiveExpirySeconds = SensitiveExpirySecondsRange.Clamp(SensitiveExpirySeconds),
        };
    }

    private Pasting.Separator? ReadCustom()
    {
        if (CustomSeparator.Length > MaxTypedSeparatorLength)
        {
            return null;
        }

        var parsed = Pasting.Separator.Parse(CustomSeparator);
        return parsed.IsSuccess ? parsed.Value : null;
    }
}
