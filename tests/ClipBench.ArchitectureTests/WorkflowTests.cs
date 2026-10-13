using System.Text.RegularExpressions;

namespace ClipBench.ArchitectureTests;

/// <summary>Security baseline S12: workflows are pinned, minimal and free of script injection.</summary>
public partial class WorkflowTests
{
    public static TheoryData<string> WorkflowFiles => new(ListWorkflows());

    // owner/name@<40 hex>, optionally followed by a "# vX.Y.Z" comment. Local actions (./) are fine.
    [GeneratedRegex(@"^\s*-?\s*uses:\s*(?<ref>\S+)", RegexOptions.Multiline)]
    private static partial Regex UsesLine();

    [GeneratedRegex(@"^[A-Za-z0-9_.-]+/[A-Za-z0-9_./-]+@[0-9a-f]{40}$")]
    private static partial Regex PinnedReference();

    // An expression that reads event data is only allowed on an env mapping line (KEY: ${{ ... }}),
    // never inside a script, where a crafted title or branch name could inject commands.
    [GeneratedRegex(@"\$\{\{[^}]*(github\.event\.|github\.head_ref)", RegexOptions.None)]
    private static partial Regex EventExpression();

    [GeneratedRegex(@"^\s+[A-Za-z_][A-Za-z0-9_]*:\s+\$\{\{.*\}\}\s*$")]
    private static partial Regex EnvMappingLine();

    // The concurrency group may read the pull request number, which is numeric and cannot inject.
    [GeneratedRegex(@"^\s+group:\s+[A-Za-z0-9_-]+-\$\{\{\s*github\.event\.pull_request\.number\s*\|\|\s*github\.ref\s*\}\}\s*$")]
    private static partial Regex ConcurrencyGroupLine();

    private static string[] ListWorkflows()
    {
        var folder = RepositoryRoot.Combine(".github", "workflows");
        return Directory.Exists(folder)
            ? [.. Directory.GetFiles(folder, "*.yml").Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal)]
            : [];
    }

    private static string Read(string workflow) => File.ReadAllText(RepositoryRoot.Combine(".github", "workflows", workflow));

    [Fact]
    public void The_expected_workflows_exist()
    {
        ListWorkflows().ShouldBe(["ci.yml", "codeql.yml", "dependency-review.yml", "scorecard.yml"]);
    }

    [Theory]
    [MemberData(nameof(WorkflowFiles))]
    public void Every_action_is_pinned_to_a_full_commit_sha(string workflow)
    {
        var unpinned = UsesLine().Matches(Read(workflow))
            .Select(match => match.Groups["ref"].Value)
            .Where(reference => !reference.StartsWith("./", StringComparison.Ordinal) && !PinnedReference().IsMatch(reference))
            .ToList();

        unpinned.ShouldBeEmpty("pin every action by its full commit sha");
    }

    [Theory]
    [MemberData(nameof(WorkflowFiles))]
    public void Workflow_declares_read_only_top_level_permissions(string workflow)
    {
        var text = Read(workflow);

        text.ShouldMatch(@"(?m)^permissions:\s*\r?\n\s+contents:\s+read\s*$");
        text.ShouldNotContain("write-all");
        text.ShouldNotContain("read-all");
    }

    [Theory]
    [MemberData(nameof(WorkflowFiles))]
    public void Workflow_does_not_use_pull_request_target(string workflow)
    {
        Read(workflow).ShouldNotContain("pull_request_target");
    }

    [Theory]
    [MemberData(nameof(WorkflowFiles))]
    public void Event_data_never_reaches_a_script(string workflow)
    {
        var offenders = Read(workflow).Split('\n')
            .Where(line => EventExpression().IsMatch(line) && !EnvMappingLine().IsMatch(line) && !ConcurrencyGroupLine().IsMatch(line))
            .Select(line => line.Trim())
            .ToList();

        offenders.ShouldBeEmpty("pass event data through env: and read it as a variable");
    }

    [Theory]
    [MemberData(nameof(WorkflowFiles))]
    public void Every_job_has_a_timeout(string workflow)
    {
        // Only the jobs: section holds jobs; the on: section also has two-space keys such as push:.
        var text = Read(workflow);
        var start = Regex.Match(text, @"(?m)^jobs:\s*$");
        start.Success.ShouldBeTrue("a workflow needs a jobs: section");
        var jobsSection = text[start.Index..];

        var jobs = Regex.Count(jobsSection, @"(?m)^  [A-Za-z0-9_-]+:\s*$");
        var withTimeout = Regex.Count(jobsSection, @"(?m)^    timeout-minutes:\s+\d+\s*$");

        jobs.ShouldBeGreaterThan(0);
        withTimeout.ShouldBe(jobs, "each job needs timeout-minutes so a hung job cannot run for six hours");
    }

    [Fact]
    public void Ci_workflow_defines_the_required_status_checks()
    {
        var text = Read("ci.yml");

        text.ShouldMatch(@"(?m)^  attribution-guard:\s*$");
        text.ShouldMatch(@"(?m)^  build-test:\s*$");
        text.ShouldContain("cancel-in-progress: true");
    }

    [Fact]
    public void Ci_workflow_opts_out_of_test_runner_telemetry_and_restores_locked()
    {
        var text = Read("ci.yml");

        text.ShouldContain("TESTINGPLATFORM_TELEMETRY_OPTOUT");
        text.ShouldContain("DOTNET_CLI_TELEMETRY_OPTOUT");
        text.ShouldContain("dotnet restore --locked-mode");
        text.ShouldContain("-warnaserror");
    }
}
