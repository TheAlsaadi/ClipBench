using ClipBench.Core.Results;

namespace ClipBench.Storage;

/// <summary>Errors that more than one part of storage reports, so the user always reads the same words.</summary>
internal static class StorageErrors
{
    /// <summary>The saved history cannot be opened with the key that is available. The caller can offer to start fresh.</summary>
    public static ResultError HistoryUnreadable { get; } =
        new(ErrorCode.HistoryUnreadable, "The saved history can't be decrypted. You can start with an empty history.");
}
