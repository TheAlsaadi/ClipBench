namespace ClipBench.Core.Clips;

/// <summary>Identifies one clip. Wraps a GUID so ids cannot be confused with other values.</summary>
/// <param name="Value">The underlying GUID.</param>
public readonly record struct ClipId(Guid Value)
{
    /// <summary>Creates a new, unique, time-ordered id.</summary>
    public static ClipId New() => new(Guid.CreateVersion7());
}
