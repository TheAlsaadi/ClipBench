using ClipBench.Core.Clips;
using ClipBench.Core.Diagnostics;
using ClipBench.Core.History;
using ClipBench.Core.Notifications;
using ClipBench.Core.Results;
using ClipBench.Core.Settings;

namespace ClipBench.Core.Capture;

/// <summary>
/// Collect mode: while a session runs, every text copy is appended to one growing clip instead of being saved on its
/// own (unless the keep-copies setting is on). Finishing saves that clip at the top of the history and puts it on the
/// clipboard (ADR 0015). The capture service offers it each accepted copy first. Safe to use from many threads: copies
/// arrive on the capture thread, and the tray, the popup and the shortcut start and finish sessions on the UI thread.
/// </summary>
public sealed class CollectController : ICollectControl, ICaptureRouter
{
    private const string Title = "ClipBench";

    private readonly ClipLibrary _library;
    private readonly IClipboardWriter _writer;
    private readonly SettingsService _settings;
    private readonly TimeProvider _timeProvider;
    private readonly IUserNotifier _notifier;
    private readonly IActivityLog _log;
    private readonly long _maxBytes;
    private readonly Lock _gate = new();
    private CollectSession? _session;
    private bool _limitTold;

    /// <summary>Creates the controller. No session runs until <see cref="Start"/>.</summary>
    /// <param name="library">Where a finished session is saved.</param>
    /// <param name="writer">Puts a finished session on the clipboard.</param>
    /// <param name="settings">The separator, read when a session starts, and the keep-copies setting.</param>
    /// <param name="timeProvider">Stamps the finished clip.</param>
    /// <param name="notifier">Tells the user about the size limit and about failures.</param>
    /// <param name="log">Records what happened, without content.</param>
    /// <param name="maxBytes">The most UTF-8 bytes a session collects.</param>
    public CollectController(
        ClipLibrary library,
        IClipboardWriter writer,
        SettingsService settings,
        TimeProvider timeProvider,
        IUserNotifier notifier,
        IActivityLog log,
        long maxBytes = CollectSession.DefaultMaxBytes)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(notifier);
        ArgumentNullException.ThrowIfNull(log);
        _library = library;
        _writer = writer;
        _settings = settings;
        _timeProvider = timeProvider;
        _notifier = notifier;
        _log = log;
        _maxBytes = maxBytes;
    }

    /// <inheritdoc/>
    public event EventHandler? Changed;

    /// <inheritdoc/>
    public bool IsActive
    {
        get
        {
            lock (_gate)
            {
                return _session is not null;
            }
        }
    }

    /// <inheritdoc/>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _session?.Count ?? 0;
            }
        }
    }

    /// <inheritdoc/>
    public void Start()
    {
        lock (_gate)
        {
            if (_session is not null)
            {
                return;
            }

            // The separator is fixed for the whole session, so changing it in the popup meanwhile changes nothing here.
            _session = new CollectSession(_settings.Current.ResolveSeparator(), _timeProvider, ClipId.New, _maxBytes);
            _session.Start();
            _limitTold = false;
        }

        _log.Write(ActivityLevel.Information, ActivityEvent.CollectStarted);
        RaiseChanged();
    }

    /// <inheritdoc/>
    public bool Offer(Clip clip)
    {
        ArgumentNullException.ThrowIfNull(clip);

        AppendOutcome outcome;
        bool tellAboutLimit = false;
        lock (_gate)
        {
            if (_session is not { } session)
            {
                return true;
            }

            var request = new CaptureRequest { Format = clip.Content.Format, SizeInBytes = clip.Content.SizeInBytes };
            outcome = session.Append(request, clip);
            if (outcome == AppendOutcome.LimitReached && !_limitTold)
            {
                _limitTold = tellAboutLimit = true;
            }
        }

        switch (outcome)
        {
            case AppendOutcome.Appended:
                RaiseChanged();
                return _settings.Current.KeepCopiesWhileCollecting;
            case AppendOutcome.LimitReached when tellAboutLimit:
                _log.Write(ActivityLevel.Warning, ActivityEvent.CollectLimitReached);
                _notifier.Show(Title, "The collected text reached its size limit. Further copies are saved as separate clips.");
                return true;
            default:
                return true;
        }
    }

    /// <inheritdoc/>
    public async Task<Result> FinishAsync(CancellationToken cancellationToken)
    {
        CollectSession? session;
        int count;
        lock (_gate)
        {
            session = _session;
            _session = null;
            count = session?.Count ?? 0;
        }

        if (session is null)
        {
            return Result.Success();
        }

        var clip = session.Finish();
        RaiseChanged();
        if (clip is null)
        {
            return Result.Success();
        }

        var saved = await _library.AddAsync(clip, cancellationToken).ConfigureAwait(false);
        var written = await _writer.WriteAsync(clip.Content, cancellationToken).ConfigureAwait(false);
        if (saved.IsFailure)
        {
            _log.Write(ActivityLevel.Error, ActivityEvent.CollectFailed, new ActivityDetails { Error = saved.Error.Code });
            _notifier.Show(Title, written.IsSuccess
                ? "The collected text is on your clipboard, but it could not be saved in the history."
                : "The collected text could not be saved in the history or put on the clipboard.");
            return Result.Failure(saved.Error);
        }

        if (written.IsFailure)
        {
            _log.Write(ActivityLevel.Warning, ActivityEvent.CollectFailed, new ActivityDetails { Error = written.Error.Code });
            _notifier.Show(Title, "The collected text is saved in your history, but ClipBench could not put it on the clipboard.");
            return Result.Failure(written.Error);
        }

        _log.Write(
            ActivityLevel.Information,
            ActivityEvent.CollectFinished,
            new ActivityDetails { Count = count, Bytes = clip.Content.SizeInBytes });
        return Result.Success();
    }

    /// <inheritdoc/>
    public void Cancel()
    {
        CollectSession? session;
        lock (_gate)
        {
            session = _session;
            _session = null;
        }

        if (session is null)
        {
            return;
        }

        session.Cancel();
        _log.Write(ActivityLevel.Information, ActivityEvent.CollectCancelled);
        RaiseChanged();
    }

    private void RaiseChanged()
    {
        if (Changed is not { } handlers)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList().Cast<EventHandler>())
        {
            try
            {
                handler(this, EventArgs.Empty);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // A subscriber's bug must not stop collecting.
            }
        }
    }
}
