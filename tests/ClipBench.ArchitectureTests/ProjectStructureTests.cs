namespace ClipBench.ArchitectureTests;

/// <summary>
/// Enforces the dependency diagram of PLAN section 2.2 from the project files, so an unused
/// reference (which the compiler would drop from the assembly metadata) is still caught.
/// A new reference must be added here in the same commit, which makes it visible in review.
/// </summary>
public class ProjectStructureTests
{
    private const string WindowsFramework = "net10.0-windows10.0.19041.0";

    // name -> (allowed project references, allowed package references, target framework)
    private static readonly Dictionary<string, (string[] Projects, string[] Packages, string Framework)> _architecture = new()
    {
        ["ClipBench.Core"] = ([], [], "net10.0"),
        ["ClipBench.Storage"] = (["ClipBench.Core"], [], "net10.0"),
        ["ClipBench.Platform.Windows"] = (["ClipBench.Core"], [], WindowsFramework),
        ["ClipBench.Presentation"] = (["ClipBench.Core"], [], "net10.0"),
        ["ClipBench.App"] = (
            ["ClipBench.Platform.Windows", "ClipBench.Presentation", "ClipBench.Storage"],
            ["Microsoft.WindowsAppSDK"],
            WindowsFramework),
    };

    public static TheoryData<string> SourceProjects => new(_architecture.Keys);

    [Fact]
    public void Every_source_project_is_described_by_the_architecture()
    {
        var onDisk = ProjectFile.LoadAll("src").Select(project => project.Name);

        onDisk.Order(StringComparer.Ordinal).ShouldBe(_architecture.Keys.Order(StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(SourceProjects))]
    public void Source_project_references_only_the_projects_the_architecture_allows(string name)
    {
        var project = ProjectFile.Load("src", name);

        project.ProjectReferences.ShouldBe(_architecture[name].Projects.Order(StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(SourceProjects))]
    public void Source_project_references_only_the_packages_the_architecture_allows(string name)
    {
        var project = ProjectFile.Load("src", name);

        project.PackageReferences.ShouldBe(_architecture[name].Packages.Order(StringComparer.Ordinal));
        project.FrameworkReferences.ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(SourceProjects))]
    public void Source_project_targets_the_expected_framework(string name)
    {
        // Core, Storage and Presentation must stay free of Windows and UI types, so they
        // must never gain a -windows target framework.
        ProjectFile.Load("src", name).TargetFramework.ShouldBe(_architecture[name].Framework);
    }

    [Fact]
    public void No_project_pins_a_package_version_outside_Directory_Packages_props()
    {
        var offenders = ProjectFile.LoadAll("src").Concat(ProjectFile.LoadAll("tests"))
            .SelectMany(project => project.PackagesWithInlineVersion.Select(package => $"{project.Name}: {package}"))
            .ToList();

        offenders.ShouldBeEmpty("package versions are managed centrally in Directory.Packages.props");
    }

    [Fact]
    public void Every_project_has_a_committed_NuGet_lock_file()
    {
        var missing = ProjectFile.LoadAll("src").Concat(ProjectFile.LoadAll("tests"))
            .Where(project => !File.Exists(Path.Combine(Path.GetDirectoryName(project.Path)!, "packages.lock.json")))
            .Select(project => project.Name)
            .ToList();

        missing.ShouldBeEmpty("restore with --locked-mode needs packages.lock.json");
    }
}
