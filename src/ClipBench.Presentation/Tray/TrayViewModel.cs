using ClipBench.Core.Capture;
using ClipBench.Core.Pasting;
using ClipBench.Presentation.Collect;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClipBench.Presentation.Tray;

/// <summary>What the tray icon shows and what its menu does.</summary>
public sealed partial class TrayViewModel : ObservableObject, IDisposable
{
    private readonly ICaptureControl _capture;
    private readonly IQueueControl _queue;
    private readonly CollectToggle _collect;
    private int _exitRequested;

    /// <summary>Creates the view model.</summary>
    /// <param name="capture">The part of capture that can be paused.</param>
    /// <param name="queue">The paste queue, which the menu can stop.</param>
    /// <param name="collect">Starts and finishes collect mode.</param>
    public TrayViewModel(ICaptureControl capture, IQueueControl queue, CollectToggle collect)
    {
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(collect);
        _capture = capture;
        _queue = queue;
        _collect = collect;
        IsPaused = capture.IsPaused;
        _queue.Changed += OnQueueChanged;
        _collect.Collect.Changed += OnCollectChanged;
    }

    /// <summary>Raised when the menu asks for the popup.</summary>
    public event EventHandler? OpenRequested;

    /// <summary>Raised once when the menu asks to quit.</summary>
    public event EventHandler? ExitRequested;

    /// <summary>True while capture is paused.</summary>
    [ObservableProperty]
    public partial bool IsPaused { get; set; }

    /// <summary>True while a collect session runs.</summary>
    public bool IsCollecting => _collect.Collect.IsActive;

    /// <summary>The label of the collect item: what choosing it does.</summary>
    public string CollectLabel => IsCollecting ? $"Finish collecting ({_collect.Collect.Count})" : "Start collecting";

    /// <summary>True while a paste queue runs.</summary>
    public bool IsQueueRunning => _queue.Progress.IsRunning;

    /// <summary>The label of the pause item: what choosing it does.</summary>
    public string PauseLabel => IsPaused ? "Resume capturing" : "Pause capturing";

    /// <summary>The text shown when the pointer rests on the icon.</summary>
    public string ToolTip =>
        (IsPaused ? "ClipBench · paused" : "ClipBench · capturing")
        + (IsCollecting ? $" · collecting {_collect.Collect.Count}" : string.Empty)
        + (IsQueueRunning ? " · paste queue running" : string.Empty);

    partial void OnIsPausedChanged(bool value)
    {
        OnPropertyChanged(nameof(PauseLabel));
        OnPropertyChanged(nameof(ToolTip));
    }

    [RelayCommand]
    private void TogglePause()
    {
        _capture.SetPaused(!IsPaused);
        IsPaused = _capture.IsPaused;
    }

    /// <summary>Stops following the queue.</summary>
    public void Dispose()
    {
        _queue.Changed -= OnQueueChanged;
        _collect.Collect.Changed -= OnCollectChanged;
    }

    [RelayCommand]
    private Task ToggleCollectAsync(CancellationToken cancellationToken) => _collect.ToggleAsync(cancellationToken);

    [RelayCommand]
    private void CancelCollect() => _collect.Cancel();

    [RelayCommand]
    private Task StopQueueAsync(CancellationToken cancellationToken) => _queue.StopAsync(cancellationToken);

    // Runs on whatever thread collected a copy or started or ended the session.
    private void OnCollectChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(IsCollecting));
        OnPropertyChanged(nameof(CollectLabel));
        OnPropertyChanged(nameof(ToolTip));
    }

    // Runs on whatever thread took the queue's step.
    private void OnQueueChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(IsQueueRunning));
        OnPropertyChanged(nameof(ToolTip));
    }

    [RelayCommand]
    private void Open() => OpenRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void Exit()
    {
        if (Interlocked.Exchange(ref _exitRequested, 1) == 0)
        {
            ExitRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}
