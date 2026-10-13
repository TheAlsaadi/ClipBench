using ClipBench.Core.Clips;

namespace ClipBench.Storage.Clips;

/// <summary>
/// What a list needs to show a clip, read without decrypting the content: a large image is not opened
/// to draw a row. Printing it shows no preview and no source.
/// </summary>
/// <param name="Id">The clip id.</param>
/// <param name="Format">The kind of content.</param>
/// <param name="CapturedAt">When it was copied.</param>
/// <param name="IsPinned">True when the history limits must not remove it.</param>
/// <param name="SizeInBytes">The size of the content in bytes.</param>
/// <param name="Preview">A short single-line text, or an empty string for an image.</param>
/// <param name="SourceApp">The application it was copied from, when known.</param>
public sealed record ClipSummary(
    ClipId Id,
    ClipFormat Format,
    DateTimeOffset CapturedAt,
    bool IsPinned,
    long SizeInBytes,
    string Preview,
    string? SourceApp)
{
    /// <summary>Never shows the preview or the source: summaries end up in logs by accident.</summary>
    /// <returns>The id, kind and size.</returns>
    public override string ToString() => $"ClipSummary {Id.Value:N} ({Format}, {SizeInBytes} bytes)";
}
