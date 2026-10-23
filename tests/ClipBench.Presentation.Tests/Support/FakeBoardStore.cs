using System.Collections.Immutable;
using ClipBench.Core.Boards;
using ClipBench.Core.Results;

namespace ClipBench.Presentation.Tests.Support;

/// <summary>An in-memory board store that records every call and can be told to fail.</summary>
internal sealed class FakeBoardStore : IBoardStore
{
    /// <summary>What a load returns.</summary>
    public ImmutableArray<Board> Stored { get; set; } = [];

    /// <summary>When set, every call fails with this error.</summary>
    public ResultError? Failure { get; set; }

    public List<Board> Added { get; } = [];

    public List<(BoardId Id, string Name)> Renamed { get; } = [];

    public List<BoardId> Deleted { get; } = [];

    public Task<Result<ImmutableArray<Board>>> LoadAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Failure is { } error ? Result.Failure<ImmutableArray<Board>>(error) : Result.Success(Stored));

    public Task<Result> AddAsync(Board board, CancellationToken cancellationToken)
    {
        Added.Add(board);
        return Outcome();
    }

    public Task<Result> RenameAsync(BoardId id, string name, CancellationToken cancellationToken)
    {
        Renamed.Add((id, name));
        return Outcome();
    }

    public Task<Result> DeleteAsync(BoardId id, CancellationToken cancellationToken)
    {
        Deleted.Add(id);
        return Outcome();
    }

    private Task<Result> Outcome() => Task.FromResult(Failure is { } error ? Result.Failure(error) : Result.Success());
}
