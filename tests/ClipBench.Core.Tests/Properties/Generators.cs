using FsCheck;
using FsCheck.Fluent;

namespace ClipBench.Core.Tests.Properties;

/// <summary>Inputs shaped like what users type and copy, with the characters parsers find hardest.</summary>
internal static class Generators
{
    // Characters that mean something to one of the parsers, plus line breaks, a NUL, a lone surrogate half and a
    // character outside the Basic Multilingual Plane.
    private static readonly string[] _awkward =
    [
        "{", "}", "{{", "}}", ":", "\\", "\"", "\t", "\r", "\n", "\r\n", "\0", "\uD800", "\uDC00", "😀", "é", "ي", " ",
    ];

    /// <summary>Any string, often built from awkward pieces; never null.</summary>
    public static Gen<string> Text { get; } = Gen.Frequency(
        (3, ArbMap.Default.GeneratorFor<string>().Select(text => text ?? string.Empty)),
        (2, Gen.Elements(_awkward).ListOf().Select(string.Concat)),
        (1, Gen.Elements("a", "b", "\"", "\t", "\r\n").ListOf().Select(string.Concat)));

    /// <summary>A string up to about a hundred thousand characters, to reach the size limits.</summary>
    public static Gen<string> LongText { get; } = Gen.Frequency(
        (4, Text),
        (1, Gen.Choose(0, 120_000).Select(length => new string('x', length))));
}
