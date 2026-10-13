using System.Reflection;

namespace ClipBench.ArchitectureTests;

public class SmokeTests
{
    [Theory]
    [InlineData("ClipBench.Core")]
    [InlineData("ClipBench.Storage")]
    [InlineData("ClipBench.Platform.Windows")]
    [InlineData("ClipBench.Presentation")]
    public void Source_assembly_loads_and_has_the_expected_name(string assemblyName)
    {
        var assembly = Assembly.Load(assemblyName);

        assembly.GetName().Name.ShouldBe(assemblyName);
    }
}
