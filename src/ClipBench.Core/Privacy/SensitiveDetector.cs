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

        // Any PEM block that got this far is public by design (a certificate, a public key, a request):
        // its base64 lines look like random tokens but are not secrets.
        if (scanned.Contains(PemHeader, StringComparison.Ordinal))
        {
            return SensitiveKind.None;
        }

        return TokenScanner.Contains(scanned) ? SensitiveKind.Token : SensitiveKind.None;
    }
}
