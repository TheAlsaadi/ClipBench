using System.Collections.Frozen;
using ClipBench.Core.Results;

namespace ClipBench.Core.Hotkeys;

/// <summary>
/// A global keyboard shortcut: some modifiers and one key from a fixed list of named keys. Immutable, with
/// value equality. Shortcuts are written the way they are shown, for example <c>Win+Alt+V</c>.
/// </summary>
public sealed record Hotkey
{
    private const HotkeyModifiers AllModifiers = HotkeyModifiers.Alt | HotkeyModifiers.Control | HotkeyModifiers.Shift | HotkeyModifiers.Windows;
    private const int FirstFunctionKey = 0x70;
    private const int LastFunctionKey = 0x87;
    private const int MaxTypedLength = 64;

    // Names are the same on every keyboard layout. Punctuation keys are left out on purpose: their key
    // codes depend on the layout, so a name would mean different keys on different machines.
    private static readonly (string Name, int VirtualKey)[] _keys =
    [
        .. Enumerable.Range('A', 26).Select(code => (((char)code).ToString(), code)),
        .. Enumerable.Range('0', 10).Select(code => (((char)code).ToString(), code)),
        .. Enumerable.Range(0, 24).Select(index => ($"F{index + 1}", FirstFunctionKey + index)),
        ("Space", 0x20), ("Enter", 0x0D), ("Esc", 0x1B), ("Tab", 0x09), ("Backspace", 0x08),
        ("Insert", 0x2D), ("Delete", 0x2E), ("Home", 0x24), ("End", 0x23), ("PageUp", 0x21), ("PageDown", 0x22),
        ("Left", 0x25), ("Up", 0x26), ("Right", 0x27), ("Down", 0x28),
    ];

    private static readonly FrozenDictionary<string, int> _codeByName =
        _keys.ToFrozenDictionary(key => key.Name, key => key.VirtualKey, StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenDictionary<int, string> _nameByCode =
        _keys.ToFrozenDictionary(key => key.VirtualKey, key => key.Name);

    /// <summary>Creates a shortcut.</summary>
    /// <param name="modifiers">The keys held down with the main key. Any combination, including none.</param>
    /// <param name="virtualKey">The Windows virtual-key code of the main key, which must be one of <see cref="KeyNames"/>.</param>
    public Hotkey(HotkeyModifiers modifiers, int virtualKey)
    {
        if ((modifiers & ~AllModifiers) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(modifiers), modifiers, "Unknown modifier.");
        }

        if (!_nameByCode.ContainsKey(virtualKey))
        {
            throw new ArgumentOutOfRangeException(nameof(virtualKey), virtualKey, "Not one of the named keys.");
        }

        Modifiers = modifiers;
        VirtualKey = virtualKey;
    }

    /// <summary>The shortcut that opens the popup until the user picks another: Win+Alt+V (ADR 0004).</summary>
    public static Hotkey Default { get; } = new(HotkeyModifiers.Windows | HotkeyModifiers.Alt, 'V');

    /// <summary>The names of the keys a shortcut can use, as they are shown and typed.</summary>
    public static IReadOnlyList<string> KeyNames { get; } = [.. _keys.Select(key => key.Name)];

    /// <summary>The keys held down with the main key.</summary>
    public HotkeyModifiers Modifiers { get; }

    /// <summary>The Windows virtual-key code of the main key.</summary>
    public int VirtualKey { get; }

    /// <summary>
    /// Reads a shortcut a person typed. The parts are separated by <c>+</c>, in any order and in any case,
    /// and exactly one of them is a key. A shortcut must include Ctrl, Alt or Windows, unless its key is
    /// a function key: a global shortcut on a plain typing key would stop people typing in every program.
    /// </summary>
    /// <param name="typed">The text.</param>
    /// <returns>The shortcut, or <see cref="ErrorCode.HotkeyInvalid"/>.</returns>
    public static Result<Hotkey> Parse(string typed)
    {
        ArgumentNullException.ThrowIfNull(typed);

        // The messages below never repeat the typed text: it may have been pasted from the clipboard.
        if (typed.Length > MaxTypedLength)
        {
            return Invalid("That is too long to be a shortcut.");
        }

        var modifiers = HotkeyModifiers.None;
        int? key = null;
        foreach (var part in typed.Split('+', StringSplitOptions.TrimEntries))
        {
            if (part.Length == 0)
            {
                return Invalid("A shortcut looks like Ctrl+Alt+V. Each part must be a key name.");
            }

            if (TryReadModifier(part, out var modifier))
            {
                if ((modifiers & modifier) != 0)
                {
                    return Invalid("A modifier key is named twice.");
                }

                modifiers |= modifier;
            }
            else if (_codeByName.TryGetValue(part, out var code))
            {
                if (key is not null)
                {
                    return Invalid("A shortcut has one main key. Use Ctrl, Alt, Shift or Win for the others.");
                }

                key = code;
            }
            else
            {
                return Invalid("That is not a key name ClipBench knows. Use a letter, a digit, F1 to F24, or a name like Space.");
            }
        }

        if (key is null)
        {
            return Invalid("A shortcut needs a key as well as Ctrl, Alt, Shift or Win.");
        }

        var hotkey = new Hotkey(modifiers, key.Value);
        return hotkey.IsSafeForGlobalUse
            ? Result.Success(hotkey)
            : Invalid("Add Ctrl, Alt or Win. A shortcut on a typing key alone would stop you typing in every program.");
    }

    /// <summary>The shortcut as it is shown and typed, with the modifiers in the order Win, Ctrl, Alt, Shift.</summary>
    /// <returns>For example <c>Win+Alt+V</c>.</returns>
    public override string ToString()
    {
        var parts = new List<string>(5);
        AddIf(HotkeyModifiers.Windows, "Win");
        AddIf(HotkeyModifiers.Control, "Ctrl");
        AddIf(HotkeyModifiers.Alt, "Alt");
        AddIf(HotkeyModifiers.Shift, "Shift");
        parts.Add(_nameByCode[VirtualKey]);
        return string.Join('+', parts);

        void AddIf(HotkeyModifiers modifier, string name)
        {
            if ((Modifiers & modifier) != 0)
            {
                parts.Add(name);
            }
        }
    }

    private bool IsSafeForGlobalUse =>
        (Modifiers & (HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Windows)) != 0
        || VirtualKey is >= FirstFunctionKey and <= LastFunctionKey;

    private static bool TryReadModifier(string name, out HotkeyModifiers modifier)
    {
        modifier = name.ToUpperInvariant() switch
        {
            "CTRL" or "CONTROL" => HotkeyModifiers.Control,
            "ALT" => HotkeyModifiers.Alt,
            "SHIFT" => HotkeyModifiers.Shift,
            "WIN" or "WINDOWS" => HotkeyModifiers.Windows,
            _ => HotkeyModifiers.None,
        };
        return modifier != HotkeyModifiers.None;
    }

    private static Result<Hotkey> Invalid(string message) =>
        Result.Failure<Hotkey>(new ResultError(ErrorCode.HotkeyInvalid, message));
}
