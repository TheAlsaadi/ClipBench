using System.Collections.Immutable;

namespace ClipBench.Storage.Clips;

/// <summary>
/// What a load found. A stored value that failed its integrity check, or could not be read, is skipped and
/// counted: it is never returned, and one bad record never hides the others.
/// </summary>
/// <typeparam name="T">The kind of item loaded.</typeparam>
/// <param name="Items">The items that were read, newest first.</param>
/// <param name="UnreadableCount">How many stored records were skipped.</param>
public sealed record LoadedItems<T>(ImmutableArray<T> Items, int UnreadableCount);
