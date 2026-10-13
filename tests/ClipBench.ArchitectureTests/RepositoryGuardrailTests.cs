using System.Text.Json;
using System.Xml.Linq;

namespace ClipBench.ArchitectureTests;

/// <summary>Security baseline S11: the toolchain and supply-chain settings cannot drift unnoticed.</summary>
public class RepositoryGuardrailTests
{
    private static XDocument LoadXml(string file) => XDocument.Load(RepositoryRoot.Combine(file));

    private static string Property(XDocument document, string name) => document.Descendants(name).Single().Value;

    [Fact]
    public void NuGet_restores_only_from_nuget_org_and_maps_every_package_to_it()
    {
        var document = LoadXml("nuget.config");
        var sources = document.Descendants("packageSources").Single();

        sources.Elements().First().Name.LocalName.ShouldBe("clear", "inherited sources must be cleared");
        var added = sources.Elements("add").ToList();
        added.Count.ShouldBe(1);
        added[0].Attribute("key")?.Value.ShouldBe("nuget.org");
        added[0].Attribute("value")?.Value.ShouldBe("https://api.nuget.org/v3/index.json");

        var mapped = document.Descendants("packageSourceMapping").Single().Elements("packageSource");
        mapped.Select(source => source.Attribute("key")?.Value).ShouldBe(["nuget.org"]);
    }

    [Theory]
    [InlineData("Nullable", "enable")]
    [InlineData("TreatWarningsAsErrors", "true")]
    [InlineData("AnalysisLevel", "latest-recommended")]
    [InlineData("EnforceCodeStyleInBuild", "true")]
    [InlineData("Deterministic", "true")]
    [InlineData("NuGetAudit", "true")]
    [InlineData("NuGetAuditMode", "all")]
    public void Directory_Build_props_sets_the_required_property(string property, string expected)
    {
        Property(LoadXml("Directory.Build.props"), property).ShouldBe(expected);
    }

    [Theory]
    [InlineData("ManagePackageVersionsCentrally", "true")]
    [InlineData("RestorePackagesWithLockFile", "true")]
    public void Directory_Packages_props_sets_the_required_property(string property, string expected)
    {
        Property(LoadXml("Directory.Packages.props"), property).ShouldBe(expected);
    }

    [Fact]
    public void Global_json_pins_the_SDK_feature_band_and_selects_the_Microsoft_Testing_Platform_runner()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(RepositoryRoot.Combine("global.json")));
        var sdk = document.RootElement.GetProperty("sdk");

        sdk.GetProperty("version").GetString().ShouldNotBeNull().ShouldMatch(@"^10\.0\.\d+$");
        sdk.GetProperty("rollForward").GetString().ShouldBeOneOf("patch", "latestPatch", "disable");
        sdk.GetProperty("allowPrerelease").GetBoolean().ShouldBeFalse();
        document.RootElement.GetProperty("test").GetProperty("runner").GetString().ShouldBe("Microsoft.Testing.Platform");
    }
}
