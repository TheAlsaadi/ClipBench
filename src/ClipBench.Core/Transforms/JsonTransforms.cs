using System.Buffers;
using System.Text;
using System.Text.Json;
using ClipBench.Core.Results;

namespace ClipBench.Core.Transforms;

/// <summary>
/// Formats and minifies JSON text. Strict JSON only: no comments, no trailing commas, one value. The text is copied
/// token by token exactly as it was written, so numbers are not rounded and strings keep their escapes (an emoji stays
/// an emoji, an escape stays an escape). Text that is not JSON comes back as a failure that names the line and never
/// repeats the text.
/// </summary>
internal static class JsonTransforms
{
    /// <summary>The deepest nesting accepted, which keeps a hostile document from using up memory or the stack.</summary>
    public const int MaxDepth = 64;

    private const char ByteOrderMark = (char)0xFEFF;
    private const int IndentWidth = 2;

    private static readonly byte[] _lineBreak = "\r\n"u8.ToArray();
    private static readonly UTF8Encoding _strict = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static Result<string> Format(string text) => Rewrite(text, indented: true);

    public static Result<string> Minify(string text) => Rewrite(text, indented: false);

    private static Result<string> Rewrite(string text, bool indented)
    {
        var json = text.TrimStart(ByteOrderMark);
        if (string.IsNullOrWhiteSpace(json))
        {
            return Invalid("There is no JSON in the text.");
        }

        byte[] bytes;
        try
        {
            bytes = _strict.GetBytes(json);
        }
        catch (EncoderFallbackException)
        {
            return Invalid("The text has a character that cannot be written as UTF-8.");
        }

        try
        {
            var output = new ArrayBufferWriter<byte>(bytes.Length + (bytes.Length / 4));
            Walk(bytes, output, indented);
            return Result.Success(_strict.GetString(output.WrittenSpan));
        }
        catch (JsonException exception)
        {
            return ExceedsDepth(json)
                ? Invalid($"The JSON is nested more than {MaxDepth} levels deep.")
                : Invalid($"The text is not valid JSON (line {(exception.LineNumber ?? 0) + 1}).");
        }
    }

    // Reads the tokens and writes them again. The reader checks the grammar, the depth and what follows the value.
    private static void Walk(byte[] bytes, ArrayBufferWriter<byte> output, bool indented)
    {
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { MaxDepth = MaxDepth, CommentHandling = JsonCommentHandling.Disallow, AllowTrailingCommas = false });

        // Whether the container at each depth already holds an item, so the next one needs a comma.
        var hasItems = new bool[MaxDepth + 2];
        var depth = 0;
        var afterName = false;

        while (reader.Read())
        {
            var token = reader.TokenType;
            if (token is JsonTokenType.EndObject or JsonTokenType.EndArray)
            {
                depth--;
                if (indented && hasItems[depth + 1])
                {
                    NewLine(output, depth);
                }

                output.Write(token == JsonTokenType.EndObject ? "}"u8 : "]"u8);
                continue;
            }

            // A value after a property name sits on the same line as the name.
            if (!afterName)
            {
                if (depth > 0)
                {
                    if (hasItems[depth])
                    {
                        output.Write(","u8);
                    }

                    hasItems[depth] = true;
                    if (indented)
                    {
                        NewLine(output, depth);
                    }
                }
            }

            afterName = false;
            switch (token)
            {
                case JsonTokenType.StartObject:
                case JsonTokenType.StartArray:
                    output.Write(token == JsonTokenType.StartObject ? "{"u8 : "["u8);
                    depth++;
                    hasItems[depth] = false;
                    break;
                case JsonTokenType.PropertyName:
                    Quoted(output, reader.ValueSpan);
                    output.Write(indented ? ": "u8 : ":"u8);
                    afterName = true;
                    break;
                case JsonTokenType.String:
                    Quoted(output, reader.ValueSpan);
                    break;
                default:
                    // A number, true, false or null, as written.
                    output.Write(reader.ValueSpan);
                    break;
            }
        }
    }

    private static void Quoted(ArrayBufferWriter<byte> output, ReadOnlySpan<byte> raw)
    {
        output.Write("\""u8);
        output.Write(raw);
        output.Write("\""u8);
    }

    private static void NewLine(ArrayBufferWriter<byte> output, int depth)
    {
        output.Write(_lineBreak);
        var indent = output.GetSpan(depth * IndentWidth);
        indent[..(depth * IndentWidth)].Fill((byte)' ');
        output.Advance(depth * IndentWidth);
    }

    // Only asked after a failure, to say why: the depth of brackets outside strings.
    private static bool ExceedsDepth(string json)
    {
        var depth = 0;
        var inString = false;
        for (var i = 0; i < json.Length; i++)
        {
            var c = json[i];
            if (inString)
            {
                if (c == '\\')
                {
                    i++;
                }
                else if (c == '"')
                {
                    inString = false;
                }
            }
            else if (c == '"')
            {
                inString = true;
            }
            else if (c is '[' or '{')
            {
                if (++depth > MaxDepth)
                {
                    return true;
                }
            }
            else if (c is ']' or '}')
            {
                depth--;
            }
        }

        return false;
    }

    private static Result<string> Invalid(string message) =>
        Result.Failure<string>(new ResultError(ErrorCode.TransformInputInvalid, message));
}
