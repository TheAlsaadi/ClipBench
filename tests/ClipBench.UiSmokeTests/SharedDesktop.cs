namespace ClipBench.UiSmokeTests;

/// <summary>
/// The smoke test classes share one desktop, one keyboard and the same shortcuts, so they run one after another, never
/// side by side: two copies of the app would fight over F24.
/// </summary>
[CollectionDefinition(Name)]
public sealed class SharedDesktop
{
    public const string Name = "Desktop";
}
