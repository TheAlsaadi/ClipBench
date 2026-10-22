using ClipBench.Core.Pasting;
using ClipBench.Presentation.Popup;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClipBench.Presentation.Queue;

/// <summary>
/// The small window that shows a running paste queue: which clip Ctrl+V pastes next and how to stop. It shows counts
/// only, never clip content.
/// </summary>
public sealed partial class QueueHudViewModel : ObservableObject, IDisposable
{
    private readonly IQueueControl _queue;
    private readonly IUiDispatcher _dispatcher;
    private QueueProgress _shown;
    private volatile bool _disposed;

    /// <summary>Creates the view model and starts following the queue.</summary>
    /// <param name="queue">The queue.</param>
    /// <param name="dispatcher">Moves updates to the UI thread.</param>
    public QueueHudViewModel(IQueueControl queue, IUiDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(dispatcher);
        _queue = queue;
        _dispatcher = dispatcher;
        _shown = queue.Progress;
        _queue.Changed += OnQueueChanged;
    }

    /// <summary>True while a queue runs.</summary>
    public bool IsVisible => _shown.IsRunning;

    /// <summary>The text shown, such as "Queue 2/5 · Esc to stop".</summary>
    public string Text => $"Queue {Next}/{_shown.Count} · Esc to stop";

    /// <summary>The text a screen reader says.</summary>
    public string AutomationText => $"Paste queue, next clip {Next} of {_shown.Count}. Press Esc to stop.";

    // The clip the next Ctrl+V pastes, counted from 1. While the last one is being pasted it stays the last.
    private int Next => Math.Min(_shown.Pasted + 1, _shown.Count);

    /// <summary>Stops following the queue.</summary>
    public void Dispose()
    {
        _disposed = true;
        _queue.Changed -= OnQueueChanged;
    }

    // Runs on whatever thread took the queue's step.
    private void OnQueueChanged(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        _dispatcher.Post(() =>
        {
            if (_disposed)
            {
                return;
            }

            _shown = _queue.Progress;
            OnPropertyChanged(nameof(IsVisible));
            OnPropertyChanged(nameof(Text));
            OnPropertyChanged(nameof(AutomationText));
        });
    }
}
