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

    /// <summary>Replaces what cannot be used with a default, so the settings can be trusted as they are.</summary>
    /// <returns>The settings, with an unknown choice or an unreadable custom separator reset.</returns>
    public AppSettings Normalize()
    {
        var customIsReadable = ReadCustom() is not null;
        var choice = Enum.IsDefined(Separator) && (Separator != SeparatorChoice.Custom || customIsReadable)
            ? Separator
            : SeparatorChoice.NewLine;

        return this with { Separator = choice, CustomSeparator = customIsReadable ? CustomSeparator : string.Empty };
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
