using ClipBench.Core.Results;

namespace ClipBench.Core.Transforms;

/// <summary>The transforms that come with ClipBench.</summary>
public static class BuiltInTransforms
{
    /// <summary>Keeps the text and drops the formatting. The text of an HTML or rich text clip is what comes in.</summary>
    public static TextTransform PlainText { get; } = new("text.plain", "Plain text", "Keep the text, drop formatting", Result.Success);

    /// <summary>Changes every letter to upper case.</summary>
    public static TextTransform Upper { get; } = new("case.upper", "UPPER CASE", "Change every letter to upper case", text => Result.Success(text.ToUpperInvariant()));

    /// <summary>Changes every letter to lower case.</summary>
    public static TextTransform Lower { get; } = new("case.lower", "lower case", "Change every letter to lower case", text => Result.Success(text.ToLowerInvariant()));

    /// <summary>Capitalises the first letter of each word and lower-cases the rest.</summary>
    public static TextTransform Title { get; } = new("case.title", "Title Case", "Capitalise each word", text => Result.Success(TextConversion.Title(text)));

    /// <summary>Capitalises the first letter of each sentence and lower-cases the rest.</summary>
    public static TextTransform Sentence { get; } = new("case.sentence", "Sentence case", "Capitalise each sentence", text => Result.Success(TextConversion.Sentence(text)));

    /// <summary>Removes white space from both ends of the text.</summary>
    public static TextTransform Trim { get; } = new("text.trim", "Trim", "Remove white space at the start and end", text => Result.Success(text.Trim()));

    /// <summary>Joins the lines into one, with a single space where a line break was.</summary>
    public static TextTransform RemoveLineBreaks { get; } = new("text.single-line", "Remove line breaks", "Join all lines into one", text => Result.Success(TextConversion.RemoveLineBreaks(text)));
}
