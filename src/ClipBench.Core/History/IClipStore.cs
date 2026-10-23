using System.Collections.Immutable;
using ClipBench.Core.Boards;
using ClipBench.Core.Clips;
using ClipBench.Core.Results;

namespace ClipBench.Core.History;

/// <summary>Where the history is kept between runs. The storage library implements it over the encrypted database.</summary>
public interface IClipStore
{
    /// <summary>Reads every stored clip that has not expired, newest first.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The clips and how many records could not be read, or why the store could not be read.</returns>
    Task<Result<StoredClips>> LoadAsync(CancellationToken cancellationToken);

    /// <summary>Saves a clip, replacing the stored clip with the same id.</summary>
    /// <param name="clip">The clip as it should now be stored, pin included.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Success or why the clip was not saved.</returns>
    Task<Result> SaveAsync(Clip clip, CancellationToken cancellationToken);

    /// <summary>Pins or unpins a stored clip. Unpinning also takes the clip off its board, because a board holds pinned clips only.</summary>
    /// <param name="id">The clip id.</param>
    /// <param name="pinned">True to pin.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Success or why the change was not stored.</returns>
    Task<Result> SetPinnedAsync(ClipId id, bool pinned, CancellationToken cancellationToken);

    /// <summary>Puts stored clips on boards, moves them within a board or takes them off, all in one step.</summary>
    /// <param name="placements">Where each clip now is. Ids that are not stored are ignored.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Success or why the changes were not stored.</returns>
    Task<Result> SetBoardsAsync(IReadOnlyList<ClipPlacement> placements, CancellationToken cancellationToken);

    /// <summary>Deletes clips for good. Ids that are not stored are ignored.</summary>
    /// <param name="ids">The clips to delete.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Success or why the clips were not deleted.</returns>
    Task<Result> DeleteAsync(IReadOnlyCollection<ClipId> ids, CancellationToken cancellationToken);
}

/// <summary>What a load of the store found.</summary>
/// <param name="Clips">The clips that were read, newest first.</param>
/// <param name="UnreadableCount">How many stored records were skipped because they failed their integrity check.</param>
public sealed record StoredClips(ImmutableArray<Clip> Clips, int UnreadableCount);

/// <summary>Where a clip is on the boards.</summary>
/// <param name="Id">The clip.</param>
/// <param name="Board">The board it is on, or null when it is on none.</param>
/// <param name="Position">Where it comes in the board, counting from 0.</param>
public readonly record struct ClipPlacement(ClipId Id, BoardId? Board, int Position);
