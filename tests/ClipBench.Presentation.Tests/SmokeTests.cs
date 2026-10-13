using System.Reflection;

namespace ClipBench.Presentation.Tests;

public class SmokeTests
{
    [Fact]
    public void Presentation_assembly_loads_and_has_the_expected_name()
    {
        var assembly = Assembly.Load("ClipBench.Presentation");

        assembly.GetName().Name.ShouldBe("ClipBench.Presentation");
    }
}
