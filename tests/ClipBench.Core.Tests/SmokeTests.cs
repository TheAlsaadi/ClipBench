using System.Reflection;

namespace ClipBench.Core.Tests;

public class SmokeTests
{
    [Fact]
    public void Core_assembly_loads_and_has_the_expected_name()
    {
        var assembly = Assembly.Load("ClipBench.Core");

        assembly.GetName().Name.ShouldBe("ClipBench.Core");
    }
}
