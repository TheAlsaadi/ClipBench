using ClipBench.Core.Clips;

namespace ClipBench.Core.Detection;

/// <summary>
/// Finds out what a clip's text is: a color, a link, an email address, a phone number or code. The whole text has to
/// be exactly that one thing, so a sentence that merely contains a link is not a link. The first reading that fits
/// wins, in the order color, link, email, phone, code. Nothing is fetched, opened or looked up.
/// </summary>
public static class ContentDetector
{
    /// <summary>Looks at a text.</summary>
    /// <param name="text">The text.</param>
    /// <returns>What it is, or <see cref="Detection.None"/>.</returns>
    public static Detection Detect(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (string.IsNullOrWhiteSpace(text))
        {
            return Detection.None;
        }

        if (ColorDetector.TryParse(text) is { } color)
        {
            return new Detection { Kind = DetectedKind.Color, Color = color };
        }

        if (LinkDetector.TryParse(text) is { } address)
        {
            return new Detection { Kind = DetectedKind.Link, Address = address };
        }

        if (EmailDetector.IsEmail(text))
        {
            return new Detection { Kind = DetectedKind.Email };
        }

        if (PhoneDetector.IsPhone(text))
        {
            return new Detection { Kind = DetectedKind.Phone };
        }

        return CodeDetector.Guess(text) is { } language
            ? new Detection { Kind = DetectedKind.Code, Language = language }
            : Detection.None;
    }

    /// <summary>Looks at a clip's content. Pictures and lists of files are never detected: a path is not a link.</summary>
    /// <param name="content">The content.</param>
    /// <returns>What it is, or <see cref="Detection.None"/>.</returns>
    public static Detection Detect(ClipContent content)
    {
        ArgumentNullException.ThrowIfNull(content);

        return content is FilesContent || content.PlainText is not { } text ? Detection.None : Detect(text);
    }
}
