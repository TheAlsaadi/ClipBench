using ArchUnitNET.Domain;
using ArchUnitNET.Loader;

namespace ClipBench.ArchitectureTests;

/// <summary>The built source assemblies, loaded once for every rule that inspects their types and dependencies.</summary>
internal static class SourceArchitecture
{
    private static readonly Lazy<Architecture> _architecture = new(() =>
        new ArchLoader()
            .LoadAssemblies(SourceAssemblies.Names.Select(name => System.Reflection.Assembly.LoadFrom(SourceAssemblies.PathOf(name))).ToArray())
            .Build());

    /// <summary>Gets the loaded architecture.</summary>
    public static Architecture Value => _architecture.Value;
}
