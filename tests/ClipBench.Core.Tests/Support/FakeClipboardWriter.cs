using ClipBench.Core.Capture;
using ClipBench.Core.Clips;
using ClipBench.Core.Results;

namespace ClipBench.Core.Tests.Support;

/// <summary>Records what is put on the clipboard and can be told to fail.</summary>
internal sealed class FakeClipboardWriter : IClipboardWriter
{
    public List<ClipContent> Written { get; } = [];

    /// <summary>When set, every write fails with this error.</summary>
    public ResultError? Failure { get; set; }

    public Task<Result<long>> WriteAsync(ClipContent content, CancellationToken cancellationToken)
    {
        Written.Add(content);
        return Task.FromResult(Failure is { } error ? Result.Failure<long>(error) : Result.Success(42L));
    }
}
