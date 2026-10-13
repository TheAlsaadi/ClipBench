using ClipBench.Core.Results;

namespace ClipBench.Core.Tests.Results;

public class ResultTests
{
    [Fact]
    public void A_success_carries_its_value()
    {
        var result = Result.Success(42);

        result.IsSuccess.ShouldBeTrue();
        result.IsFailure.ShouldBeFalse();
        result.Value.ShouldBe(42);
    }

    [Fact]
    public void A_failure_carries_its_error()
    {
        var error = new ResultError(ErrorCode.NothingSelected, "Select at least one clip.");

        var result = Result.Failure<int>(error);

        result.IsSuccess.ShouldBeFalse();
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(error);
    }

    [Fact]
    public void Reading_the_value_of_a_failure_is_a_bug_and_throws()
    {
        var result = Result.Failure<string>(new ResultError(ErrorCode.NothingSelected, "none"));

        Should.Throw<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public void Reading_the_error_of_a_success_is_a_bug_and_throws()
    {
        var result = Result.Success("ok");

        Should.Throw<InvalidOperationException>(() => result.Error);
    }

    [Fact]
    public void An_uninitialized_result_is_a_failure_and_never_a_success()
    {
        Result<string> result = default;

        result.IsSuccess.ShouldBeFalse();
        result.Error.Code.ShouldBe(ErrorCode.Unspecified);
    }

    [Fact]
    public void A_result_without_a_value_can_succeed()
    {
        var result = Result.Success();

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void A_result_without_a_value_can_fail()
    {
        var error = new ResultError(ErrorCode.QueueNotArmed, "Arm the queue first.");

        var result = Result.Failure(error);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(error);
    }

    [Fact]
    public void An_uninitialized_result_without_a_value_is_a_failure()
    {
        Result result = default;

        result.IsSuccess.ShouldBeFalse();
    }

    [Fact]
    public void Printing_a_result_never_reveals_the_value_or_the_message()
    {
        // A result may carry clip content, and results end up in logs and test output.
        Result.Success("secret-marker").ToString().ShouldNotContain("secret-marker");
        Result.Failure<string>(new ResultError(ErrorCode.NothingSelected, "secret-marker")).ToString()
            .ShouldBe("Failure(NothingSelected)");
        Result.Success().ToString().ShouldBe("Success");
        Result.Failure(new ResultError(ErrorCode.QueueNotArmed, "secret-marker")).ToString()
            .ShouldBe("Failure(QueueNotArmed)");
    }

    [Fact]
    public void Results_with_the_same_content_are_equal()
    {
        Result.Success(7).ShouldBe(Result.Success(7));
        Result.Success(7).ShouldNotBe(Result.Success(8));
        Result.Failure<int>(new ResultError(ErrorCode.NothingSelected, "a"))
            .ShouldBe(Result.Failure<int>(new ResultError(ErrorCode.NothingSelected, "a")));
        Result.Success(7).ShouldNotBe(Result.Failure<int>(new ResultError(ErrorCode.NothingSelected, "a")));
    }
}
