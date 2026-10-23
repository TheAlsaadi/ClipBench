using System.Collections.Immutable;
using ClipBench.Core.Results;
using ClipBench.Core.Snippets;

namespace ClipBench.Presentation.Tests.Support;

/// <summary>An in-memory snippet store that records every call and can be told to fail.</summary>
internal sealed class FakeSnippetStore : ISnippetStore
{
    /// <summary>What a load returns.</summary>
    public ImmutableArray<Snippet> Stored { get; set; } = [];

    /// <summary>When set, every call fails with this error.</summary>
    public ResultError? Failure { get; set; }

    public List<Snippet> Saved { get; } = [];

    public List<SnippetId> Deleted { get; } = [];

    public Task<Result<ImmutableArray<Snippet>>> LoadAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Failure is { } error ? Result.Failure<ImmutableArray<Snippet>>(error) : Result.Success(Stored));

    public Task<Result> SaveAsync(Snippet snippet, CancellationToken cancellationToken)
    {
        Saved.Add(snippet);
        return Outcome();
    }

    public Task<Result> DeleteAsync(SnippetId id, CancellationToken cancellationToken)
    {
        Deleted.Add(id);
        return Outcome();
    }

    private Task<Result> Outcome() => Task.FromResult(Failure is { } error ? Result.Failure(error) : Result.Success());
}
