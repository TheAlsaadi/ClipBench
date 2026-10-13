using ClipBench.Core.Screen;

namespace ClipBench.Platform.Windows.Tray;

/// <summary>What the tray icon's callback message says happened.</summary>
internal enum TrayEvent
{
    /// <summary>Something that needs no reaction (mouse moves, button down, balloons closing).</summary>
    Ignored,

    /// <summary>The icon was clicked or chosen with the keyboard.</summary>
    Select,

    /// <summary>The menu was asked for with the right button or the menu key.</summary>
    ContextMenu,
}

/// <summary>A decoded tray callback.</summary>
/// <param name="Event">What happened.</param>
/// <param name="IconId">Which icon of this window it happened to.</param>
/// <param name="Anchor">Where on the screen it happened, for placing the menu.</param>
internal readonly record struct TrayMessage(TrayEvent Event, uint IconId, PixelPoint Anchor)
{
    /// <summary>Reads the two parameters of the callback message of an icon that asked for version 4 messages.</summary>
    /// <param name="wParam">Low word: x, high word: y, both signed, in screen pixels.</param>
    /// <param name="lParam">Low word: the notification, high word: the icon id.</param>
    /// <returns>The decoded message.</returns>
    public static TrayMessage Decode(nint wParam, nint lParam)
    {
        // Only the low 32 bits carry the two words. Windows may leave anything above them.
        var parameters = (long)lParam;
        var notification = (int)(parameters & 0xFFFF);
        var iconId = (uint)((parameters >> 16) & 0xFFFF);
        var position = (long)wParam;
        var anchor = new PixelPoint((short)(position & 0xFFFF), (short)((position >> 16) & 0xFFFF));

        var kind = notification switch
        {
            NinSelect or NinKeySelect => TrayEvent.Select,
            WmContextMenu => TrayEvent.ContextMenu,
            _ => TrayEvent.Ignored,
        };

        return new TrayMessage(kind, iconId, anchor);
    }

    private const int NinSelect = 0x400;
    private const int NinKeySelect = 0x401;
    private const int WmContextMenu = 0x7B;
}

/// <summary>Shortens text to what the notification area can hold.</summary>
internal static class TrayText
{
    /// <summary>Longest tool tip, in UTF-16 units.</summary>
    public const int MaxToolTip = 127;

    /// <summary>Longest notice title.</summary>
    public const int MaxTitle = 63;

    /// <summary>Longest notice text.</summary>
    public const int MaxMessage = 255;

    /// <summary>Cuts text to a length, ending with an ellipsis when something was cut and never splitting a character.</summary>
    /// <param name="text">The text.</param>
    /// <param name="maxLength">The most UTF-16 units to keep, ellipsis included.</param>
    /// <returns>The text, or its start with an ellipsis.</returns>
    public static string Fit(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        if (text.Length <= maxLength)
        {
            return text;
        }

        // Leave room for the ellipsis, and do not keep half of a pair of UTF-16 units.
        var keep = maxLength - 1;
        if (keep > 0 && char.IsHighSurrogate(text[keep - 1]))
        {
            keep--;
        }

        return text[..keep] + "…";
    }
}
