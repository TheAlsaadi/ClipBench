using System.Text;

namespace ClipBench.Core.Clips;

/// <summary>Plain text.</summary>
public sealed record TextContent : ClipContent
{
    /// <summary>Creates plain text content.</summary>
    /// <param name="text">The text.</param>
    public TextContent(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Text = text;
    }

    /// <summary>The text.</summary>
    public string Text { get; }

    /// <inheritdoc/>
    public override ClipFormat Format => ClipFormat.Text;

    /// <inheritdoc/>
    public override long SizeInBytes => Encoding.UTF8.GetByteCount(Text);

    /// <inheritdoc/>
    public override string? PlainText => Text;
}
