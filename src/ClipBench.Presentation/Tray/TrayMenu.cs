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

    /// <summary>The id of the item that stops a running paste queue.</summary>
    public const int StopQueueId = 4;

    /// <summary>The id of the item that starts or finishes collecting.</summary>
    public const int CollectId = 5;

    /// <summary>The id of the item that cancels collecting.</summary>
    public const int CancelCollectId = 6;

    /// <summary>The id of the item that switches putting the clipboard back after a paste.</summary>
    public const int RestoreId = 7;

    /// <summary>The id of the item that opens the snippet editor.</summary>
    public const int SnippetsId = 8;

    /// <summary>Builds the tool tip and the menu.</summary>
    /// <param name="tray">The tray view model.</param>
    /// <returns>What the icon should show now.</returns>
    public static TrayModel Build(TrayViewModel tray)
    {
        ArgumentNullException.ThrowIfNull(tray);
        List<TrayMenuItem> menu = [new TrayMenuItem(OpenId, "Open ClipBench", IsDefault: true), TrayMenuItem.Separator];
        if (tray.IsQueueRunning)
        {
            menu.Add(new TrayMenuItem(StopQueueId, "Stop paste queue"));
        }

        menu.Add(new TrayMenuItem(CollectId, tray.CollectLabel));
        if (tray.IsCollecting)
        {
            menu.Add(new TrayMenuItem(CancelCollectId, "Cancel collecting"));
        }

        menu.Add(new TrayMenuItem(RestoreId, "Restore clipboard after paste", IsChecked: tray.RestoreClipboard));
        menu.Add(new TrayMenuItem(SnippetsId, "Snippets…"));
        menu.Add(new TrayMenuItem(PauseId, tray.PauseLabel));
        menu.Add(new TrayMenuItem(ExitId, "Exit"));
        return new TrayModel(tray.ToolTip, [.. menu]);
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
            case StopQueueId:
                tray.StopQueueCommand.Execute(null);
                break;
            case CollectId:
                tray.ToggleCollectCommand.Execute(null);
                break;
            case CancelCollectId:
                tray.CancelCollectCommand.Execute(null);
                break;
            case RestoreId:
                tray.ToggleRestoreCommand.Execute(null);
                break;
            case SnippetsId:
                tray.EditSnippetsCommand.Execute(null);
                break;
            default:
                break;
        }
    }
}
