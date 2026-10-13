using System.Text;

namespace ClipBench.Core.Clips;

/// <summary>Plain text.</summary>
public sealed record TextContent : ClipContent
{
    private readonly long _sizeInBytes;

    /// <summary>Creates plain text content.</summary>
    /// <param name="text">The text.</param>
    public TextContent(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Text = text;
        _sizeInBytes = Encoding.UTF8.GetByteCount(text);
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
