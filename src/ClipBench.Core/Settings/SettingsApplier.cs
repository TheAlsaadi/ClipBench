using ClipBench.Core.Capture;
using ClipBench.Core.Diagnostics;
using ClipBench.Core.History;

namespace ClipBench.Core.Settings;

/// <summary>
/// Carries the settings into the parts of the app that work from them, now and whenever they change. A change is
/// applied in the order it was made, on a background task that never throws; <see cref="WhenIdleAsync"/> waits for it.
/// </summary>
public sealed class SettingsApplier : IDisposable
{
    private readonly SettingsService _settings;
    private readonly ClipLibrary _library;
    private readonly CaptureService _capture;
    private readonly IActivityLog _log;
    private readonly Lock _gate = new();
    private Task _pending = Task.CompletedTask;
    private HistoryOptions? _appliedHistory;
    private bool _disposed;

    /// <summary>Applies the settings now in force and starts listening for changes.</summary>
    /// <param name="settings">The settings in force.</param>
    /// <param name="library">The history, which keeps to the limits the settings ask for.</param>
    /// <param name="capture">The clipboard capture, which skips the programs the settings exclude.</param>
    /// <param name="log">Where a failure is recorded. It never receives clip content.</param>
    public SettingsApplier(SettingsService settings, ClipLibrary library, CaptureService capture, IActivityLog log)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(log);
        _settings = settings;
        _library = library;
        _capture = capture;
        _log = log;
        _settings.Changed += OnChanged;
        Apply();
    }

    /// <summary>Waits until every change made so far has been applied.</summary>
    /// <returns>A task that completes when the applier has nothing left to do.</returns>
    public Task WhenIdleAsync()
    {
        lock (_gate)
        {
            return _pending;
        }
    }

    /// <summary>Stops listening. A change that is already being applied finishes.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _settings.Changed -= OnChanged;
    }

    private void OnChanged(object? sender, EventArgs e) => Apply();

    private void Apply()
    {
        var current = _settings.Current;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            var excluded = current.ExcludedPrograms.Names;
            _capture.UpdateFilter(filter => filter with { ExcludedProcesses = excluded });

            _library.SensitiveExpiry = TimeSpan.FromSeconds(AppSettings.SensitiveExpirySecondsRange.Clamp(current.SensitiveExpirySeconds));

            var history = current.ToHistoryOptions();
            if (history == _appliedHistory)
            {
                return;
            }

            _appliedHistory = history;
            _pending = ApplyAfterAsync(_pending, history);
        }
    }

    private async Task ApplyAfterAsync(Task previous, HistoryOptions history)
    {
        try
        {
            await previous.ConfigureAwait(false);
            var result = await _library.ApplyLimitsAsync(history, CancellationToken.None).ConfigureAwait(false);
            if (result.IsFailure)
            {
                _log.Write(ActivityLevel.Warning, ActivityEvent.StoreFailed, new ActivityDetails { Error = result.Error.Code });
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Applying a setting must never take the app down; the next change tries again.
            _log.Write(ActivityLevel.Warning, ActivityEvent.StoreFailed, new ActivityDetails { ExceptionType = exception.GetType() });
        }
    }
}
