using ClipBench.Core.Diagnostics;
using ClipBench.Core.Transforms;
using ClipBench.Presentation.Transforms;

namespace ClipBench.Presentation.Tests.Support;

/// <summary>Builds transform pickers for popup tests.</summary>
internal static class TestPickers
{
    public static TransformPickerViewModel New(IActivityLog? log = null) =>
        new(TransformRegistry.Default, new TransformPipeline(), log ?? new FakeActivityLog());
}
