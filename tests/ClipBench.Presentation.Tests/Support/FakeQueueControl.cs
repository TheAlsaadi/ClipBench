using ClipBench.Core.Clips;
using ClipBench.Core.Pasting;
using ClipBench.Core.Results;

namespace ClipBench.Presentation.Tests.Support;

/// <summary>A queue that records what it is asked and reports whatever progress the test sets.</summary>
internal sealed class FakeQueueControl : IQueueControl
{
    public event EventHandler? Changed;

    public QueueProgress Progress { get; private set; } = new(QueueState.Idle, 0, 0);

    /// <summary>What a start returns. The default succeeds and arms the queue.</summary>
    public Result StartResult { get; set; } = Result.Success();

    public List<IReadOnlyList<Clip>> Started { get; } = [];

    public int Stops { get; private set; }

    public int SubscriberCount => Changed?.GetInvocationList().Length ?? 0;

    /// <summary>Sets the progress and announces it, as the real queue does after each step.</summary>
    public void Report(QueueState state, int pasted, int count)
    {
        Progress = new QueueProgress(state, pasted, count);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public Task<Result> StartAsync(IReadOnlyList<Clip> clips, CancellationToken cancellationToken)
    {
        Started.Add([.. clips]);
        if (StartResult.IsSuccess)
        {
            Report(QueueState.Armed, 0, clips.Count);
        }

        return Task.FromResult(StartResult);
    }

    /// <summary>Runs at the start of every stop, so a test can look at the state at that moment.</summary>
    public Action? OnStop { get; set; }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        OnStop?.Invoke();
        Stops++;
        Report(QueueState.Cancelled, Progress.Pasted, Progress.Count);
        return Task.CompletedTask;
    }
}
