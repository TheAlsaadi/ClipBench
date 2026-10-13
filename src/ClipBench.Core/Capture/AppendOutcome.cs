namespace ClipBench.Core.Capture;

/// <summary>What happened to a capture offered to a collect session.</summary>
public enum AppendOutcome
{
    /// <summary>The capture was added to the session.</summary>
    Appended,

    /// <summary>No session is running, so nothing was collected.</summary>
    NotCollecting,

    /// <summary>ClipBench wrote this content itself, so it was ignored.</summary>
    OwnWrite,

    /// <summary>The capture has no text (an image), so it cannot be collected.</summary>
    NotText,

    /// <summary>Adding the capture would pass the size limit, so it was left out.</summary>
    LimitReached,
}
