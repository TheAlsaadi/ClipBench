using System.Reflection;
using System.Text.RegularExpressions;
using ClipBench.Core.Diagnostics;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace ClipBench.ArchitectureTests;

/// <summary>
/// Security baseline S6: what the user copied never reaches a log, a crash record or a notice. The activity log is the
/// only place the app writes diagnostics, it takes numbers, codes and types only, and no exception message (which can
/// quote what was being read) is ever turned into text.
/// </summary>
public partial class LoggingSafetyTests
{
    // Types that write diagnostics somewhere other than the activity log: the console, the debugger output, the trace
    // listeners, the Windows event log, and the Microsoft.Extensions.Logging pipeline.
    private const string DiagnosticWriters =
        @"^(System\.Console|System\.Diagnostics\.(Debug|Trace|TraceSource|EventLog|Debugger)|Microsoft\.Extensions\.Logging\..*)$";

    // A .Message or .StackTrace read. The receiver is captured so that a ResultError's message (fixed text written
    // by ClipBench) can be told apart from an exception's.
    [GeneratedRegex(@"(?<receiver>[A-Za-z_][A-Za-z0-9_]*)\.(?<member>Message|StackTrace)\b")]
    private static partial Regex MemberReads();

    [GeneratedRegex(@"//[^\r\n]*")]
    private static partial Regex LineComments();

    public static TheoryData<string> AssemblyNames => new(SourceAssemblies.Names);

    [Theory]
    [MemberData(nameof(AssemblyNames))]
    public void No_src_assembly_writes_diagnostics_except_the_activity_log(string assemblyName)
    {
        var assembly = SourceArchitecture.Value.Assemblies.Single(candidate => candidate.Name == assemblyName);

        var violations = Types().That().ResideInAssembly(assembly)
            .Should().NotDependOnAnyTypesThat().HaveFullNameMatching(DiagnosticWriters)
            .WithoutRequiringPositiveResults()
            .Evaluate(SourceArchitecture.Value)
            .Where(result => !result.Passed)
            .Select(result => result.Description)
            .ToList();

        violations.ShouldBeEmpty();
    }

    [Fact]
    public void ActivityDetails_has_no_string_field()
    {
        var offenders = typeof(ActivityDetails)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => !(property.PropertyType.IsValueType || property.PropertyType == typeof(Type)))
            .Select(property => $"{property.Name}: {property.PropertyType.Name}")
            .ToList();

        offenders.ShouldBeEmpty("a log detail that can hold text can hold what the user copied");
    }

    [Fact]
    public void No_exception_message_or_stack_trace_is_read_anywhere_in_the_source()
    {
        // Only a ResultError's message may be read: it is fixed text that ClipBench writes, never a quote of the input.
        // By convention its receiver is named error, or ends in Error (result.Error, parsed.Error).
        var offenders = from file in Directory.EnumerateFiles(RepositoryRoot.Combine("src"), "*.cs", SearchOption.AllDirectories)
                        let relative = Path.GetRelativePath(RepositoryRoot.Path, file).Replace('\\', '/')
                        where !relative.Contains("/obj/", StringComparison.Ordinal) && !relative.Contains("/bin/", StringComparison.Ordinal)
                        from Match read in MemberReads().Matches(LineComments().Replace(File.ReadAllText(file), string.Empty))
                        let receiver = read.Groups["receiver"].Value
                        where !(read.Groups["member"].Value == "Message" && (receiver == "error" || receiver == "Error"))
                        select $"{relative}: {read.Value}";

        offenders.ShouldBeEmpty();
    }
}
