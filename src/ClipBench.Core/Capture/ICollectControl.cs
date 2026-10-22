using ClipBench.Core.Results;

namespace ClipBench.Core.Capture;

/// <summary>The part of collect mode that the tray, the popup and the shortcut use.</summary>
public interface ICollectControl
{
    /// <summary>Raised when a session starts or ends and when a copy is collected, on the thread that did it.</summary>
    event EventHandler? Changed;

    /// <summary>True while a session collects.</summary>
    bool IsActive { get; }

    /// <summary>How many copies the session holds.</summary>
    int Count { get; }

    /// <summary>Starts a session. A session that runs keeps what it has.</summary>
    void Start();

    /// <summary>Ends the session: what was collected becomes one clip at the top of the history and is put on the clipboard.</summary>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <returns>Success, also when nothing was collected, or why the clip could not be saved or put on the clipboard.</returns>
    Task<Result> FinishAsync(CancellationToken cancellationToken);

    /// <summary>Ends the session and throws away what was collected.</summary>
    void Cancel();
}
