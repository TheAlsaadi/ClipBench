using ClipBench.Core.Results;
using ClipBench.Core.Settings;

namespace ClipBench.Core.Tests.Support;

/// <summary>An in-memory settings store that records every save and can be told to fail or to wait.</summary>
internal sealed class FakeSettingsStore : ISettingsStore
{
    private int _activeSaves;

    /// <summary>What a load returns.</summary>
    public SettingsLoad Load { get; set; } = new(AppSettings.Default, SettingsLoadStatus.Missing);

    /// <summary>When set, every save fails with this error.</summary>
    public ResultError? Failure { get; set; }

    /// <summary>Runs inside every save, so a test can hold a save open.</summary>
    public Func<Task>? DuringSave { get; set; }

    public List<AppSettings> Saved { get; } = [];

    public int MaxConcurrentSaves { get; private set; }

    public Task<SettingsLoad> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(Load);

    public async Task<Result> SaveAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        var active = Interlocked.Increment(ref _activeSaves);
        MaxConcurrentSaves = Math.Max(MaxConcurrentSaves, active);
        try
        {
            if (DuringSave is { } hold)
            {
                await hold();
            }

            lock (Saved)
            {
                Saved.Add(settings);
            }

            return Failure is { } error ? Result.Failure(error) : Result.Success();
        }
        finally
        {
            Interlocked.Decrement(ref _activeSaves);
        }
    }
}
