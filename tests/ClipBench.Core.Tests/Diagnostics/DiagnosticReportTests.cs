using ClipBench.Core.Diagnostics;

namespace ClipBench.Core.Tests.Diagnostics;

public sealed class DiagnosticReportTests
{
    private static readonly DiagnosticFacts _facts = new(
        AppVersion: "1.2.3",
        WindowsVersion: "10.0.26300",
        Architecture: "X64",
        ClipCount: 12,
        PinnedCount: 3,
        BoardCount: 2,
        SnippetCount: 4,
        ExcludedAppCount: 5);

    private static readonly string[] _logLines =
    [
        "2026-11-20T10:00:00.0000000+00:00 INF AppStarted ms=420",
        "2026-11-20T10:00:01.0000000+00:00 INF HistoryLoaded count=12",
    ];

    [Fact]
    public void The_report_lists_the_facts_and_then_the_log_lines_in_order()
    {
        var report = DiagnosticReport.Build(_facts, _logLines, droppedLines: 0);

        var expected = string.Join(
            "\r\n",
            "ClipBench diagnostic report",
            "This file holds versions, counts and recent log lines. It never contains anything you copied.",
            string.Empty,
            "ClipBench version: 1.2.3",
            "Windows version: 10.0.26300",
            "Architecture: X64",
            "Clips: 12",
            "Pinned clips: 3",
            "Boards: 2",
            "Snippets: 4",
            "Excluded programs: 5",
            string.Empty,
            "Recent log (2 lines):",
            _logLines[0],
            _logLines[1],
            string.Empty);
        report.ShouldBe(expected);
    }

    [Fact]
    public void The_report_gives_only_the_number_of_excluded_programs()
    {
        // The facts can hold counts and versions only: no list, so no program name can get into a report.
        var properties = typeof(DiagnosticFacts).GetProperties();

        properties.ShouldAllBe(property => property.PropertyType == typeof(int) || property.PropertyType == typeof(string));
        properties.Where(property => property.PropertyType == typeof(string)).Select(property => property.Name)
            .ShouldBe(["AppVersion", "WindowsVersion", "Architecture"], ignoreOrder: true);
    }

    [Theory]
    [InlineData(1, "Recent log (2 lines; 1 line was left out because ClipBench could not have written it):")]
    [InlineData(3, "Recent log (2 lines; 3 lines were left out because ClipBench could not have written them):")]
    public void The_report_says_how_many_lines_were_left_out(int dropped, string heading)
    {
        var report = DiagnosticReport.Build(_facts, _logLines, dropped);

        report.ShouldContain("\r\n" + heading + "\r\n");
    }

    [Fact]
    public void An_empty_log_says_so()
    {
        var report = DiagnosticReport.Build(_facts, [], droppedLines: 0);

        report.ShouldEndWith("Recent log (0 lines):\r\n(empty)\r\n");
    }

    [Fact]
    public void A_negative_number_of_dropped_lines_is_a_bug_in_the_caller()
    {
        _ = Should.Throw<ArgumentOutOfRangeException>(() => DiagnosticReport.Build(_facts, _logLines, -1));
    }
}
