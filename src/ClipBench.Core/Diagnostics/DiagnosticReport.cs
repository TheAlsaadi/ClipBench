using System.Globalization;
using System.Text;

namespace ClipBench.Core.Diagnostics;

/// <summary>
/// The facts a diagnostic report states. Versions come from the program and Windows; everything the user made is a
/// count, so no clip, board name, snippet or excluded program can be named in a report.
/// </summary>
/// <param name="AppVersion">The version of ClipBench.</param>
/// <param name="WindowsVersion">The version of Windows.</param>
/// <param name="Architecture">The processor architecture the app runs as.</param>
/// <param name="ClipCount">How many clips the history holds.</param>
/// <param name="PinnedCount">How many of them are pinned.</param>
/// <param name="BoardCount">How many boards there are.</param>
/// <param name="SnippetCount">How many snippets there are.</param>
/// <param name="ExcludedAppCount">How many programs are on the exclusion list.</param>
public sealed record DiagnosticFacts(
    string AppVersion,
    string WindowsVersion,
    string Architecture,
    int ClipCount,
    int PinnedCount,
    int BoardCount,
    int SnippetCount,
    int ExcludedAppCount);

/// <summary>Writes the text of the report the user can choose to save and attach to a problem report.</summary>
public static class DiagnosticReport
{
    private const string NewLine = "\r\n";

    /// <summary>Builds the report.</summary>
    /// <param name="facts">The versions and counts.</param>
    /// <param name="logLines">The recent log lines, oldest first, already checked to be lines the log itself wrote.</param>
    /// <param name="droppedLines">How many lines were left out because the log could not have written them.</param>
    /// <returns>The report, with Windows line endings.</returns>
    public static string Build(DiagnosticFacts facts, IReadOnlyList<string> logLines, int droppedLines)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(logLines);
        ArgumentOutOfRangeException.ThrowIfNegative(droppedLines);

        var report = new StringBuilder();
        void Line(string text) => report.Append(text).Append(NewLine);

        Line("ClipBench diagnostic report");
        Line("This file holds versions, counts and recent log lines. It never contains anything you copied.");
        Line(string.Empty);
        Line("ClipBench version: " + facts.AppVersion);
        Line("Windows version: " + facts.WindowsVersion);
        Line("Architecture: " + facts.Architecture);
        Line("Clips: " + Number(facts.ClipCount));
        Line("Pinned clips: " + Number(facts.PinnedCount));
        Line("Boards: " + Number(facts.BoardCount));
        Line("Snippets: " + Number(facts.SnippetCount));
        Line("Excluded programs: " + Number(facts.ExcludedAppCount));
        Line(string.Empty);

        var heading = $"Recent log ({Number(logLines.Count)} lines";
        heading += droppedLines switch
        {
            0 => "):",
            1 => "; 1 line was left out because ClipBench could not have written it):",
            _ => $"; {Number(droppedLines)} lines were left out because ClipBench could not have written them):",
        };
        Line(heading);
        if (logLines.Count == 0)
        {
            Line("(empty)");
        }

        foreach (var line in logLines)
        {
            Line(line);
        }

        return report.ToString();
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
