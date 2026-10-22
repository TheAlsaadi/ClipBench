using System.Collections.Immutable;
using ClipBench.Core.Notifications;
using ClipBench.Core.Results;

namespace ClipBench.Core.Tray;

/// <summary>One line of the tray icon's menu.</summary>
/// <param name="Id">What the menu reports when the item is chosen. Positive. Zero marks a separator.</param>
/// <param name="Text">The label, or empty for a separator.</param>
/// <param name="IsDefault">True for the item that a double click would choose; Windows draws it in bold.</param>
/// <param name="IsChecked">True for a setting that is on; Windows draws a tick next to it.</param>
public sealed record TrayMenuItem(int Id, string Text, bool IsDefault = false, bool IsChecked = false)
{
    /// <summary>A line between two groups of items.</summary>
    public static TrayMenuItem Separator { get; } = new(0, string.Empty);

    /// <summary>True when the item is a separator.</summary>
    public bool IsSeparator => Id == 0;
}

/// <summary>What the tray icon shows.</summary>
/// <param name="ToolTip">The text when the pointer rests on the icon. Windows keeps only the first 127 characters.</param>
/// <param name="Menu">The menu, top to bottom.</param>
public sealed record TrayModel(string ToolTip, ImmutableArray<TrayMenuItem> Menu);

/// <summary>Tells which menu item was chosen.</summary>
/// <param name="id">The id of the item.</param>
public sealed class TrayMenuItemEventArgs(int id) : EventArgs
{
    /// <summary>The id of the item that was chosen.</summary>
    public int Id { get; } = id;
}

/// <summary>
/// The icon in the notification area, its menu and its notices. Events are raised on a thread-pool thread, never on
/// the thread that runs the icon's window.
/// </summary>
public interface ITrayIcon : IUserNotifier, IDisposable
{
    /// <summary>Raised when the icon is clicked or chosen with the keyboard.</summary>
    event EventHandler? OpenRequested;

    /// <summary>Raised when an item of the menu is chosen. Closing the menu without a choice raises nothing.</summary>
    event EventHandler<TrayMenuItemEventArgs>? MenuItemChosen;

    /// <summary>Puts the icon in the notification area. It is put back by itself if Explorer restarts.</summary>
    /// <param name="model">The tool tip and the menu.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>Success, or <see cref="ErrorCode.PlatformFailure"/> when Windows would not add the icon or its picture is missing.</returns>
    Task<Result> ShowAsync(TrayModel model, CancellationToken cancellationToken);

    /// <summary>Changes the tool tip and the menu.</summary>
    /// <param name="model">The new tool tip and menu.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>Success, or why the tool tip could not be changed.</returns>
    Task<Result> UpdateAsync(TrayModel model, CancellationToken cancellationToken);
}
