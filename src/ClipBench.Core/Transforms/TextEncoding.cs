using System.Text;
using ClipBench.Core.Results;

namespace ClipBench.Core.Transforms;

/// <summary>
/// URL (percent) and Base64 encoding of text. Both work on strict UTF-8, so a character that has no UTF-8 form, a
/// broken escape or bytes that are not text come back as a failure and are never quietly replaced.
/// </summary>
internal static class TextEncoding
{
    private const string UpperHex = "0123456789ABCDEF";

    private static readonly UTF8Encoding _strict = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static Result<string> UrlEncode(string text)
    {
        if (!TryGetBytes(text, out var bytes))
        {
            return Invalid("The text has a character that cannot be written as UTF-8.");
        }

        var builder = new StringBuilder(bytes.Length);
        foreach (var value in bytes)
        {
            if (IsUnreserved(value))
            {
                builder.Append((char)value);
            }
            else
            {
                builder.Append('%').Append(UpperHex[value >> 4]).Append(UpperHex[value & 0xF]);
            }
        }

        return Result.Success(builder.ToString());
    }

    public static Result<string> UrlDecode(string text)
    {
        var bytes = new List<byte>(text.Length);
        var index = 0;
        while (index < text.Length)
        {
            if (text[index] == '%')
            {
                if (index + 2 >= text.Length || !char.IsAsciiHexDigit(text[index + 1]) || !char.IsAsciiHexDigit(text[index + 2]))
                {
                    return Invalid("The text has a % that is not followed by two hex digits.");
                }

                bytes.Add((byte)((HexValue(text[index + 1]) << 4) | HexValue(text[index + 2])));
                index += 3;
                continue;
            }

            var end = text.IndexOf('%', index);
            if (end < 0)
            {
                end = text.Length;
            }

            if (!TryGetBytes(text.AsSpan(index, end - index), out var run))
            {
                return Invalid("The text has a character that cannot be written as UTF-8.");
            }

            bytes.AddRange(run);
            index = end;
        }

        return TryGetString([.. bytes], out var decoded)
            ? Result.Success(decoded)
            : Invalid("The escapes do not make valid UTF-8 text.");
    }

    public static Result<string> Base64Encode(string text) =>
        TryGetBytes(text, out var bytes)
            ? Result.Success(Convert.ToBase64String(bytes))
            : Invalid("The text has a character that cannot be written as UTF-8.");

    public static Result<string> Base64Decode(string text)
    {
        var clean = new StringBuilder(text.Length + 2);
        foreach (var c in text)
        {
            if (!char.IsWhiteSpace(c))
            {
                clean.Append(c);
            }
        }

        // Padding is optional on input: a length of 1 more than a multiple of 4 cannot be Base64 at all.
        switch (clean.Length % 4)
        {
            case 1:
                return Invalid("The text is not valid Base64.");
            case 2:
                clean.Append("==");
                break;
            case 3:
                clean.Append('=');
                break;
            default:
                break;
        }

        var buffer = new byte[(clean.Length / 4 * 3) + 3];
        if (!Convert.TryFromBase64String(clean.ToString(), buffer, out var written))
        {
            return Invalid("The text is not valid Base64.");
        }

        return TryGetString(buffer.AsSpan(0, written), out var decoded)
            ? Result.Success(decoded)
            : Invalid("The decoded bytes are not valid UTF-8 text.");
    }

    private static bool TryGetBytes(ReadOnlySpan<char> text, out byte[] bytes)
    {
        try
        {
            bytes = new byte[_strict.GetByteCount(text)];
            _ = _strict.GetBytes(text, bytes);
            return true;
        }
        catch (EncoderFallbackException)
        {
            bytes = [];
            return false;
        }
    }

    private static bool TryGetString(ReadOnlySpan<byte> bytes, out string text)
    {
        try
        {
            text = _strict.GetString(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            text = string.Empty;
            return false;
        }
    }

    private static bool IsUnreserved(byte value) =>
        value is >= (byte)'A' and <= (byte)'Z'
            or >= (byte)'a' and <= (byte)'z'
            or >= (byte)'0' and <= (byte)'9'
            or (byte)'-' or (byte)'_' or (byte)'.' or (byte)'~';

    private static int HexValue(char c) => c <= '9' ? c - '0' : (c | 0x20) - 'a' + 10;

    private static Result<string> Invalid(string message) =>
        Result.Failure<string>(new ResultError(ErrorCode.TransformInputInvalid, message));
}
