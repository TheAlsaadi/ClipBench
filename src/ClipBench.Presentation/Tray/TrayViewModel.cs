using ClipBench.Core.Capture;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClipBench.Presentation.Tray;

/// <summary>What the tray icon shows and what its menu does.</summary>
public sealed partial class TrayViewModel : ObservableObject
{
    private readonly ICaptureControl _capture;
    private int _exitRequested;

    /// <summary>Creates the view model.</summary>
    /// <param name="capture">The part of capture that can be paused.</param>
    public TrayViewModel(ICaptureControl capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        _capture = capture;
        IsPaused = capture.IsPaused;
    }

    /// <summary>Raised when the menu asks for the popup.</summary>
    public event EventHandler? OpenRequested;

    /// <summary>Raised once when the menu asks to quit.</summary>
    public event EventHandler? ExitRequested;

    /// <summary>True while capture is paused.</summary>
    [ObservableProperty]
    public partial bool IsPaused { get; set; }

    /// <summary>The label of the pause item: what choosing it does.</summary>
    public string PauseLabel => IsPaused ? "Resume capturing" : "Pause capturing";

    /// <summary>The text shown when the pointer rests on the icon.</summary>
    public string ToolTip => IsPaused ? "ClipBench · paused" : "ClipBench · capturing";

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
