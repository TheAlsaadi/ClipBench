using System.Text;

namespace ClipBench.Core.Clips;

/// <summary>Rich Text Format with a plain text alternative.</summary>
public sealed record RtfContent : ClipContent
{
    private readonly long _sizeInBytes;

    /// <summary>Creates RTF content. Half of a surrogate pair becomes the replacement character, as it would when stored.</summary>
    /// <param name="rtf">The RTF document.</param>
    /// <param name="text">The plain text alternative.</param>
    public RtfContent(string rtf, string text)
    {
        ArgumentNullException.ThrowIfNull(rtf);
        ArgumentNullException.ThrowIfNull(text);
        Rtf = WellFormedText.Repair(rtf);
        Text = WellFormedText.Repair(text);
        _sizeInBytes = (long)Encoding.UTF8.GetByteCount(Rtf) + Encoding.UTF8.GetByteCount(Text);
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
