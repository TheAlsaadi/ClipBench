using ClipBench.Platform.Windows.Diagnostics;

namespace ClipBench.Platform.Windows.IntegrationTests.Diagnostics;

public sealed class WindowsErrorReportingTests
{
    [Fact]
    public void ExcludeHeap_sets_the_no_heap_flag_for_this_process()
    {
        WindowsErrorReporting.ExcludeHeap().IsSuccess.ShouldBeTrue();

        WindowsErrorReporting.IsHeapExcluded().ShouldBeTrue();
    }

    [Fact]
    public void ExcludeHeap_twice_keeps_the_flag()
    {
        WindowsErrorReporting.ExcludeHeap().IsSuccess.ShouldBeTrue();
        WindowsErrorReporting.ExcludeHeap().IsSuccess.ShouldBeTrue();

        WindowsErrorReporting.IsHeapExcluded().ShouldBeTrue();
    }
}
