using ClipBench.Core.Clips;

namespace ClipBench.Presentation.Popup;

/// <summary>Runs work on the thread that owns the user interface.</summary>
public interface IUiDispatcher
{
    /// <summary>Queues an action for the UI thread. It never blocks the caller.</summary>
    /// <param name="action">What to run.</param>
    void Post(Action action);
}

/// <summary>What happens around the popup when a clip is pasted: remembering where to paste and doing it.</summary>
public interface IPasteFlow
{
    /// <summary>Remembers the window that has focus now, as the place to paste. Call it before the popup takes focus.</summary>
    void Begin();

    /// <summary>
    /// Pastes a clip into the remembered window. The popup is already hidden. When the paste fails, the user is told
    /// why; nothing is thrown for that.
    /// </summary>
    /// <param name="clip">The clip to paste.</param>
    /// <param name="cancellationToken">Cancels the paste before any key is sent.</param>
    /// <returns>A task that completes when the paste has been attempted.</returns>
    Task PasteAsync(Clip clip, CancellationToken cancellationToken);
}
