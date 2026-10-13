using System.Text;

namespace ClipBench.Core.Clips;

/// <summary>Rich Text Format with a plain text alternative.</summary>
public sealed record RtfContent : ClipContent
{
    private readonly long _sizeInBytes;

    /// <summary>Creates RTF content.</summary>
    /// <param name="rtf">The RTF document.</param>
    /// <param name="text">The plain text alternative.</param>
    public RtfContent(string rtf, string text)
    {
        ArgumentNullException.ThrowIfNull(rtf);
        ArgumentNullException.ThrowIfNull(text);
        Rtf = rtf;
        Text = text;
        _sizeInBytes = (long)Encoding.UTF8.GetByteCount(rtf) + Encoding.UTF8.GetByteCount(text);
    }

    /// <summary>The RTF document.</summary>
    public string Rtf { get; }

    /// <summary>The plain text alternative.</summary>
    public string Text { get; }

    /// <inheritdoc/>
    public override ClipFormat Format => ClipFormat.Rtf;

    /// <inheritdoc/>
    public override long SizeInBytes => _sizeInBytes;

    /// <inheritdoc/>
    public override string? PlainText => Text;
}
