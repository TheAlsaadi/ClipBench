namespace ClipBench.Core.Privacy;

/// <summary>
/// Finds JSON web tokens: three base64url parts separated by dots, the first one a JSON header
/// (so it starts with <c>eyJ</c>). Linear in the text length.
/// </summary>
internal static class JwtScanner
{
    private const string HeaderStart = "eyJ";
    private const int MinHeaderLength = 10;
    private const int MinPayloadLength = 2;
    private const int MinSignatureLength = 8;

    public static bool Contains(ReadOnlySpan<char> text)
    {
        var offset = 0;
        while (offset < text.Length)
        {
            var found = text[offset..].IndexOf(HeaderStart, StringComparison.Ordinal);
            if (found < 0)
            {
                return false;
            }

            var start = offset + found;
            var headerEnd = EndOfPart(text, start);

            // Continue after the part just read: another "eyJ" inside it cannot start a token, because it
            // would be glued to the characters before it. This keeps the whole scan linear.
            offset = Math.Max(start + HeaderStart.Length, headerEnd);

            if (start > 0 && IsBase64Url(text[start - 1]))
            {
                continue;
            }

            if (headerEnd - start >= MinHeaderLength && IsTokenFrom(text, headerEnd))
            {
                return true;
            }
        }

        return false;
    }

    // headerEnd points at the dot after the header, if there is one.
    private static bool IsTokenFrom(ReadOnlySpan<char> text, int headerEnd)
    {
        if (headerEnd >= text.Length || text[headerEnd] != '.')
        {
            return false;
        }

        var payloadStart = headerEnd + 1;
        var payloadEnd = EndOfPart(text, payloadStart);
        if (payloadEnd - payloadStart < MinPayloadLength || payloadEnd >= text.Length || text[payloadEnd] != '.')
        {
            return false;
        }

        var signatureStart = payloadEnd + 1;
        return EndOfPart(text, signatureStart) - signatureStart >= MinSignatureLength;
    }

    private static int EndOfPart(ReadOnlySpan<char> text, int start)
    {
        var end = start;
        while (end < text.Length && IsBase64Url(text[end]))
        {
            end++;
        }

        return end;
    }

    private static bool IsBase64Url(char c) => Ascii.IsLetterOrDigit(c) || c is '-' or '_';
}
