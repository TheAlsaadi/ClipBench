using System.Text.RegularExpressions;

namespace ClipBench.ArchitectureTests;

/// <summary>
/// Security checks S4 and Phase 2: there is no SQL injection surface. Every statement is constant text and every
/// value reaches the database as a named parameter. The compiler's CA2100 rule is switched on as an error for the
/// storage library, and this test scans its source for the patterns CA2100 cannot see.
/// </summary>
public partial class SqlSafetyTests
{
    // The only two places that assign a variable to CommandText. Each must carry a CA2100 suppression right above
    // it, and each is fed by code the compiler checks: Execute takes a [ConstantExpected] argument (CA1857), and
    // migrations are constants of the assembly.
    private static readonly (string File, string Expression)[] _reviewedAssignments =
    [
        ("src/ClipBench.Storage/Clips/ClipRepository.cs", "sql"),
        ("src/ClipBench.Storage/Sqlite/SchemaMigrator.cs", "migration.Sql"),
    ];

    // A whole string literal is read first, because SQL ends in a semicolon inside the quotes. Anything that is not
    // a plain literal is read up to the next semicolon, which is enough to show it as an offender.
    [GeneratedRegex(@"\bCommandText\s*=\s*(?<value>""(?:[^""\\]|\\.)*""|[^;]+);")]
    private static partial Regex CommandTextAssignment();

    // A plain string literal (an interpolated one starts with $" and does not match) or a PascalCase name, which
    // must then be a const. A $ inside the quotes is fine: it marks a named SQL parameter.
    [GeneratedRegex(@"^(?:""(?:[^""\\]|\\.)*""|(?<name>[A-Z][A-Za-z0-9]*))$")]
    private static partial Regex ConstantSql();

    [GeneratedRegex(@"\bconst\s+string\s+(?<name>[A-Za-z0-9_]+)")]
    private static partial Regex ConstStringDeclaration();

    [GeneratedRegex(@"\bnew\s+SqliteCommand\s*\(")]
    private static partial Regex CommandConstructor();

    private static IEnumerable<(string Path, string Text)> StorageSources() =>
        Directory.GetFiles(RepositoryRoot.Combine("src", "ClipBench.Storage"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(path => (Path.GetRelativePath(RepositoryRoot.Path, path).Replace('\\', '/'), File.ReadAllText(path)));

    [Fact]
    public void The_scan_looks_at_the_storage_source()
    {
        // Guards against the rules below passing only because they found nothing to read.
        var sources = StorageSources().ToList();

        sources.Count.ShouldBeGreaterThan(5);
        sources.Sum(source => CommandTextAssignment().Count(source.Text)).ShouldBeGreaterThan(10);
    }

    [Fact]
    public void Every_command_text_is_a_literal_or_a_constant_or_a_reviewed_exception()
    {
        var offenders = new List<string>();
        foreach (var (path, text) in StorageSources())
        {
            var constants = ConstStringDeclaration().Matches(text).Select(match => match.Groups["name"].Value).ToHashSet(StringComparer.Ordinal);
            foreach (Match assignment in CommandTextAssignment().Matches(text))
            {
                var value = assignment.Groups["value"].Value.Trim();
                var constant = ConstantSql().Match(value);
                var isConstant = constant.Success && (!constant.Groups["name"].Success || constants.Contains(constant.Groups["name"].Value));
                if (isConstant || IsReviewedException(path, value, text, assignment.Index))
                {
                    continue;
                }

                offenders.Add($"{path}: CommandText = {value}");
            }
        }

        offenders.ShouldBeEmpty("SQL must be a literal or a const string with named parameters, never built at run time");
    }

    [Fact]
    public void No_command_is_built_from_a_string_argument()
    {
        var offenders = StorageSources()
            .Where(source => CommandConstructor().IsMatch(source.Text))
            .Select(source => source.Path)
            .ToList();

        offenders.ShouldBeEmpty("create commands with CreateCommand() and set CommandText from a constant");
    }

    [Fact]
    public void Storage_turns_the_SQL_injection_rule_into_an_error()
    {
        var config = File.ReadAllText(RepositoryRoot.Combine("src", "ClipBench.Storage", ".editorconfig"));

        config.ShouldMatch(@"(?m)^dotnet_diagnostic\.CA2100\.severity\s*=\s*error\s*$");
    }

    [Fact]
    public void Every_reviewed_exception_still_exists_and_carries_its_suppression()
    {
        foreach (var (file, expression) in _reviewedAssignments)
        {
            var text = File.ReadAllText(RepositoryRoot.Combine(file.Split('/')));
            var index = text.IndexOf($"CommandText = {expression};", StringComparison.Ordinal);

            index.ShouldBeGreaterThanOrEqualTo(0, $"{file} no longer assigns {expression}; remove it from the reviewed list");
            PreviousLine(text, index).ShouldContain("#pragma warning disable CA2100");
        }
    }

    private static bool IsReviewedException(string path, string value, string text, int index) =>
        _reviewedAssignments.Any(entry => entry.File == path && entry.Expression == value)
        && PreviousLine(text, index).Contains("#pragma warning disable CA2100", StringComparison.Ordinal);

    private static string PreviousLine(string text, int index)
    {
        var lineStart = text.LastIndexOf('\n', Math.Max(0, index - 1)) + 1;
        var previousEnd = lineStart - 1;
        if (previousEnd < 0)
        {
            return string.Empty;
        }

        var previousStart = text.LastIndexOf('\n', Math.Max(0, previousEnd - 1)) + 1;
        return text[previousStart..previousEnd];
    }
}
