using ClipBench.Core.Snippets;
using ClipBench.Core.Tests.Support;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace ClipBench.Core.Tests.Properties;

public sealed class SnippetProperties
{
    // Pieces of snippet syntax, well formed and broken, mixed with any text.
    private static readonly Gen<string> _body = Gen.Frequency(
        (1, Generators.LongText),
        (3, Gen.Frequency(
                (4, Gen.Elements(
                    "{clipboard}", "{date}", "{time}", "{date:yyyy}", "{time:HH:mm:ss}", "{date:%}", "{date:\\}", "{date:",
                    "{input:name}", "{input:Name}", "{input:}", "{input:a b}", "{input:" + new string('n', 50) + "}",
                    "{unknown}", "{", "}", "{{", "}}", "{{clipboard}}", "text ", "\r\n")),
                (1, Generators.Text))
            .ListOf()
            .Select(string.Concat)));

    private static readonly SnippetEngine _engine = new(new FakeTimeProvider(TestClips.Start));

    [Property]
    public Property Parsing_any_text_never_throws() =>
        Prop.ForAll(Arb.From(_body), body =>
        {
            var parsed = SnippetEngine.Parse(body);
            return parsed.IsSuccess
                ? parsed.Value.InputNames.Count <= SnippetEngine.MaxInputs
                : body.Length > SnippetEngine.MaxBodyLength || parsed.Error.Code == ClipBench.Core.Results.ErrorCode.InvalidSnippet;
        });

    [Property]
    public Property Filling_in_a_parsed_snippet_never_throws_and_stays_within_the_output_limit() =>
        Prop.ForAll(Arb.From(_body), Arb.From(Generators.LongText), Arb.From(Generators.LongText), (body, clipboard, typed) =>
        {
            var parsed = SnippetEngine.Parse(body);
            if (parsed.IsFailure)
            {
                return true;
            }

            var inputs = parsed.Value.InputNames.ToDictionary(name => name, _ => typed, StringComparer.OrdinalIgnoreCase);
            var expanded = _engine.Expand(parsed.Value, new SnippetValues(clipboard, inputs));
            return expanded.IsFailure || expanded.Value.Length <= SnippetEngine.MaxOutputLength;
        });

    [Property]
    public Property A_snippet_with_no_braces_comes_back_as_it_was() =>
        Prop.ForAll(Arb.From(Generators.Text.Where(text => !text.Contains('{', StringComparison.Ordinal) && !text.Contains('}', StringComparison.Ordinal))), text =>
        {
            var parsed = SnippetEngine.Parse(text);
            return parsed.IsSuccess && _engine.Expand(parsed.Value, new SnippetValues(null, null)).Value == text;
        });
}
