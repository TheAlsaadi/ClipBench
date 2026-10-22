using ClipBench.Core.Clips;
using ClipBench.Core.Results;

namespace ClipBench.Core.Pasting;

/// <summary>The part of queue paste that the popup, the tray and the queue window use: start, stop and progress.</summary>
public interface IQueueControl
{
    /// <summary>Raised after every step of a queue, on the thread that took it.</summary>
    event EventHandler? Changed;

    /// <summary>Where the queue is.</summary>
    QueueProgress Progress { get; }

    /// <summary>Starts a queue of clips, stopping one that runs.</summary>
    /// <param name="clips">The clips, in paste order.</param>
    /// <param name="cancellationToken">Cancels the start.</param>
    /// <returns>Success, or why the queue did not start, in words for the user.</returns>
    Task<Result> StartAsync(IReadOnlyList<Clip> clips, CancellationToken cancellationToken);

    /// <summary>Stops the queue and gives its keys back.</summary>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>A task that completes when the keys are given back.</returns>
    Task StopAsync(CancellationToken cancellationToken);
}
