using System.Buffers;

namespace ClipBench.Core.Privacy;

/// <summary>
/// Recognises text that looks like a secret: private keys, JSON web tokens, card numbers, IBANs and long
/// random-looking tokens. These are heuristics that favour few false alarms over catching everything, so a
/// secret can be missed. Every scan is linear in the text length and only the first
/// <see cref="MaxScannedCharacters"/> characters are read.
/// </summary>
public static class SensitiveDetector
{
    /// <summary>Only this many characters at the start of a text are scanned.</summary>
    public const int MaxScannedCharacters = 1_000_000;

    private const string PemHeader = "-----BEGIN ";
    private const string PemFooter = "-----END ";

    /// <summary>Looks at a text for a secret.</summary>
    /// <param name="text">The text to scan.</param>
    /// <returns>The most serious kind found, or <see cref="SensitiveKind.None"/>.</returns>
    public static SensitiveKind Detect(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var scanned = text.AsSpan(0, Math.Min(text.Length, MaxScannedCharacters));

        if (PrivateKeyScanner.Contains(scanned))
        {
            return SensitiveKind.PrivateKey;
        }

        if (JwtScanner.Contains(scanned))
        {
            return SensitiveKind.Jwt;
        }

        if (CardNumberScanner.Contains(scanned))
        {
            return SensitiveKind.CardNumber;
        }

        if (IbanScanner.Contains(scanned))
        {
            return SensitiveKind.Iban;
        }

        return ContainsTokenOutsidePemBlocks(scanned) ? SensitiveKind.Token : SensitiveKind.None;
    }

    // Any PEM block that got this far is public by design (a certificate, a public key, a request): its base64 lines
    // look like random tokens but are not secrets. Those blocks are blanked out and the rest of the text is still
    // scanned, so a token next to a certificate is found. A block with no end line runs to the end of the text.
    private static bool ContainsTokenOutsidePemBlocks(ReadOnlySpan<char> text)
    {
        if (!text.Contains(PemHeader, StringComparison.Ordinal))
        {
            return TokenScanner.Contains(text);
        }

        var buffer = ArrayPool<char>.Shared.Rent(text.Length);
        try
        {
            var copy = buffer.AsSpan(0, text.Length);
            text.CopyTo(copy);
            BlankPemBlocks(copy);
            return TokenScanner.Contains(copy);
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer, clearArray: true);
        }
    }

    private static void BlankPemBlocks(Span<char> text)
    {
        var offset = 0;
        while (offset < text.Length)
        {
            var begin = text[offset..].IndexOf(PemHeader, StringComparison.Ordinal);
            if (begin < 0)
            {
                return;
            }

            begin += offset;
            var afterHeader = begin + PemHeader.Length;
            var footer = text[afterHeader..].IndexOf(PemFooter, StringComparison.Ordinal);
            var stop = text.Length;
            if (footer >= 0)
            {
                footer += afterHeader;
                var lineEnd = text[footer..].IndexOfAny('\r', '\n');
                stop = lineEnd < 0 ? text.Length : footer + lineEnd;
            }

            text[begin..stop].Fill(' ');
            offset = stop;
        }
    }
}
