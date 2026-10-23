using ClipBench.Core.Results;
using ClipBench.Core.Transforms;

namespace ClipBench.Core.Tests.Transforms;

public class TransformRegistryTests
{
    private static TextTransform Named(string id) => new(id, id.ToUpperInvariant(), "test", text => Result.Success(text));

    [Fact]
    public void The_transforms_are_listed_in_the_order_they_were_given()
    {
        var registry = new TransformRegistry([Named("b"), Named("a"), Named("c")]);

        registry.All.Select(transform => transform.Id).ShouldBe(["b", "a", "c"]);
    }

    [Fact]
    public void A_transform_is_found_by_its_identifier()
    {
        var registry = new TransformRegistry([Named("a"), Named("b")]);

        registry.Find("b")!.Id.ShouldBe("b");
    }

    [Fact]
    public void An_unknown_identifier_finds_nothing()
    {
        new TransformRegistry([Named("a")]).Find("zzz").ShouldBeNull();
    }

    [Fact]
    public void The_identifier_is_matched_exactly()
    {
        new TransformRegistry([Named("case.upper")]).Find("CASE.UPPER").ShouldBeNull();
    }

    [Fact]
    public void Two_transforms_with_one_identifier_are_a_bug()
    {
        Should.Throw<ArgumentException>(() => new TransformRegistry([Named("a"), Named("a")]));
    }

    [Fact]
    public void A_missing_list_or_identifier_is_a_bug()
    {
        Should.Throw<ArgumentNullException>(() => new TransformRegistry(null!));
        Should.Throw<ArgumentNullException>(() => new TransformRegistry([]).Find(null!));
    }

    [Fact]
    public void The_default_registry_lists_every_built_in_transform_that_needs_no_input_in_picker_order()
    {
        TransformRegistry.Default.All.Select(transform => transform.Id).ShouldBe(
        [
            "text.plain", "case.upper", "case.lower", "case.title", "case.sentence", "text.trim", "text.single-line",
            "json.format", "json.minify", "url.encode", "url.decode", "base64.encode", "base64.decode",
        ]);
    }

    [Fact]
    public void Every_default_transform_works_on_an_empty_text_and_on_a_text_with_a_lone_line_break()
    {
        foreach (var transform in TransformRegistry.Default.All)
        {
            // Some may refuse (JSON of nothing), but none may throw.
            _ = transform.Apply(string.Empty);
            _ = transform.Apply("\r\n");
        }
    }

    [Fact]
    public void An_empty_registry_is_allowed()
    {
        new TransformRegistry([]).All.ShouldBeEmpty();
    }
}
