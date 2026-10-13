using System.Collections.Immutable;
using ClipBench.Core.Clips;

namespace ClipBench.Core.History;

/// <summary>What happened when a clip was added to the history.</summary>
/// <param name="Clip">The clip as stored. For a duplicate this is the existing clip with the new time and source.</param>
/// <param name="WasDuplicate">True when the same content was already in the history and was moved to the top.</param>
/// <param name="Evicted">Clips the history limits removed because of this add, so storage can delete them.</param>
public sealed record AddResult(Clip Clip, bool WasDuplicate, ImmutableArray<Clip> Evicted);
