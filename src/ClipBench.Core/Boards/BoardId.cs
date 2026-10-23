namespace ClipBench.Core.Boards;

/// <summary>Identifies one board. Wraps a GUID so ids cannot be confused with other values.</summary>
/// <param name="Value">The underlying GUID.</param>
public readonly record struct BoardId(Guid Value)
{
    /// <summary>Creates a new, unique, time-ordered id.</summary>
    public static BoardId New() => new(Guid.CreateVersion7());
}
