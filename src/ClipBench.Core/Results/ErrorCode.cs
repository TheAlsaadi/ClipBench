namespace ClipBench.Core.Results;

/// <summary>Reasons an operation can fail for an expected, non-bug reason.</summary>
public enum ErrorCode
{
    /// <summary>The default value. A result that was never initialised carries this code.</summary>
    Unspecified = 0,

    /// <summary>No clips were given to an operation that needs at least one.</summary>
    NothingSelected,

    /// <summary>The paste queue is not armed, so there is nothing to paste.</summary>
    QueueNotArmed,
}
