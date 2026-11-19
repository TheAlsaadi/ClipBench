using System.Text;
using ClipBench.Core.Pasting;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace ClipBench.Core.Tests.Properties;

public sealed class SeparatorProperties
{
    [Property]
    public Property Parsing_anything_typed_never_throws_and_stays_within_the_limit() =>
        Prop.ForAll(Arb.From(Generators.LongText), typed =>
        {
            var parsed = Separator.Parse(typed);
            return parsed.IsFailure || parsed.Value.Text.Length <= Separator.MaxCustomLength;
        });

    [Property]
    public Property Any_short_separator_written_with_escapes_reads_back_the_same() =>
        Prop.ForAll(Arb.From(Generators.Text.Where(text => text.Length <= Separator.MaxCustomLength)), text =>
            Separator.Parse(Escape(text)) is { IsSuccess: true } parsed && parsed.Value.Text == text);

    private static string Escape(string text)
    {
        var escaped = new StringBuilder();
        foreach (var character in text)
        {
            _ = character switch
            {
                '\\' => escaped.Append(@"\\"),
                '\n' => escaped.Append(@"\n"),
                '\r' => escaped.Append(@"\r"),
                '\t' => escaped.Append(@"\t"),
                _ => escaped.Append(character),
            };
        }

        return escaped.ToString();
    }
}
