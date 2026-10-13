namespace ClipBench.UiSmokeTests;

// FlaUI-driven tests arrive in Phase 4. They launch the built App executable, so
// this project needs no assembly reference to it. This test proves the runner works.
public class SmokeTests
{
    [Fact]
    public void Test_runner_executes_in_this_project()
    {
        typeof(SmokeTests).Assembly.GetName().Name.ShouldBe("ClipBench.UiSmokeTests");
    }
}
