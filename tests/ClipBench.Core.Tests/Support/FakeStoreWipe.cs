using ClipBench.Core.History;
using ClipBench.Core.Results;

namespace ClipBench.Core.Tests.Support;

/// <summary>A store wipe that counts its calls and can be told to fail or to wait.</summary>
internal sealed class FakeStoreWipe : IStoreWipe
{
    /// <summary>When set, the wipe fails with this error.</summary>
    public ResultError? Failure { get; set; }

    /// <summary>Runs inside the wipe, so a test can hold it open.</summary>
    public Func<Task>? DuringWipe { get; set; }

    public int Wipes { get; private set; }

    public async Task<Result> WipeAsync(CancellationToken cancellationToken)
    {
        Wipes++;
        if (DuringWipe is { } hold)
        {
            await hold();
        }

        return Failure is { } error ? Result.Failure(error) : Result.Success();
    }
}
