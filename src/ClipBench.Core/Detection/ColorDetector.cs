using System.Globalization;

namespace ClipBench.Core.Detection;

/// <summary>
/// Reads a color written as hex (<c>#f00</c>, <c>#ff0000</c>, with or without alpha), <c>rgb()</c>/<c>rgba()</c> or
/// <c>hsl()</c>/<c>hsla()</c>. The whole text has to be exactly one color. Hand-written, with a length cap, so no
/// pattern can take long.
/// </summary>
public static class ColorDetector
{
    /// <summary>The longest text that is looked at, after trimming.</summary>
    public const int MaxLength = 64;

    /// <summary>Reads the text as one color.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The color, or null when the text is not exactly one color.</returns>
    public static ColorSwatch? TryParse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var trimmed = text.Trim();
        if (trimmed.Length == 0 || trimmed.Length > MaxLength)
        {
            return null;
        }

        if (trimmed[0] == '#')
        {
            return ParseHex(trimmed[1..]);
        }

        if (!trimmed.EndsWith(')'))
        {
            return null;
        }

        var open = trimmed.IndexOf('(', StringComparison.Ordinal);
        if (open < 0)
        {
            return null;
        }

        var name = trimmed[..open].ToLowerInvariant();
        var parts = trimmed[(open + 1)..^1].Replace(',', ' ').Replace('/', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is not (3 or 4))
        {
            return null;
        }

        return name switch
        {
            "rgb" or "rgba" => ParseRgb(parts),
            "hsl" or "hsla" => ParseHsl(parts),
            _ => null,
        };
    }

    private static ColorSwatch? ParseHex(string digits)
    {
        if (digits.Length is not (3 or 4 or 6 or 8) || !digits.All(char.IsAsciiHexDigit))
        {
            return null;
        }

        var shortForm = digits.Length <= 4;
        var step = shortForm ? 1 : 2;
        var channels = new List<byte>(4);
        for (var i = 0; i < digits.Length; i += step)
        {
            var pair = shortForm ? new string(digits[i], 2) : digits.Substring(i, 2);
            channels.Add(Convert.ToByte(pair, 16));
        }

        return new ColorSwatch(channels[0], channels[1], channels[2], channels.Count == 4 ? channels[3] : (byte)255);
    }

    private static ColorSwatch? ParseRgb(string[] parts)
    {
        var channels = new byte[3];
        for (var i = 0; i < 3; i++)
        {
            if (ReadChannel(parts[i]) is not { } channel)
            {
                return null;
            }

            channels[i] = channel;
        }

        return ReadAlpha(parts) is { } alpha ? new ColorSwatch(channels[0], channels[1], channels[2], alpha) : null;
    }

    // Whole numbers from 0 to 255, or a percentage from 0 to 100.
    private static byte? ReadChannel(string part)
    {
        if (part.EndsWith('%'))
        {
            return ReadDecimal(part[..^1]) is { } percent and >= 0 and <= 100 ? ToByte(percent * 255m / 100m) : null;
        }

        return part.All(char.IsAsciiDigit) && part.Length <= 3 && int.Parse(part, CultureInfo.InvariantCulture) is var value and <= 255
            ? (byte)value
            : null;
    }

    // A fraction from 0 to 1, or a percentage from 0 to 100; opaque when there is none.
    private static byte? ReadAlpha(string[] parts)
    {
        if (parts.Length == 3)
        {
            return 255;
        }

        var part = parts[3];
        if (part.EndsWith('%'))
        {
            return ReadDecimal(part[..^1]) is { } percent and >= 0 and <= 100 ? ToByte(percent * 255m / 100m) : null;
        }

        return ReadDecimal(part) is { } fraction and >= 0 and <= 1 ? ToByte(fraction * 255m) : null;
    }

    private static ColorSwatch? ParseHsl(string[] parts)
    {
        var hueText = parts[0].EndsWith("deg", StringComparison.OrdinalIgnoreCase) ? parts[0][..^3] : parts[0];
        var hue = ReadDecimal(hueText);
        var saturation = ReadPercent(parts[1]);
        var lightness = ReadPercent(parts[2]);
        var alpha = ReadAlpha(parts);
        if (hue is null || saturation is null || lightness is null || alpha is null)
        {
            return null;
        }

        var s = (double)saturation.Value / 100;
        var l = (double)lightness.Value / 100;
        var h = (((double)hue.Value % 360) + 360) % 360;
        var chroma = (1 - Math.Abs((2 * l) - 1)) * s;
        var x = chroma * (1 - Math.Abs((h / 60 % 2) - 1));
        var m = l - (chroma / 2);
        var (r, g, b) = (int)(h / 60) switch
        {
            0 => (chroma, x, 0d),
            1 => (x, chroma, 0d),
            2 => (0d, chroma, x),
            3 => (0d, x, chroma),
            4 => (x, 0d, chroma),
            _ => (chroma, 0d, x),
        };

        return new ColorSwatch(ToByte((decimal)((r + m) * 255)), ToByte((decimal)((g + m) * 255)), ToByte((decimal)((b + m) * 255)), alpha.Value);
    }

    // A percentage from 0 to 100 written with a percent sign.
    private static decimal? ReadPercent(string part) =>
        part.EndsWith('%') && ReadDecimal(part[..^1]) is { } value and >= 0 and <= 100 ? value : null;

    // Digits with an optional sign and decimal point; no exponent, no NaN, no thousands separators.
    private static decimal? ReadDecimal(string text) =>
        decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private static byte ToByte(decimal value) => (byte)Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), 0, 255);
}
