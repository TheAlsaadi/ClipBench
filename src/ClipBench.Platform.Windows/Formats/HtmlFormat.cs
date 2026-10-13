using System.Globalization;
using System.Text;
using ClipBench.Core.Results;

namespace ClipBench.Platform.Windows.Formats;

/// <summary>
/// Reads and writes the "HTML Format" clipboard data (CF_HTML): a short ASCII header of byte offsets,
/// then an HTML page that marks the copied part. Only the copied fragment is kept.
/// </summary>
internal static class HtmlFormat
{
    // The header is five short lines. Anything that looks like more is not a header, so a hostile
    // document of header-like lines is never scanned beyond this window.
    private const int MaxHeaderBytes = 1024;
    private const int MaxHeaderLines = 32;
    private const int MaxFieldNameLength = 32;

    private const string Preamble = "<html>\r\n<body>\r\n<!--StartFragment-->";
    private const string Postamble = "<!--EndFragment-->\r\n</body>\r\n</html>";

    /// <summary>Reads the copied fragment out of CF_HTML data.</summary>
    /// <param name="data">The data as it is on the clipboard (UTF-8, usually ending with a NUL).</param>
    /// <param name="maxBytes">The most bytes to accept. Larger data is refused without being read.</param>
    /// <returns>The fragment, or <see cref="ErrorCode.ClipboardUnreadable"/> when the header or its offsets are not valid.</returns>
    public static Result<string> ExtractFragment(ReadOnlySpan<byte> data, int maxBytes)
    {
        if (data.Length > maxBytes)
        {
            return Unreadable("The HTML on the clipboard is too large to keep.");
        }

        data = data.TrimEnd((byte)0);
        if (!TryReadHeader(data, out var start, out var end, out var headerEnd)
            || start < headerEnd
            || start > end
            || end > data.Length)
        {
            return Unreadable("The HTML on the clipboard is damaged.");
        }

        // Offsets count bytes, so one can fall inside a character. Decoding then yields a replacement
        // character and never an error.
        return Result.Success(Encoding.UTF8.GetString(data[start..end]));
    }

    /// <summary>Wraps a fragment in the CF_HTML layout other programs expect.</summary>
    /// <param name="fragment">The HTML to paste.</param>
    /// <returns>UTF-8 data ending with a NUL, with byte offsets in ten digits.</returns>
    public static byte[] Build(string fragment)
    {
        ArgumentNullException.ThrowIfNull(fragment);

        var preamble = Encoding.UTF8.GetByteCount(Preamble);
        var body = Encoding.UTF8.GetByteCount(fragment);
        var postamble = Encoding.UTF8.GetByteCount(Postamble);
        var headerLength = Encoding.UTF8.GetByteCount(Header(0, 0, 0, 0));

        var startHtml = headerLength;
        var startFragment = checked(headerLength + preamble);
        var endFragment = checked(startFragment + body);
        var endHtml = checked(endFragment + postamble);

        var text = Header(startHtml, endHtml, startFragment, endFragment) + Preamble + fragment + Postamble + "\0";
        return Encoding.UTF8.GetBytes(text);
    }

    private static string Header(int startHtml, int endHtml, int startFragment, int endFragment) =>
        string.Create(CultureInfo.InvariantCulture,
            $"Version:0.9\r\nStartHTML:{startHtml:D10}\r\nEndHTML:{endHtml:D10}\r\nStartFragment:{startFragment:D10}\r\nEndFragment:{endFragment:D10}\r\n");

    // Reads "Name:value" lines from the start until a line that is not one. headerEnd is where the page begins.
    private static bool TryReadHeader(ReadOnlySpan<byte> data, out int start, out int end, out int headerEnd)
    {
        start = -1;
        end = -1;
        headerEnd = 0;

        var window = data[..Math.Min(data.Length, MaxHeaderBytes)];
        var position = 0;
        for (var line = 0; line < MaxHeaderLines && position < window.Length; line++)
        {
            var rest = window[position..];
            var lineLength = rest.IndexOf((byte)'\n');
            if (lineLength < 0)
            {
                break;
            }

            var text = rest[..lineLength].TrimEnd((byte)'\r');
            var colon = text.IndexOf((byte)':');
            if (colon <= 0 || colon > MaxFieldNameLength || !IsFieldName(text[..colon]))
            {
                break;
            }

            var name = text[..colon];
            var value = text[(colon + 1)..].Trim((byte)' ');
            if (IsName(name, "StartFragment"u8))
            {
                start = start < 0 ? ReadNumber(value) : start;
            }
            else if (IsName(name, "EndFragment"u8))
            {
                end = end < 0 ? ReadNumber(value) : end;
            }

            position += lineLength + 1;
            headerEnd = position;
        }

        return start >= 0 && end >= 0;
    }

    private static bool IsFieldName(ReadOnlySpan<byte> name)
    {
        foreach (var character in name)
        {
            if (!(character is >= (byte)'A' and <= (byte)'Z' || character is >= (byte)'a' and <= (byte)'z'))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsName(ReadOnlySpan<byte> name, ReadOnlySpan<byte> expected) =>
        name.Length == expected.Length && Ascii.EqualsIgnoreCase(name, expected);

    // Digits only: a sign or a space is not part of the format, and a number too large for an int is damage.
    private static int ReadNumber(ReadOnlySpan<byte> digits) =>
        int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : -1;

    private static Result<string> Unreadable(string message) =>
        Result.Failure<string>(new ResultError(ErrorCode.ClipboardUnreadable, message));
}
