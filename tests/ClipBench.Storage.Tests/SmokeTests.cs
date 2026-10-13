using System.Reflection;

namespace ClipBench.Storage.Tests;

public class SmokeTests
{
    [Fact]
    public void Storage_assembly_loads_and_has_the_expected_name()
    {
        var assembly = Assembly.Load("ClipBench.Storage");

        assembly.GetName().Name.ShouldBe("ClipBench.Storage");
    }
}
