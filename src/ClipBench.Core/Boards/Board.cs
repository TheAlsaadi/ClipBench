namespace ClipBench.Core.Boards;

/// <summary>A named group of pinned clips. Boards are shown in the order of <paramref name="Position"/>.</summary>
/// <param name="Id">Identifies the board.</param>
/// <param name="Name">What the user called it.</param>
/// <param name="Position">Where the board comes in the list. Boards are numbered as they are created.</param>
public sealed record Board(BoardId Id, string Name, int Position)
{
    /// <summary>What the user called the board.</summary>
    public string Name
    {
        get;
        init => field = value ?? throw new ArgumentNullException(nameof(value));
    } = Name ?? throw new ArgumentNullException(nameof(Name));

    /// <summary>Printing shows the id only: a name is what the user typed.</summary>
    /// <returns>The id.</returns>
    public override string ToString() => $"Board {Id.Value:N}";
}
