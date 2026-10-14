using ClipBench.Presentation.Popup;
using Microsoft.UI.Dispatching;

namespace ClipBench.App.Composition;

/// <summary>Moves work to the UI thread through the WinUI dispatcher queue.</summary>
internal sealed class DispatcherQueueDispatcher : IUiDispatcher
{
    private readonly DispatcherQueue _queue;

    public DispatcherQueueDispatcher(DispatcherQueue queue)
    {
        ArgumentNullException.ThrowIfNull(queue);
        _queue = queue;
    }

    /// <inheritdoc/>
    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _ = _queue.TryEnqueue(() => action());
    }
}
