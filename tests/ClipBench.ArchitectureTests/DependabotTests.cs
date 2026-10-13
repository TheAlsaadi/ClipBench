namespace ClipBench.ArchitectureTests;

/// <summary>
/// Dependabot opens one pull request per dependency. The CodeQL steps (init, analyze, upload-sarif) must all run the
/// same version, so a lone bump of one of them fails the build. They have to be updated in a single group.
/// </summary>
public class DependabotTests
{
    private static string Block(string ecosystem)
    {
        var text = File.ReadAllText(RepositoryRoot.Combine(".github", "dependabot.yml"));
        var blocks = text.Split("- package-ecosystem:", StringSplitOptions.RemoveEmptyEntries);

        return blocks.Single(block => block.TrimStart().StartsWith(ecosystem, StringComparison.Ordinal));
    }

    [Fact]
    public void The_CodeQL_action_steps_are_updated_together_in_one_group()
    {
        var block = Block("github-actions");

        block.ShouldContain("groups:");
        block.ShouldMatch(@"(?m)^\s+patterns:\s*\r?\n\s+- ['""]?github/codeql-action\*['""]?\s*$");
    }

    [Theory]
    [InlineData("nuget")]
    [InlineData("github-actions")]
    public void Each_ecosystem_is_checked_every_week(string ecosystem)
    {
        Block(ecosystem).ShouldMatch(@"(?m)^\s+interval:\s+weekly\s*$");
    }
}
