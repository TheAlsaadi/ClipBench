using ClipBench.Core.Tray;

namespace ClipBench.Presentation.Tray;

/// <summary>Builds what the tray icon shows from the tray view model and sends a chosen menu item to the right command.</summary>
public static class TrayMenu
{
    /// <summary>The id of the item that opens the popup.</summary>
    public const int OpenId = 1;

    /// <summary>The id of the item that pauses or resumes capturing.</summary>
    public const int PauseId = 2;

    /// <summary>The id of the item that quits.</summary>
    public const int ExitId = 3;

    /// <summary>Builds the tool tip and the menu.</summary>
    /// <param name="tray">The tray view model.</param>
    /// <returns>What the icon should show now.</returns>
    public static TrayModel Build(TrayViewModel tray)
    {
        ArgumentNullException.ThrowIfNull(tray);
        return new TrayModel(
            tray.ToolTip,
            [
                new TrayMenuItem(OpenId, "Open ClipBench", IsDefault: true),
                TrayMenuItem.Separator,
                new TrayMenuItem(PauseId, tray.PauseLabel),
                new TrayMenuItem(ExitId, "Exit"),
            ]);
    }

    /// <summary>Runs the command behind a menu item. An id that is not one of ours does nothing.</summary>
    /// <param name="tray">The tray view model.</param>
    /// <param name="id">The id the menu reported.</param>
    public static void Execute(TrayViewModel tray, int id)
    {
        ArgumentNullException.ThrowIfNull(tray);
        switch (id)
        {
            case OpenId:
                tray.OpenCommand.Execute(null);
                break;
            case PauseId:
                tray.TogglePauseCommand.Execute(null);
                break;
            case ExitId:
                tray.ExitCommand.Execute(null);
                break;
            default:
                break;
        }
    }
}
