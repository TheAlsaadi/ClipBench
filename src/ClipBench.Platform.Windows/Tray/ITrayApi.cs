using ClipBench.Core.Screen;
using ClipBench.Core.Tray;

namespace ClipBench.Platform.Windows.Tray;

/// <summary>The notification-area calls, kept apart so the icon's logic can be tested without a taskbar.</summary>
internal interface ITrayApi
{
    /// <summary>The message the taskbar broadcasts when it is created again, for example after Explorer restarts.</summary>
    uint TaskbarCreatedMessage { get; }

    /// <summary>Loads the icon picture from a file.</summary>
    /// <param name="path">The .ico file.</param>
    /// <returns>The icon handle, or zero when it could not be loaded.</returns>
    nint LoadIcon(string path);

    /// <summary>Frees an icon returned by <see cref="LoadIcon"/>.</summary>
    /// <param name="icon">The handle.</param>
    void DestroyIcon(nint icon);

    /// <summary>Adds the icon.</summary>
    /// <returns>False when Windows refused.</returns>
    bool Add(nint window, uint iconId, uint callbackMessage, nint icon, string toolTip);

    /// <summary>Asks for version 4 callback messages, which carry the anchor point and a separate notice for the keyboard. Call it right after <see cref="Add"/>.</summary>
    /// <returns>False when Windows refused.</returns>
    bool UseVersion4(nint window, uint iconId);

    /// <summary>Changes the tool tip of the icon.</summary>
    /// <returns>False when Windows refused.</returns>
    bool Modify(nint window, uint iconId, string toolTip);

    /// <summary>Takes the icon away.</summary>
    /// <returns>False when Windows refused.</returns>
    bool Delete(nint window, uint iconId);

    /// <summary>Shows a notice next to the icon.</summary>
    /// <returns>False when Windows refused.</returns>
    bool Notify(nint window, uint iconId, string title, string message);
}

/// <summary>Shows the icon's menu and waits for the choice.</summary>
internal interface ITrayMenu
{
    /// <summary>Shows the menu at a point and waits until an item is chosen or the menu is closed.</summary>
    /// <param name="window">The window that owns the menu.</param>
    /// <param name="at">Where the menu opens, in screen pixels.</param>
    /// <param name="items">The items, top to bottom.</param>
    /// <returns>The id of the chosen item, or zero when the menu was closed without a choice.</returns>
    int Show(nint window, PixelPoint at, IReadOnlyList<TrayMenuItem> items);
}
