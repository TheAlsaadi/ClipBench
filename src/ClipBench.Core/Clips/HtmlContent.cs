using System.Text;

namespace ClipBench.Core.Clips;

/// <summary>HTML with a plain text alternative.</summary>
public sealed record HtmlContent : ClipContent
{
    private readonly long _sizeInBytes;

    /// <summary>Creates HTML content. Half of a surrogate pair becomes the replacement character, as it would when stored.</summary>
    /// <param name="html">The HTML fragment.</param>
    /// <param name="text">The plain text alternative.</param>
    public HtmlContent(string html, string text)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentNullException.ThrowIfNull(text);
        Html = WellFormedText.Repair(html);
        Text = WellFormedText.Repair(text);
        _sizeInBytes = (long)Encoding.UTF8.GetByteCount(Html) + Encoding.UTF8.GetByteCount(Text);
    }

    /// <summary>The HTML fragment.</summary>
    public string Html { get; }

    /// <summary>The plain text alternative.</summary>
    public string Text { get; }

    /// <inheritdoc/>
    public override ClipFormat Format => ClipFormat.Html;

    /// <inheritdoc/>
    public override long SizeInBytes => _sizeInBytes;

    /// <inheritdoc/>
    public override string? PlainText => Text;
}
