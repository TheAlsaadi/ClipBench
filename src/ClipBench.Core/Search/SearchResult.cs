using ClipBench.Core.Clips;

namespace ClipBench.Core.Search;

/// <summary>A clip that matched a search, with how well it matched.</summary>
/// <param name="Clip">The matching clip.</param>
/// <param name="Score">Higher is better. Zero means the query was empty and nothing was ranked.</param>
public readonly record struct SearchResult(Clip Clip, int Score);
