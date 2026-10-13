using System.Text;

namespace ClipBench.Core.Clips;

/// <summary>HTML with a plain text alternative.</summary>
public sealed record HtmlContent : ClipContent
{
    private readonly long _sizeInBytes;

    /// <summary>Creates HTML content.</summary>
    /// <param name="html">The HTML fragment.</param>
    /// <param name="text">The plain text alternative.</param>
    public HtmlContent(string html, string text)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentNullException.ThrowIfNull(text);
        Html = html;
        Text = text;
        _sizeInBytes = (long)Encoding.UTF8.GetByteCount(html) + Encoding.UTF8.GetByteCount(text);
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
