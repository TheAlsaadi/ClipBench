using ClipBench.Core.Results;
using ClipBench.Core.Transforms;

namespace ClipBench.Core.Tests.Transforms;

public class TransformPipelineTests
{
    private readonly TransformPipeline _pipeline = new();

    private static TextTransform Appending(string id, string suffix) =>
        new(id, id, "test", text => Result.Success(text + suffix));

    private static TextTransform Failing(string name, ErrorCode code, string message) =>
        new("test.fail", name, "test", _ => Result.Failure<string>(new ResultError(code, message)));

    [Fact]
    public void An_empty_chain_returns_the_text_as_it_is()
    {
        var result = _pipeline.Run("<b>keep</b>", []);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe("<b>keep</b>");
    }

    [Fact]
    public void Transforms_run_in_the_order_given_each_on_the_result_of_the_one_before()
    {
        var result = _pipeline.Run("x", [Appending("a", "1"), Appending("b", "2"), Appending("c", "3")]);

        result.Value.ShouldBe("x123");
    }

    [Fact]
    public void The_order_matters()
    {
        var upperThenSuffix = new TextTransform("up", "Up", "test", text => Result.Success(text.ToUpperInvariant()));

        _pipeline.Run("a", [upperThenSuffix, Appending("s", "b")]).Value.ShouldBe("Ab");
        _pipeline.Run("a", [Appending("s", "b"), upperThenSuffix]).Value.ShouldBe("AB");
    }

    [Fact]
    public void The_first_failure_stops_the_chain_and_names_the_transform_that_failed()
    {
        var later = false;
        var after = new TextTransform("after", "After", "test", text =>
        {
            later = true;
            return Result.Success(text);
        });

        var result = _pipeline.Run("x", [Appending("a", "1"), Failing("JSON format", ErrorCode.TransformInputInvalid, "The text is not valid JSON."), after]);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ErrorCode.TransformInputInvalid);
        result.Error.Message.ShouldBe("JSON format: The text is not valid JSON.");
        later.ShouldBeFalse();
    }

    [Fact]
    public void Text_over_the_limit_is_refused_before_any_transform_runs()
    {
        var ran = false;
        var spy = new TextTransform("spy", "Spy", "test", text =>
        {
            ran = true;
            return Result.Success(text);
        });

        var result = new TransformPipeline(maxCharacters: 5).Run("123456", [spy]);

        result.Error.Code.ShouldBe(ErrorCode.OutputTooLarge);
        ran.ShouldBeFalse();
    }

    [Fact]
    public void Text_of_exactly_the_limit_is_accepted()
    {
        new TransformPipeline(maxCharacters: 5).Run("12345", []).Value.ShouldBe("12345");
    }

    [Fact]
    public void A_step_whose_output_goes_over_the_limit_fails_and_names_the_step()
    {
        var doubling = new TextTransform("double", "Double", "test", text => Result.Success(text + text));

        var result = new TransformPipeline(maxCharacters: 5).Run("123", [doubling]);

        result.Error.Code.ShouldBe(ErrorCode.OutputTooLarge);
        result.Error.Message.ShouldStartWith("Double: ");
    }

    [Fact]
    public void An_empty_text_is_fine()
    {
        _pipeline.Run(string.Empty, [Appending("a", "!")]).Value.ShouldBe("!");
    }

    [Fact]
    public void A_missing_argument_is_a_bug()
    {
        Should.Throw<ArgumentNullException>(() => _pipeline.Run(null!, []));
        Should.Throw<ArgumentNullException>(() => _pipeline.Run("x", null!));
    }

    [Fact]
    public void A_limit_below_one_is_a_bug()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new TransformPipeline(0));
    }
}
