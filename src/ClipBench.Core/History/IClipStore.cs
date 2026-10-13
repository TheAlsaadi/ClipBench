using System.Collections.Immutable;
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

    /// <summary>Pins or unpins a stored clip.</summary>
    /// <param name="id">The clip id.</param>
    /// <param name="pinned">True to pin.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Success or why the change was not stored.</returns>
    Task<Result> SetPinnedAsync(ClipId id, bool pinned, CancellationToken cancellationToken);

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
