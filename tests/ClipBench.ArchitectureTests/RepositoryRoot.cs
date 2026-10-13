namespace ClipBench.ArchitectureTests;

/// <summary>Locates the repository root so tests can inspect project and config files.</summary>
internal static class RepositoryRoot
{
    private static readonly Lazy<string> _path = new(Find);

    /// <summary>Gets the absolute path of the repository root (the folder that holds global.json).</summary>
    public static string Path => _path.Value;

    /// <summary>Combines the repository root with the given relative segments.</summary>
    public static string Combine(params string[] segments) =>
        System.IO.Path.Combine([Path, .. segments]);

    private static string Find()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(System.IO.Path.Combine(directory.FullName, "global.json")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("global.json not found in any parent of " + AppContext.BaseDirectory);
    }
}
