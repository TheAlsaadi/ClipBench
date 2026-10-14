namespace ClipBench.UiSmokeTests;

/// <summary>
/// Not tagged: it keeps the project in the runs that skip integration tests, which would otherwise find no test here
/// and fail. It proves the runner works; the real tests are in <see cref="SmokeTests"/>.
/// </summary>
public class RunnerTests
{
    [Fact]
    public void The_test_runner_executes_in_this_project()
    {
        typeof(RunnerTests).Assembly.GetName().Name.ShouldBe("ClipBench.UiSmokeTests");
    }
}
