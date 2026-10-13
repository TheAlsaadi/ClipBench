namespace ClipBench.ArchitectureTests;

/// <summary>Finds the built source assemblies so rules can inspect their types and dependencies.</summary>
internal static class SourceAssemblies
{
    /// <summary>Gets the names of every assembly built from <c>src</c>.</summary>
    public static IReadOnlyList<string> Names { get; } =
    [
        "ClipBench.Core",
        "ClipBench.Storage",
        "ClipBench.Platform.Windows",
        "ClipBench.Presentation",
        "ClipBench.App",
    ];

    /// <summary>Gets the absolute path of the named assembly's DLL.</summary>
    public static string PathOf(string name)
    {
        // The libraries are project references, so they sit next to this test assembly.
        var beside = Path.Combine(AppContext.BaseDirectory, name + ".dll");
        if (File.Exists(beside))
        {
            return beside;
        }

        // The App is a self-contained executable and is deliberately not referenced.
        // Use its build output for the same configuration as this test run (bin/<Configuration>/<tfm>/).
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var binFolder = RepositoryRoot.Combine("src", name, "bin");
        var separator = Path.DirectorySeparatorChar;
        var candidates = Directory.Exists(binFolder)
            ? Directory.GetFiles(binFolder, name + ".dll", SearchOption.AllDirectories)
                .Where(path => path.Contains(separator + configuration + separator, StringComparison.Ordinal))
                .OrderBy(File.GetLastWriteTimeUtc)
                .ToList()
            : [];

        candidates.ShouldNotBeEmpty($"{name} has no {configuration} build output; run 'dotnet build -c {configuration}' first");
        return candidates[^1];
    }
}
