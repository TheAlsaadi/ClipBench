using System.Reflection;

namespace ClipBench.Platform.Windows.IntegrationTests;

// Clipboard-touching tests added in Phase 3 carry [Trait("Category", "Integration")].
// This smoke test touches no OS state, so it runs everywhere.
public class SmokeTests
{
    [Fact]
    public void Platform_assembly_loads_and_has_the_expected_name()
    {
        var assembly = Assembly.Load("ClipBench.Platform.Windows");

        assembly.GetName().Name.ShouldBe("ClipBench.Platform.Windows");
    }
}
