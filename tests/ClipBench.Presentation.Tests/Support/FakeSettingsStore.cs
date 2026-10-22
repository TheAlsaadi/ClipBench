using ClipBench.Core.Results;
using ClipBench.Core.Settings;

namespace ClipBench.Presentation.Tests.Support;

/// <summary>An in-memory settings store that records every save and can be told to fail.</summary>
internal sealed class FakeSettingsStore : ISettingsStore
{
    /// <summary>What a load returns.</summary>
    public SettingsLoad Load { get; set; } = new(AppSettings.Default, SettingsLoadStatus.Missing);

    /// <summary>When set, every save fails with this error.</summary>
    public ResultError? Failure { get; set; }

    public List<AppSettings> Saved { get; } = [];

    public Task<SettingsLoad> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(Load);

    public Task<Result> SaveAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        Saved.Add(settings);
        return Task.FromResult(Failure is { } error ? Result.Failure(error) : Result.Success());
    }
}
