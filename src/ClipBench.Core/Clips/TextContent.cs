using System.Text;

namespace ClipBench.Core.Clips;

/// <summary>Plain text.</summary>
public sealed record TextContent : ClipContent
{
    private readonly long _sizeInBytes;

    /// <summary>Creates plain text content. Half of a surrogate pair becomes the replacement character, as it would when stored.</summary>
    /// <param name="text">The text.</param>
    public TextContent(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Text = WellFormedText.Repair(text);
        _sizeInBytes = Encoding.UTF8.GetByteCount(Text);
    }

    /// <summary>The text.</summary>
    public string Text { get; }

    /// <inheritdoc/>
    public override ClipFormat Format => ClipFormat.Text;

    /// <inheritdoc/>
    public override long SizeInBytes => _sizeInBytes;

    /// <inheritdoc/>
    public override string? PlainText => Text;
}
