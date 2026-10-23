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

    /// <summary>Stored data failed its integrity check: it was altered, or it belongs to another key or place.</summary>
    DecryptionFailed,

    /// <summary>A new encryption key could not be protected or stored, so there is nothing to lose by trying again.</summary>
    KeyProtectionFailed,

    /// <summary>The saved history cannot be decrypted because its key is missing, damaged or not this user's. The caller can offer to start fresh.</summary>
    HistoryUnreadable,

    /// <summary>The disk or the database refused an operation. The data on disk is unchanged unless the operation says otherwise.</summary>
    StorageFailure,

    /// <summary>A clip is larger than storage accepts, so it was not saved.</summary>
    ContentTooLarge,

    /// <summary>The saved history was written by a newer version of the app. It is left untouched, and starting fresh would destroy it.</summary>
    HistoryTooNew,

    /// <summary>The clipboard holds nothing ClipBench keeps: it is empty, or it holds only formats ClipBench does not read.</summary>
    ClipboardEmpty,

    /// <summary>Another program kept the clipboard open for the whole time ClipBench was willing to wait. Trying again later may work.</summary>
    ClipboardBusy,

    /// <summary>The clipboard content is damaged, larger than the safety limit, or in a form ClipBench cannot read.</summary>
    ClipboardUnreadable,

    /// <summary>Windows refused to take the content that ClipBench tried to put on the clipboard.</summary>
    ClipboardWriteFailed,

    /// <summary>A keyboard shortcut is not valid: unknown key, no key, or a key that would stop people typing.</summary>
    HotkeyInvalid,

    /// <summary>Another program already uses the keyboard shortcut, or Windows reserves it.</summary>
    HotkeyUnavailable,

    /// <summary>The keyboard shortcut is not registered, so there is nothing to release.</summary>
    HotkeyNotRegistered,

    /// <summary>The window to paste into no longer exists.</summary>
    TargetNotFound,

    /// <summary>The window to paste into runs with more rights than ClipBench, so Windows would block the keystrokes. ClipBench never elevates.</summary>
    TargetElevated,

    /// <summary>Focus could not be given back to the window to paste into, so no keystrokes were sent.</summary>
    FocusNotRestored,

    /// <summary>Another program changed the clipboard between ClipBench writing it and pasting, so the paste was cancelled.</summary>
    ClipboardChangedBeforePaste,

    /// <summary>Windows did not accept the keystrokes.</summary>
    InputBlocked,

    /// <summary>A Windows service ClipBench needs (a window, a message loop, a lock) could not be set up.</summary>
    PlatformFailure,

    /// <summary>The text given to a transform is not in the form the transform needs, such as JSON that does not parse.</summary>
    TransformInputInvalid,

    /// <summary>A find-and-replace pattern is not valid, or is too long.</summary>
    PatternInvalid,

    /// <summary>A find-and-replace pattern took too long to match, so the paste was not changed.</summary>
    PatternTimedOut,

    /// <summary>No board with the given id exists.</summary>
    BoardNotFound,

    /// <summary>A board name is empty, too long or holds control characters.</summary>
    InvalidBoardName,

    /// <summary>Another board already has that name, ignoring capital letters.</summary>
    BoardNameTaken,

    /// <summary>The most boards that can exist already do.</summary>
    TooManyBoards,

    /// <summary>The clip is not on a board, so there is no place in a board to move it from.</summary>
    ClipNotOnBoard,

    /// <summary>A snippet is too long, or asks for more values than the limit.</summary>
    InvalidSnippet,

    /// <summary>A snippet asks for a value that was not given.</summary>
    SnippetInputMissing,

    /// <summary>A value given to a snippet is longer than the limit.</summary>
    SnippetInputTooLong,

    /// <summary>No snippet with the given id exists.</summary>
    SnippetNotFound,

    /// <summary>A snippet name is empty, too long or holds control characters.</summary>
    InvalidSnippetName,

    /// <summary>Another snippet already has that name, ignoring capital letters.</summary>
    SnippetNameTaken,

    /// <summary>The most snippets that can exist already do.</summary>
    TooManySnippets,
}
