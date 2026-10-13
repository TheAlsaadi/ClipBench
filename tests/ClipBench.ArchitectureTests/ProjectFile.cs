using System.Xml.Linq;

namespace ClipBench.ArchitectureTests;

/// <summary>A parsed SDK-style project file, used to check what a project is allowed to reference.</summary>
internal sealed class ProjectFile
{
    private readonly XDocument _document;

    private ProjectFile(string name, string path)
    {
        Name = name;
        Path = path;
        _document = XDocument.Load(path);
    }

    /// <summary>Gets the project name, for example <c>ClipBench.Core</c>.</summary>
    public string Name { get; }

    /// <summary>Gets the absolute path of the .csproj file.</summary>
    public string Path { get; }

    /// <summary>Gets the single TargetFramework value.</summary>
    public string TargetFramework => _document.Descendants("TargetFramework").Single().Value;

    /// <summary>Gets the names of the projects referenced through ProjectReference.</summary>
    public IReadOnlyList<string> ProjectReferences => Includes("ProjectReference")
        .Select(include => System.IO.Path.GetFileNameWithoutExtension(include.Replace('\\', '/')))
        .Order(StringComparer.Ordinal)
        .ToList();

    /// <summary>Gets the ids of the packages referenced through PackageReference.</summary>
    public IReadOnlyList<string> PackageReferences => Includes("PackageReference")
        .Order(StringComparer.Ordinal)
        .ToList();

    /// <summary>Gets the ids of PackageReference items that pin their own Version instead of using central management.</summary>
    public IReadOnlyList<string> PackagesWithInlineVersion => _document.Descendants("PackageReference")
        .Where(element => element.Attribute("Version") is not null || element.Attribute("VersionOverride") is not null)
        .Select(element => (string?)element.Attribute("Include") ?? "?")
        .ToList();

    /// <summary>Gets the ids of the FrameworkReference items.</summary>
    public IReadOnlyList<string> FrameworkReferences => Includes("FrameworkReference")
        .Order(StringComparer.Ordinal)
        .ToList();

    /// <summary>Loads a project under <c>src</c> or <c>tests</c> by name.</summary>
    public static ProjectFile Load(string folder, string name)
    {
        var path = RepositoryRoot.Combine(folder, name, name + ".csproj");
        File.Exists(path).ShouldBeTrue($"project file not found: {path}");
        return new ProjectFile(name, path);
    }

    /// <summary>Loads every project under the given folder (<c>src</c> or <c>tests</c>).</summary>
    public static IReadOnlyList<ProjectFile> LoadAll(string folder) =>
        Directory.GetDirectories(RepositoryRoot.Combine(folder))
            .Select(directory => new DirectoryInfo(directory).Name)
            .Order(StringComparer.Ordinal)
            .Select(name => Load(folder, name))
            .ToList();

    private IEnumerable<string> Includes(string elementName) => _document.Descendants(elementName)
        .Select(element => (string?)element.Attribute("Include"))
        .OfType<string>();
}
