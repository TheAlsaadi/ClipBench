using System.Text;

namespace ClipBench.Core.Clips;

/// <summary>Rich Text Format with a plain text alternative.</summary>
public sealed record RtfContent : ClipContent
{
    /// <summary>Creates RTF content.</summary>
    /// <param name="rtf">The RTF document.</param>
    /// <param name="text">The plain text alternative.</param>
    public RtfContent(string rtf, string text)
    {
        ArgumentNullException.ThrowIfNull(rtf);
        ArgumentNullException.ThrowIfNull(text);
        Rtf = rtf;
        Text = text;
    }

    /// <summary>The RTF document.</summary>
    public string Rtf { get; }

    /// <summary>The plain text alternative.</summary>
    public string Text { get; }

    /// <inheritdoc/>
    public override ClipFormat Format => ClipFormat.Rtf;

    /// <inheritdoc/>
    public override long SizeInBytes => (long)Encoding.UTF8.GetByteCount(Rtf) + Encoding.UTF8.GetByteCount(Text);

    /// <inheritdoc/>
    public override string? PlainText => Text;
}
