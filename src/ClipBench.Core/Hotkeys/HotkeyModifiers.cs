namespace ClipBench.Core.Hotkeys;

/// <summary>The keys held down together with the main key of a shortcut.</summary>
[Flags]
public enum HotkeyModifiers
{
    /// <summary>No modifier.</summary>
    None = 0,

    /// <summary>The Alt key.</summary>
    Alt = 1,

    /// <summary>The Ctrl key.</summary>
    Control = 2,

    /// <summary>The Shift key.</summary>
    Shift = 4,

    /// <summary>The Windows key.</summary>
    Windows = 8,
}
