using ClipBench.Core.Capture;
using ClipBench.Core.Results;

namespace ClipBench.Presentation.Tests.Support;

/// <summary>A collect session held in memory, with the outcome of a finish set by the test.</summary>
internal sealed class FakeCollectControl : ICollectControl
{
    public event EventHandler? Changed;

    public bool IsActive { get; private set; }

    public int Count { get; private set; }

    /// <summary>What a finish returns. The default succeeds.</summary>
    public Result FinishResult { get; set; } = Result.Success();

    public int Starts { get; private set; }

    public int Finishes { get; private set; }

    public int Cancels { get; private set; }

    public int SubscriberCount => Changed?.GetInvocationList().Length ?? 0;

    /// <summary>Pretends a copy was collected.</summary>
    public void Collected()
    {
        Count++;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Start()
    {
        Starts++;
        IsActive = true;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public Task<Result> FinishAsync(CancellationToken cancellationToken)
    {
        Finishes++;
        IsActive = false;
        Count = 0;
        Changed?.Invoke(this, EventArgs.Empty);
        return Task.FromResult(FinishResult);
    }

    public void Cancel()
    {
        Cancels++;
        IsActive = false;
        Count = 0;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
