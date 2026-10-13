using ClipBench.Core.Pasting;

namespace ClipBench.Core.Tests.Support;

/// <summary>Lets a theory name a separator preset as a string, because xUnit needs serializable test data.</summary>
internal static class SeparatorNames
{
    public static Separator Get(string name) => name switch
    {
        "None" => Separator.None,
        "NewLine" => Separator.NewLine,
        "BlankLine" => Separator.BlankLine,
        "Space" => Separator.Space,
        "Tab" => Separator.Tab,
        "Comma" => Separator.Comma,
        "CommaSpace" => Separator.CommaSpace,
        "Semicolon" => Separator.Semicolon,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Not a separator preset."),
    };
}
