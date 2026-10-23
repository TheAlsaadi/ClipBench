using ClipBench.Core.Snippets;
using ClipBench.Presentation.Snippets;

namespace ClipBench.Presentation.Tests.Support;

/// <summary>Builds snippet view models for popup tests.</summary>
internal static class TestSnippets
{
    public static SnippetsViewModel New(SnippetCatalog? catalog = null) =>
        new(catalog ?? new SnippetCatalog(new FakeSnippetStore()), new SnippetEngine(new SnippetTestClock()));
}
