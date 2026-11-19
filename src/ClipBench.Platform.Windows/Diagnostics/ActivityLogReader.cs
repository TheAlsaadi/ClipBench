using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ClipBench.Core.Capture;
using ClipBench.Core.Clips;
using ClipBench.Core.Diagnostics;
using ClipBench.Core.Results;

namespace ClipBench.Platform.Windows.Diagnostics;

/// <summary>The recent lines of the activity log, and how many were left out.</summary>
/// <param name="Lines">The lines, oldest first.</param>
/// <param name="Dropped">How many lines were left out because the log could not have written them.</param>
public sealed record LogExcerpt(IReadOnlyList<string> Lines, int Dropped);

/// <summary>
/// Reads back the end of the activity log for a diagnostic report. Only a line that <see cref="FileActivityLog"/> could
/// have written gets through: a time, a level, a known event and known details with numbers, names or a type. A line
/// added by someone else, or damaged, is left out and counted, so no text the log did not write reaches a report.
/// </summary>
public static class ActivityLogReader
{
    // A line can never be longer than this: a time, a level, an event and eight short details.
    private const int MaxLineLength = 1024;

    private static readonly Lazy<Regex> _wellFormed = new(BuildPattern);

    /// <summary>Reads the last lines of the log, across the rotated files, oldest first.</summary>
    /// <param name="directory">The log folder.</param>
    /// <param name="maxLines">How many of the newest lines to look at. At least 1.</param>
    /// <returns>The lines the log could have written, and how many of the newest lines were not.</returns>
    public static LogExcerpt ReadRecent(string directory, int maxLines = 200)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLines, 1);

        var newest = new Queue<string>(maxLines);
        foreach (var file in FilesOldestFirst(directory))
        {
            foreach (var line in ReadLines(file))
            {
                if (line.Length == 0)
                {
                    continue;
                }

                if (newest.Count == maxLines)
                {
                    _ = newest.Dequeue();
                }

                newest.Enqueue(line);
            }
        }

        var kept = newest.Where(IsWellFormed).ToList();
        return new LogExcerpt(kept, newest.Count - kept.Count);
    }

    private static bool IsWellFormed(string line) => line.Length <= MaxLineLength && _wellFormed.Value.IsMatch(line);

    private static IEnumerable<string> FilesOldestFirst(string directory)
    {
        if (!Directory.Exists(directory))
        {
            yield break;
        }

        // Rotated files are clipbench.1.log (newest) to clipbench.N.log (oldest); the current file is the newest of all.
        var rotated = Directory.GetFiles(directory, "clipbench.*.log")
            .Select(path => (Path: path, Index: RotationIndex(path)))
            .Where(file => file.Index > 0)
            .OrderByDescending(file => file.Index)
            .Select(file => file.Path);
        foreach (var path in rotated)
        {
            yield return path;
        }

        var current = Path.Combine(directory, FileActivityLog.FileName);
        if (File.Exists(current))
        {
            yield return current;
        }
    }

    private static int RotationIndex(string path)
    {
        var middle = Path.GetFileNameWithoutExtension(path)["clipbench.".Length..];
        return int.TryParse(middle, NumberStyles.None, CultureInfo.InvariantCulture, out var index) ? index : 0;
    }

    private static IEnumerable<string> ReadLines(string path)
    {
        string text;
        try
        {
            // The log may be writing: share the file rather than wait for it.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            text = reader.ReadToEnd();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A file that cannot be read adds nothing to the report.
            return [];
        }

        return text.Split('\n').Select(line => line.TrimEnd('\r'));
    }

    // Built from the enums the log writes, so a new event or error code is accepted without a change here, and a word
    // that is not one of them is not.
    private static Regex BuildPattern()
    {
        static string Names<T>()
            where T : struct, Enum => string.Join('|', Enum.GetNames<T>());

        var pattern =
            @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{1,7})?(Z|[+-]\d{2}:\d{2}) (INF|WRN|ERR) (" + Names<ActivityEvent>() + ")"
            + "( error=(" + Names<ErrorCode>() + "))?"
            + "( format=(" + Names<ClipFormat>() + "))?"
            + @"( bytes=-?\d{1,19})?"
            + @"( count=-?\d{1,10})?"
            + "( rejection=(" + Names<CaptureRejection>() + "))?"
            + @"( exception=[A-Za-z_][A-Za-z0-9_.+`]{0,200})?"
            + "( source=(" + Names<CrashSource>() + "))?"
            + @"( ms=-?\d{1,19})?$";
        return new Regex(pattern, RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(1));
    }
}
