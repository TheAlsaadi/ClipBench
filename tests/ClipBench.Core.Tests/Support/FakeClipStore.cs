using System.Collections.Immutable;
using ClipBench.Core.Clips;
using ClipBench.Core.History;
using ClipBench.Core.Results;

namespace ClipBench.Core.Tests.Support;

/// <summary>An in-memory store that records every call and can be told to fail or to wait.</summary>
internal sealed class FakeClipStore : IClipStore
{
    private int _activeSaves;

    /// <summary>What a load returns.</summary>
    public ImmutableArray<Clip> Stored { get; set; } = [];

    public int Unreadable { get; set; }

    /// <summary>When set, every call fails with this error.</summary>
    public ResultError? Failure { get; set; }

    /// <summary>Runs inside every save, so a test can hold a save open.</summary>
    public Func<Task>? DuringSave { get; set; }

    public List<Clip> Saved { get; } = [];

    public List<(ClipId Id, bool Pinned)> PinChanges { get; } = [];

    public List<ClipId> Deleted { get; } = [];

    /// <summary>Every call to <see cref="SetBoardsAsync"/>, in order.</summary>
    public List<IReadOnlyList<ClipPlacement>> BoardChanges { get; } = [];

    public int MaxConcurrentSaves { get; private set; }

    public Task<Result<StoredClips>> LoadAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Failure is { } error
            ? Result.Failure<StoredClips>(error)
            : Result.Success(new StoredClips(Stored, Unreadable)));

    public async Task<Result> SaveAsync(Clip clip, CancellationToken cancellationToken)
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
                Saved.Add(clip);
            }

            return Failure is { } error ? Result.Failure(error) : Result.Success();
        }
        finally
        {
            Interlocked.Decrement(ref _activeSaves);
        }
    }

    public Task<Result> SetPinnedAsync(ClipId id, bool pinned, CancellationToken cancellationToken)
    {
        PinChanges.Add((id, pinned));
        return Task.FromResult(Failure is { } error ? Result.Failure(error) : Result.Success());
    }

    public Task<Result> SetBoardsAsync(IReadOnlyList<ClipPlacement> placements, CancellationToken cancellationToken)
    {
        BoardChanges.Add(placements);
        return Task.FromResult(Failure is { } error ? Result.Failure(error) : Result.Success());
    }

    public Task<Result> DeleteAsync(IReadOnlyCollection<ClipId> ids, CancellationToken cancellationToken)
    {
        Deleted.AddRange(ids);
        return Task.FromResult(Failure is { } error ? Result.Failure(error) : Result.Success());
    }
}
