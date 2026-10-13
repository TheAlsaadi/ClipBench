using System.Text.RegularExpressions;

namespace ClipBench.ArchitectureTests;

/// <summary>Keeps decision records consistent and the public files free of tool credits and stale names.</summary>
public partial class DocumentationTests
{
    // Text files worth scanning. Binary output and build folders are skipped.
    private static readonly string[] _textExtensions =
    [
        ".md", ".yml", ".yaml", ".json", ".props", ".targets", ".csproj", ".cs", ".xaml", ".manifest",
        ".config", ".txt", ".iss", ".slnx", ".editorconfig", ".gitattributes", ".sh",
    ];

    private static readonly string[] _skippedFolders = ["bin", "obj", ".git", ".vs", "artifacts", "TestResults", "node_modules"];

    // Files and folders that must name the tool: the rules, the plan, the settings, the hook, the
    // guard that looks for it, and this test. Everything else is public and must not.
    private static readonly string[] _mayNameTheTool =
    [
        "CLAUDE.md",
        "PLAN.md",
        ".claude/",
        ".githooks/",
        ".gitignore",
        "scripts/",
        "tests/ClipBench.ArchitectureTests/DocumentationTests.cs",
    ];

    // The hook keeps a cosmetic mention of the old name, and the record of the rename names it.
    private static readonly string[] _mayNameTheOldProject =
    [
        ".githooks/",
        "docs/adr/0005-project-name-and-bootstrap.md",
        "tests/ClipBench.ArchitectureTests/DocumentationTests.cs",
    ];

    [GeneratedRegex(@"claude|anthropic", RegexOptions.IgnoreCase)]
    private static partial Regex ToolName();

    [GeneratedRegex(@"clipdeck", RegexOptions.IgnoreCase)]
    private static partial Regex OldProjectName();

    [GeneratedRegex(@"^(?<number>\d{4})-[a-z0-9-]+\.md$")]
    private static partial Regex AdrFileName();

    [GeneratedRegex(@"docs/adr/(?<file>\d{4}-[a-z0-9-]+\.md)")]
    private static partial Regex AdrPathReference();

    [GeneratedRegex(@"\bADR[ -](?<number>\d{4})\b")]
    private static partial Regex AdrNumberReference();

    [GeneratedRegex(@"\A---\r?\nstatus: (proposed|accepted|deprecated|superseded by ADR-\d{4})\r?\n---\r?\n")]
    private static partial Regex AdrFrontMatter();

    private static readonly Lazy<List<(string Path, string Text)>> _textFiles = new(() => [.. ReadTextFiles()]);

    // Read once: every test below scans the same files.
    private static List<(string Path, string Text)> TextFiles() => _textFiles.Value;

    private static IEnumerable<(string Path, string Text)> ReadTextFiles()
    {
        var root = RepositoryRoot.Path;
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            var skipped = relative.Split('/').SkipLast(1).Any(folder => _skippedFolders.Contains(folder));
            var extension = Path.GetExtension(file);
            var named = Path.GetFileName(file) is ".editorconfig" or ".gitattributes" or ".gitignore";
            if (skipped || (!named && !_textExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase)))
            {
                continue;
            }

            yield return (relative, File.ReadAllText(file));
        }
    }

    private static bool Matches(string relativePath, string[] entries) =>
        entries.Any(entry => entry.EndsWith('/') ? relativePath.StartsWith(entry, StringComparison.Ordinal) : relativePath == entry);

    private static List<string> AdrFiles() =>
        [.. Directory.GetFiles(RepositoryRoot.Combine("docs", "adr"), "*.md").Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal)];

    [Fact]
    public void Decision_records_are_numbered_without_gaps()
    {
        var files = AdrFiles();

        files.ShouldAllBe(name => AdrFileName().IsMatch(name));
        files.Select(name => int.Parse(AdrFileName().Match(name).Groups["number"].Value, System.Globalization.CultureInfo.InvariantCulture))
            .ShouldBe(Enumerable.Range(1, files.Count));
    }

    [Fact]
    public void Decision_records_have_a_status_and_a_title()
    {
        foreach (var name in AdrFiles())
        {
            var text = File.ReadAllText(RepositoryRoot.Combine("docs", "adr", name));

            AdrFrontMatter().IsMatch(text).ShouldBeTrue($"{name} must start with a status front matter block");
            text.ShouldMatch(@"(?m)^# \S", $"{name} must have a title");
        }
    }

    [Fact]
    public void Every_decision_record_that_a_document_refers_to_exists()
    {
        var numbers = AdrFiles().Select(name => name[..4]).ToHashSet(StringComparer.Ordinal);
        var missing = new List<string>();

        foreach (var (path, text) in TextFiles().Where(file => file.Path.EndsWith(".md", StringComparison.Ordinal) || file.Path.EndsWith(".yml", StringComparison.Ordinal)))
        {
            missing.AddRange(AdrPathReference().Matches(text)
                .Select(match => match.Groups["file"].Value)
                .Where(file => !File.Exists(RepositoryRoot.Combine("docs", "adr", file)))
                .Select(file => $"{path} -> docs/adr/{file}"));
            missing.AddRange(AdrNumberReference().Matches(text)
                .Select(match => match.Groups["number"].Value)
                .Where(number => !numbers.Contains(number))
                .Select(number => $"{path} -> ADR {number}"));
        }

        missing.ShouldBeEmpty();
    }

    [Fact]
    public void Public_files_do_not_credit_a_tool()
    {
        var offenders = TextFiles()
            .Where(file => !Matches(file.Path, _mayNameTheTool) && ToolName().IsMatch(file.Text))
            .Select(file => file.Path)
            .ToList();

        offenders.ShouldBeEmpty("the repository owner is the sole credited author");
    }

    [Fact]
    public void The_old_project_name_does_not_remain()
    {
        var offenders = TextFiles()
            .Where(file => !Matches(file.Path, _mayNameTheOldProject) && OldProjectName().IsMatch(file.Text))
            .Select(file => file.Path)
            .ToList();

        offenders.ShouldBeEmpty("the project was renamed to ClipBench");
    }
}
