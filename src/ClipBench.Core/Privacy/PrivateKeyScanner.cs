namespace ClipBench.Core.Privacy;

/// <summary>Finds PEM headers of private keys, such as <c>-----BEGIN RSA PRIVATE KEY-----</c>. Linear in the text length.</summary>
internal static class PrivateKeyScanner
{
    private const string Begin = "-----BEGIN ";
    private const string Fence = "-----";
    private const string Label = "PRIVATE KEY";
    private const int MaxLabelLength = 64;

    public static bool Contains(ReadOnlySpan<char> text)
    {
        var offset = 0;
        while (offset < text.Length)
        {
            var found = text[offset..].IndexOf(Begin, StringComparison.OrdinalIgnoreCase);
            if (found < 0)
            {
                return false;
            }

            // The label sits between "-----BEGIN " and the closing "-----". Look at a short window only,
            // so a header that never closes cannot make the scan slow.
            var labelStart = offset + found + Begin.Length;
            var window = text[labelStart..Math.Min(text.Length, labelStart + MaxLabelLength + Fence.Length)];
            var close = window.IndexOf(Fence, StringComparison.Ordinal);
            if (close >= 0 && window[..close].Contains(Label, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            offset = labelStart;
        }

        return false;
    }
}
