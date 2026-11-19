using System.Text;
using ClipBench.Core.Clips;
using ClipBench.Core.Pasting;
using ClipBench.Core.Tests.Support;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace ClipBench.Core.Tests.Properties;

public sealed class PasteComposerProperties
{
    private static readonly Gen<Clip[]> _clips = Generators.Text.Select(text => TestClips.Text(text)).ArrayOf().Select(clips => clips.Take(50).ToArray());

    private static readonly Gen<Separator> _separators = Gen.Elements(
        Separator.None, Separator.NewLine, Separator.BlankLine, Separator.Space, Separator.Tab, Separator.Comma, Separator.CommaSpace, Separator.Semicolon);

    private static readonly Gen<int> _limits = Gen.Frequency((1, Gen.Constant(PasteComposer.DefaultMaxOutputCharacters)), (3, Gen.Choose(1, 2_000)));

    private static string[] TextsOf(Clip[] clips) => [.. clips.Select(clip => clip.Content.PlainText!)];

    [Property]
    public Property A_batch_never_throws_stays_within_the_limit_and_joins_the_texts_in_order() =>
        Prop.ForAll(Arb.From(_clips), Arb.From(_separators), Arb.From(_limits), (clips, separator, limit) =>
        {
            var composed = new PasteComposer(limit).ComposeBatch(clips, separator);
            if (clips.Length == 0)
            {
                return composed.IsFailure;
            }

            var joined = string.Join(separator.Text, TextsOf(clips));
            return composed.IsSuccess
                ? composed.Value.Length <= limit && composed.Value == joined
                : joined.Length > limit;
        });

    [Property]
    public Property A_table_never_throws_stays_within_the_limit_and_a_spreadsheet_reads_back_every_cell() =>
        Prop.ForAll(Arb.From(_clips), Arb.From(Gen.Elements(TableLayout.Column, TableLayout.Row)), Arb.From(_limits), (clips, layout, limit) =>
        {
            var composed = new PasteComposer(limit).ComposeTable(clips, layout);
            if (clips.Length == 0 || composed.IsFailure)
            {
                return clips.Length == 0 ? composed.IsFailure : true;
            }

            if (composed.Value.Length > limit)
            {
                return false;
            }

            var cells = ReadCells(composed.Value, layout == TableLayout.Column ? "\r\n" : "\t");
            return cells.SequenceEqual(TextsOf(clips));
        });

    // Reads cells the way a spreadsheet reads pasted text: a cell that starts with a quote runs to the matching quote,
    // with "" standing for one quote inside it; any other cell runs to the next break.
    private static List<string> ReadCells(string table, string between)
    {
        var cells = new List<string>();
        var index = 0;
        while (true)
        {
            var cell = new StringBuilder();
            if (index < table.Length && table[index] == '"')
            {
                index++;
                while (index < table.Length)
                {
                    if (table[index] == '"')
                    {
                        if (index + 1 < table.Length && table[index + 1] == '"')
                        {
                            _ = cell.Append('"');
                            index += 2;
                            continue;
                        }

                        index++;
                        break;
                    }

                    _ = cell.Append(table[index++]);
                }
            }
            else
            {
                var end = table.IndexOf(between, index, StringComparison.Ordinal);
                end = end < 0 ? table.Length : end;
                _ = cell.Append(table, index, end - index);
                index = end;
            }

            cells.Add(cell.ToString());
            if (index >= table.Length)
            {
                return cells;
            }

            if (string.CompareOrdinal(table, index, between, 0, between.Length) != 0)
            {
                // Text after a closing quote that is not a break: the table is not one a spreadsheet can read.
                cells.Add("<unreadable>");
                return cells;
            }

            index += between.Length;
        }
    }
}
