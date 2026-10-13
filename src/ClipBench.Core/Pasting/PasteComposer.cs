using System.Buffers;
using System.Text;
using ClipBench.Core.Clips;
using ClipBench.Core.Results;

namespace ClipBench.Core.Pasting;

/// <summary>Turns a selection of clips into the one text that is pasted.</summary>
public sealed class PasteComposer
{
    /// <summary>The default limit on the size of the composed text, in characters.</summary>
    public const int DefaultMaxOutputCharacters = 10_000_000;

    private const string RowBreak = "\r\n";
    private const string CellBreak = "\t";

    // A spreadsheet reads a cell as quoted when it holds any of these.
    private static readonly SearchValues<char> _needsQuoting = SearchValues.Create("\t\r\n\"");

    private readonly int _maxOutputCharacters;

    /// <summary>Creates a composer.</summary>
    /// <param name="maxOutputCharacters">The most characters the composed text may have.</param>
    public PasteComposer(int maxOutputCharacters = DefaultMaxOutputCharacters)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxOutputCharacters, 1);
        _maxOutputCharacters = maxOutputCharacters;
    }

    /// <summary>Joins the clips' text with a separator, in the order given.</summary>
    /// <param name="clips">The selection, in selection order.</param>
    /// <param name="separator">The text between clips.</param>
    /// <returns>The text, or a failure when the selection is empty, holds an image or is too large.</returns>
    public Result<string> ComposeBatch(IReadOnlyList<Clip> clips, Separator separator)
    {
        ArgumentNullException.ThrowIfNull(clips);
        ArgumentNullException.ThrowIfNull(separator);

        var texts = TextsOf(clips);
        if (texts.IsFailure)
        {
            return Result.Failure<string>(texts.Error);
        }

        if (RawLength(texts.Value, separator.Text.Length) > _maxOutputCharacters)
        {
            return TooLarge();
        }

        return Result.Success(string.Join(separator.Text, texts.Value));
    }

    /// <summary>
    /// Builds tab-separated text for a spreadsheet, one clip per cell. A cell that holds a tab, a line
    /// break or a double quote is wrapped in double quotes with inner quotes doubled, as Excel does.
    /// </summary>
    /// <param name="clips">The selection, in selection order.</param>
    /// <param name="layout">Whether the cells fill a column or a row.</param>
    /// <returns>The text, or a failure when the selection is empty, holds an image or is too large.</returns>
    public Result<string> ComposeTable(IReadOnlyList<Clip> clips, TableLayout layout)
    {
        ArgumentNullException.ThrowIfNull(clips);
        if (!Enum.IsDefined(layout))
        {
            throw new ArgumentOutOfRangeException(nameof(layout), layout, "Unknown table layout.");
        }

        var texts = TextsOf(clips);
        if (texts.IsFailure)
        {
            return Result.Failure<string>(texts.Error);
        }

        var between = layout == TableLayout.Column ? RowBreak : CellBreak;
        if (RawLength(texts.Value, between.Length) > _maxOutputCharacters)
        {
            return TooLarge();
        }

        var table = new StringBuilder();
        for (var index = 0; index < texts.Value.Count; index++)
        {
            if (index > 0)
            {
                table.Append(between);
            }

            AppendCell(table, texts.Value[index]);

            // Quoting makes text longer, so the size is checked again as the table grows.
            if (table.Length > _maxOutputCharacters)
            {
                return TooLarge();
            }
        }

        return Result.Success(table.ToString());
    }

    private static void AppendCell(StringBuilder table, string text)
    {
        if (!text.AsSpan().ContainsAny(_needsQuoting))
        {
            table.Append(text);
            return;
        }

        table.Append('"').Append(text.Replace("\"", "\"\"", StringComparison.Ordinal)).Append('"');
    }

    private static long RawLength(List<string> texts, int betweenLength)
    {
        long total = (long)betweenLength * (texts.Count - 1);
        foreach (var text in texts)
        {
            total += text.Length;
        }

        return total;
    }

    private static Result<List<string>> TextsOf(IReadOnlyList<Clip> clips)
    {
        if (clips.Count == 0)
        {
            return Result.Failure<List<string>>(new ResultError(ErrorCode.NothingSelected, "Select at least one clip to paste."));
        }

        var texts = new List<string>(clips.Count);
        foreach (var clip in clips)
        {
            if (clip.Content.PlainText is not { } text)
            {
                return Result.Failure<List<string>>(new ResultError(
                    ErrorCode.ImageNotJoinable,
                    "Images cannot be joined into text. Select only text clips, or paste the image on its own."));
            }

            texts.Add(text);
        }

        return Result.Success(texts);
    }

    private static Result<string> TooLarge() =>
        Result.Failure<string>(new ResultError(ErrorCode.OutputTooLarge, "The combined text is too large to paste."));
}
