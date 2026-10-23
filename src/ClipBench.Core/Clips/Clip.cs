namespace ClipBench.Core.Clips;

/// <summary>One captured piece of clipboard content with when and where it was copied. Immutable.</summary>
/// <param name="Id">Identifies the clip.</param>
/// <param name="Content">What was copied.</param>
/// <param name="CapturedAt">When it was copied.</param>
/// <param name="SourceApp">The application it was copied from, when known.</param>
/// <param name="IsPinned">True when the history limits must not remove it.</param>
public sealed record Clip(ClipId Id, ClipContent Content, DateTimeOffset CapturedAt, string? SourceApp = null, bool IsPinned = false)
{
    /// <summary>What was copied.</summary>
    public ClipContent Content
    {
        get;
        init => field = value ?? throw new ArgumentNullException(nameof(value));
    } = Content ?? throw new ArgumentNullException(nameof(Content));

    /// <summary>The board the clip is on, or null. A clip on a board is always pinned.</summary>
    public Boards.BoardId? OnBoard { get; init; }

    /// <summary>Where the clip comes in its board, counting from 0. Only meaningful while <see cref="OnBoard"/> is set.</summary>
    public int BoardPosition { get; init; }

    /// <summary>The size of the content in bytes.</summary>
    public long SizeInBytes => Content.SizeInBytes;

    /// <summary>Printing never includes the content or the source app: clips end up in logs by accident.</summary>
    /// <returns>The id, kind and size of the clip.</returns>
    public override string ToString() => $"Clip {Id.Value:N} ({Content.Format}, {Content.SizeInBytes} bytes)";
}
