using System.Buffers.Binary;
using System.Text;
using ClipBench.Core.Clips;
using ClipBench.Core.Results;

namespace ClipBench.Storage.Clips;

/// <summary>
/// Turns clip content into the bytes that are encrypted, and back. Text is its UTF-8 bytes and an image
/// is its PNG bytes. HTML, rich text and file lists are a part count followed by length-prefixed UTF-8
/// parts, all little-endian. Decoding is strict: sizes and counts are checked against the data before
/// anything is allocated, invalid UTF-8 and trailing bytes are rejected, and hostile or damaged input gives
/// a failure and never an exception.
/// </summary>
internal static class ClipContentCodec
{
    /// <summary>The most bytes one clip may take once encoded: above the largest default capture.</summary>
    public const int MaxEncodedBytes = 64 * 1024 * 1024;

    /// <summary>The most parts a framed clip may hold, which bounds the length of a file list.</summary>
    public const int MaxParts = 100_000;

    private const int Prefix = sizeof(uint);

    // Writing never throws on a lone surrogate: it is replaced, as clip previews already do.
    private static readonly UTF8Encoding _lenient = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);

    // Reading is strict: bytes that are not valid UTF-8 mean the record is damaged.
    private static readonly UTF8Encoding _strict = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>Encodes a clip's content.</summary>
    /// <param name="content">The content.</param>
    /// <param name="maxBytes">The most encoded bytes to accept.</param>
    /// <returns>The encoded bytes, or <see cref="ErrorCode.ContentTooLarge"/>.</returns>
    public static Result<byte[]> Encode(ClipContent content, int maxBytes = MaxEncodedBytes)
    {
        ArgumentNullException.ThrowIfNull(content);

        return content switch
        {
            TextContent text => EncodeText(text.Text, maxBytes),
            ImageContent image => image.Png.Length > maxBytes ? TooLarge() : Result.Success(image.Png.ToArray()),
            HtmlContent html => EncodeParts([html.Html, html.Text], maxBytes),
            RtfContent rtf => EncodeParts([rtf.Rtf, rtf.Text], maxBytes),
            FilesContent files => EncodeParts(files.Paths, maxBytes),
            _ => throw new ArgumentException("This kind of content cannot be stored.", nameof(content)),
        };
    }

    /// <summary>Decodes stored bytes of the given format.</summary>
    /// <param name="format">The format recorded with the clip.</param>
    /// <param name="data">The decrypted bytes.</param>
    /// <param name="maxBytes">The most bytes to accept.</param>
    /// <returns>The content, or a failure when the data is too large or damaged.</returns>
    public static Result<ClipContent> Decode(ClipFormat format, ReadOnlySpan<byte> data, int maxBytes = MaxEncodedBytes)
    {
        if (data.Length > maxBytes)
        {
            return Result.Failure<ClipContent>(new ResultError(ErrorCode.ContentTooLarge, "A stored clip is larger than storage accepts."));
        }

        try
        {
            return format switch
            {
                ClipFormat.Text => Result.Success<ClipContent>(new TextContent(_strict.GetString(data))),
                ClipFormat.Image => data.IsEmpty ? Damaged() : Result.Success<ClipContent>(new ImageContent(data)),
                ClipFormat.Html => DecodePair(data, (html, text) => new HtmlContent(html, text)),
                ClipFormat.Rtf => DecodePair(data, (rtf, text) => new RtfContent(rtf, text)),
                ClipFormat.Files => DecodeFiles(data),
                _ => Damaged(),
            };
        }
        catch (ArgumentException)
        {
            // Invalid UTF-8 (DecoderFallbackException is an ArgumentException), or content the model refuses,
            // such as an empty file list or a blank path.
            return Damaged();
        }
    }

    private static Result<byte[]> EncodeText(string text, int maxBytes)
    {
        var length = _lenient.GetByteCount(text);
        return length > maxBytes ? TooLarge() : Result.Success(_lenient.GetBytes(text));
    }

    private static Result<byte[]> EncodeParts(IReadOnlyList<string> parts, int maxBytes)
    {
        var lengths = new int[parts.Count];
        var total = (long)Prefix;
        for (var i = 0; i < parts.Count; i++)
        {
            lengths[i] = _lenient.GetByteCount(parts[i]);
            total += Prefix + lengths[i];
        }

        if (total > maxBytes)
        {
            return TooLarge();
        }

        var encoded = new byte[total];
        BinaryPrimitives.WriteUInt32LittleEndian(encoded, (uint)parts.Count);
        var offset = Prefix;
        for (var i = 0; i < parts.Count; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(encoded.AsSpan(offset), (uint)lengths[i]);
            offset += Prefix;
            offset += _lenient.GetBytes(parts[i], encoded.AsSpan(offset));
        }

        return Result.Success(encoded);
    }

    private static Result<ClipContent> DecodePair(ReadOnlySpan<byte> data, Func<string, string, ClipContent> create)
    {
        var parts = ReadParts(data);
        return parts is { Length: 2 } ? Result.Success(create(parts[0], parts[1])) : Damaged();
    }

    private static Result<ClipContent> DecodeFiles(ReadOnlySpan<byte> data)
    {
        var parts = ReadParts(data);
        return parts is { Length: > 0 } ? Result.Success<ClipContent>(new FilesContent(parts)) : Damaged();
    }

    // Returns null when the data is not exactly a count followed by that many length-prefixed parts.
    private static string[]? ReadParts(ReadOnlySpan<byte> data)
    {
        if (data.Length < Prefix)
        {
            return null;
        }

        // The count is checked before an array is allocated for it, so damaged data cannot ask for a huge one.
        var count = BinaryPrimitives.ReadUInt32LittleEndian(data);
        if (count > MaxParts)
        {
            return null;
        }

        var parts = new string[count];
        var offset = Prefix;
        for (var i = 0; i < parts.Length; i++)
        {
            if (data.Length - offset < Prefix)
            {
                return null;
            }

            var length = BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
            offset += Prefix;
            if (length > data.Length - offset)
            {
                return null;
            }

            parts[i] = _strict.GetString(data.Slice(offset, (int)length));
            offset += (int)length;
        }

        return offset == data.Length ? parts : null;
    }

    private static Result<byte[]> TooLarge() =>
        Result.Failure<byte[]>(new ResultError(ErrorCode.ContentTooLarge, "That clip is too large to store."));

    private static Result<ClipContent> Damaged() =>
        Result.Failure<ClipContent>(new ResultError(ErrorCode.StorageFailure, "A stored clip could not be read."));
}
