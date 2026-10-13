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

    /// <summary>The paste queue is still pasting a clip and cannot take another step yet.</summary>
    QueuePasteInProgress,

    /// <summary>The paste queue has no paste running that could be completed.</summary>
    QueueNotPasting,

    /// <summary>No clip with the given id is in the history.</summary>
    ClipNotFound,

    /// <summary>The selection contains an image, and images cannot be joined into text.</summary>
    ImageNotJoinable,

    /// <summary>The text to paste would be larger than the allowed size.</summary>
    OutputTooLarge,

    /// <summary>A custom separator is too long or has an unknown escape sequence.</summary>
    InvalidSeparator,
}
