namespace ClipBench.Core.Capture;

/// <summary>The part of capture that the tray menu controls: pausing and resuming.</summary>
public interface ICaptureControl
{
    /// <summary>True while capture is paused: the filter then rejects every change before any content is read.</summary>
    bool IsPaused { get; }

    /// <summary>Pauses or resumes recording.</summary>
    /// <param name="paused">True to pause.</param>
    void SetPaused(bool paused);
}
