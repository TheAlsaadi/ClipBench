using ClipBench.Core.Boards;
using ClipBench.Core.History;
using ClipBench.Presentation.Boards;

namespace ClipBench.Presentation.Tests.Support;

/// <summary>Builds sidebars for popup tests.</summary>
internal static class TestSidebars
{
    public static SidebarViewModel New(ClipLibrary library, FakeBoardStore? store = null) =>
        New(library, new BoardCatalog(library, store ?? new FakeBoardStore()));

    public static SidebarViewModel New(ClipLibrary library, BoardCatalog catalog) =>
        new(catalog, library, new ImmediateDispatcher());
}
