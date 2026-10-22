using ClipBench.Core.Capture;
using ClipBench.Core.Clips;
using ClipBench.Core.Results;

namespace ClipBench.Core.Diagnostics;

/// <summary>How serious an activity is.</summary>
public enum ActivityLevel
{
    /// <summary>Something that normally happens.</summary>
    Information,

    /// <summary>Something went wrong that the app recovered from.</summary>
    Warning,

    /// <summary>Something went wrong and a feature did not work.</summary>
    Error,
}

/// <summary>What happened. Each value is a fixed event: the log never gets a free-form message.</summary>
public enum ActivityEvent
{
    /// <summary>The clipboard listener started.</summary>
    CaptureListening,

    /// <summary>The clipboard listener could not start.</summary>
    CaptureListenFailed,

    /// <summary>A clipboard change was recorded.</summary>
    CaptureStored,

    /// <summary>A clipboard change was not recorded because of a rule.</summary>
    CaptureRejected,

    /// <summary>The clipboard could not be read.</summary>
    CaptureFailed,

    /// <summary>Handling a clipboard change threw an exception that was not expected.</summary>
    CaptureFaulted,

    /// <summary>A change could not be saved to the history database.</summary>
    StoreFailed,

    /// <summary>A clip was pasted into another program.</summary>
    PasteSucceeded,

    /// <summary>A paste did not happen, with the reason as an error code.</summary>
    PasteFailed,

    /// <summary>A paste threw an exception that was not expected.</summary>
    PasteFaulted,

    /// <summary>The app started.</summary>
    AppStarted,

    /// <summary>The app is closing.</summary>
    AppStopping,

    /// <summary>The saved history was read; the count says how many clips.</summary>
    HistoryLoaded,

    /// <summary>Some saved clips could not be read and were skipped; the count says how many.</summary>
    HistorySkipped,

    /// <summary>The saved history could not be used and the user chose to start again.</summary>
    HistoryReset,

    /// <summary>The popup shortcut is registered.</summary>
    HotkeyRegistered,

    /// <summary>The popup shortcut could not be registered, with the reason as an error code.</summary>
    HotkeyFailed,

    /// <summary>The tray icon could not be shown, with the reason as an error code.</summary>
    TrayFailed,

    /// <summary>The saved settings were damaged and were set aside; the defaults are in use.</summary>
    SettingsDamaged,

    /// <summary>The saved settings could not be read; the defaults are in use and the file was left alone.</summary>
    SettingsUnreadable,
}

/// <summary>
/// The facts that can accompany an activity. Every field is a number, a code or a type: there is no string, so
/// clip text, file names and program names cannot reach a log through it.
/// </summary>
public readonly record struct ActivityDetails
{
    /// <summary>The error code, when the activity is a failure.</summary>
    public ErrorCode Error { get; init; }

    /// <summary>The kind of clip involved, when there is one.</summary>
    public ClipFormat? Format { get; init; }

    /// <summary>The size of the clip involved, in bytes.</summary>
    public long? Bytes { get; init; }

    /// <summary>How many of something: clips loaded, records skipped.</summary>
    public int? Count { get; init; }

    /// <summary>Why a change was rejected.</summary>
    public CaptureRejection Rejection { get; init; }

    /// <summary>The type of an unexpected exception. Its message is never recorded: messages can quote content.</summary>
    public Type? ExceptionType { get; init; }
}

/// <summary>Records what the app does, without ever recording what the user copied.</summary>
public interface IActivityLog
{
    /// <summary>Writes one activity.</summary>
    /// <param name="level">How serious it is.</param>
    /// <param name="activity">What happened.</param>
    /// <param name="details">The facts about it.</param>
    void Write(ActivityLevel level, ActivityEvent activity, ActivityDetails details = default);
}
