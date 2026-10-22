using ClipBench.Core.Capture;
using ClipBench.Core.Hotkeys;
using ClipBench.Core.Notifications;

namespace ClipBench.Presentation.Collect;

/// <summary>
/// Starts collect mode, or finishes it when a session runs, and tells the user in a notice. The tray menu, the popup
/// and the collect shortcut all use it, so each says the same thing.
/// </summary>
public sealed class CollectToggle
{
    private const string Title = "ClipBench";

    private readonly IUserNotifier _notifier;

    /// <summary>Creates the toggle.</summary>
    /// <param name="collect">The collect session.</param>
    /// <param name="notifier">Tells the user what happened.</param>
    public CollectToggle(ICollectControl collect, IUserNotifier notifier)
    {
        ArgumentNullException.ThrowIfNull(collect);
        ArgumentNullException.ThrowIfNull(notifier);
        Collect = collect;
        _notifier = notifier;
    }

    /// <summary>The global shortcut that starts and finishes collecting until the user picks another: Win+Alt+C (ADR 0015).</summary>
    public static Hotkey DefaultShortcut { get; } = new(HotkeyModifiers.Windows | HotkeyModifiers.Alt, 'C');

    /// <summary>The collect session, to read its state and follow its changes.</summary>
    public ICollectControl Collect { get; }

    /// <summary>Starts a session, or finishes the one that runs.</summary>
    /// <param name="cancellationToken">Cancels a finish.</param>
    /// <returns>A task that completes when the session started or finished.</returns>
    public async Task ToggleAsync(CancellationToken cancellationToken)
    {
        if (!Collect.IsActive)
        {
            Collect.Start();
            _notifier.Show(Title, "Collecting. Every copy you make is added to one clip until you finish.");
            return;
        }

        var count = Collect.Count;
        var finished = await Collect.FinishAsync(cancellationToken).ConfigureAwait(false);

        // A finish that failed has told the user why already.
        if (finished.IsFailure)
        {
            return;
        }

        _notifier.Show(Title, count switch
        {
            0 => "Collecting stopped. Nothing was collected.",
            1 => "Collected 1 copy. It is on your clipboard and at the top of your history.",
            _ => $"Collected {count} copies into one clip. It is on your clipboard and at the top of your history.",
        });
    }

    /// <summary>Ends the session and throws away what it holds, without a notice: the user chose it from a menu.</summary>
    public void Cancel() => Collect.Cancel();
}
