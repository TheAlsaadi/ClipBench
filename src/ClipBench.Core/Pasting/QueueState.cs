namespace ClipBench.Core.Pasting;

/// <summary>Where a paste queue is in its life.</summary>
public enum QueueState
{
    /// <summary>Nothing is loaded.</summary>
    Idle,

    /// <summary>Clips are loaded and the next paste will take the next clip.</summary>
    Armed,

    /// <summary>A clip was handed out and is being pasted.</summary>
    Pasting,

    /// <summary>Every clip was pasted.</summary>
    Exhausted,

    /// <summary>The user stopped the queue before it finished.</summary>
    Cancelled,
}
