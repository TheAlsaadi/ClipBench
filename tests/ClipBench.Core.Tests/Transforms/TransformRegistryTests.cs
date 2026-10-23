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
    public void An_empty_registry_is_allowed()
    {
        new TransformRegistry([]).All.ShouldBeEmpty();
    }
}
