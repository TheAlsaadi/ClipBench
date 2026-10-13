using ArchUnitNET.Domain;
using ArchUnitNET.Fluent;
using ArchUnitNET.Loader;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace ClipBench.ArchitectureTests;

/// <summary>Security baseline S1: no source assembly uses networking types.</summary>
public class NetworkRulesTests
{
    // Namespaces that only exist to talk over a network.
    private const string NetworkNamespaces = @"^System\.Net\.(Http|Sockets|WebSockets|Mail)(\..*)?$";

    // Network entry points that live in the plain System.Net namespace. WebUtility, IPAddress and
    // similar value types are deliberately not listed: they make no network calls.
    private const string NetworkClasses = @"^System\.Net\.(WebClient|WebRequest|HttpWebRequest|FtpWebRequest|Dns)$";

    private static readonly Lazy<Architecture> _architecture = new(() =>
        new ArchLoader()
            .LoadAssemblies(SourceAssemblies.Names.Select(name => System.Reflection.Assembly.LoadFrom(SourceAssemblies.PathOf(name))).ToArray())
            .Build());

    public static TheoryData<string> AssemblyNames => new(SourceAssemblies.Names);

    [Fact]
    public void The_loaded_architecture_contains_every_source_assembly()
    {
        // Guards against a rule passing only because its assembly was never loaded.
        var loaded = _architecture.Value.Assemblies.Select(assembly => assembly.Name).ToList();

        SourceAssemblies.Names.ShouldBeSubsetOf(loaded);
    }

    [Theory]
    [MemberData(nameof(AssemblyNames))]
    public void Source_assembly_does_not_depend_on_networking_types(string assemblyName)
    {
        // Pass the loaded Assembly object: the string overload matches the full display name.
        var assembly = _architecture.Value.Assemblies.Single(candidate => candidate.Name == assemblyName);

        // NotDependOnAnyTypesThat() is required: it also sees framework types that are referenced
        // but not loaded. NotDependOnAny(provider) silently misses them (proved by a mutation run).
        // An assembly that has no types yet has nothing to evaluate, which is not a failure.
        // Each rule starts from its own Types() call: the fluent builders share mutable state.
        var inNetworkNamespaces = Types().That().ResideInAssembly(assembly)
            .Should().NotDependOnAnyTypesThat().ResideInNamespaceMatching(NetworkNamespaces)
            .WithoutRequiringPositiveResults();
        var networkClasses = Types().That().ResideInAssembly(assembly)
            .Should().NotDependOnAnyTypesThat().HaveFullNameMatching(NetworkClasses)
            .WithoutRequiringPositiveResults();

        var violations = new[] { inNetworkNamespaces, networkClasses }
            .SelectMany(rule => rule.Evaluate(_architecture.Value))
            .Where(result => !result.Passed)
            .Select(result => result.Description)
            .ToList();

        violations.ShouldBeEmpty();
    }
}
