using System.Globalization;
using System.Text;
using ClipBench.Core.Capture;
using ClipBench.Core.Diagnostics;
using ClipBench.Core.Results;

namespace ClipBench.Platform.Windows.Diagnostics;

/// <summary>
/// Writes the activity log to a small set of text files that never grow past a fixed size. A line holds a time, a
/// level, an event name and the codes, sizes and types of <see cref="ActivityDetails"/>: there is nothing else to
/// write, so clip content cannot get in. A failing disk never reaches the caller.
/// </summary>
public sealed class FileActivityLog : IActivityLog
{
    /// <summary>The name of the file being written.</summary>
    public const string FileName = "clipbench.log";

    private const long MinimumBytesPerFile = 256;

    private static readonly UTF8Encoding _utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private readonly string _directory;
    private readonly TimeProvider _timeProvider;
    private readonly long _maxBytesPerFile;
    private readonly int _maxFiles;
    private readonly Lock _gate = new();

    /// <summary>Creates a log that writes into a folder.</summary>
    /// <param name="directory">The folder. It is created when the first line is written.</param>
    /// <param name="timeProvider">The clock for the time on each line.</param>
    /// <param name="maxBytesPerFile">When the file would grow past this, it is rotated first. At least 256.</param>
    /// <param name="maxFiles">How many files to keep, the current one included. At least 1.</param>
    public FileActivityLog(string directory, TimeProvider timeProvider, long maxBytesPerFile = 1024 * 1024, int maxFiles = 3)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxBytesPerFile, MinimumBytesPerFile);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxFiles, 1);
        _directory = directory;
        _timeProvider = timeProvider;
        _maxBytesPerFile = maxBytesPerFile;
        _maxFiles = maxFiles;
    }

    /// <inheritdoc/>
    public void Write(ActivityLevel level, ActivityEvent activity, ActivityDetails details = default)
    {
        var line = _utf8.GetBytes(Format(level, activity, details) + "\r\n");

        lock (_gate)
        {
            try
            {
                _ = Directory.CreateDirectory(_directory);
                var current = Path.Combine(_directory, FileName);
                var length = File.Exists(current) ? new FileInfo(current).Length : 0;
                if (length > 0 && length + line.Length > _maxBytesPerFile)
                {
                    Rotate(current);
                }

                using var stream = new FileStream(current, FileMode.Append, FileAccess.Write, FileShare.Read);
                stream.Write(line);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Logging must never take the app down. A line that cannot be written is lost.
            }
        }
    }

    private string Format(ActivityLevel level, ActivityEvent activity, ActivityDetails details)
    {
        var line = new StringBuilder()
            .Append(_timeProvider.GetUtcNow().ToString("O", CultureInfo.InvariantCulture))
            .Append(' ')
            .Append(level switch
            {
                ActivityLevel.Information => "INF",
                ActivityLevel.Warning => "WRN",
                ActivityLevel.Error => "ERR",
                _ => "???",
            })
            .Append(' ')
            .Append(activity);

        if (details.Error != ErrorCode.Unspecified)
        {
            _ = line.Append(" error=").Append(details.Error);
        }

        if (details.Format is { } format)
        {
            _ = line.Append(" format=").Append(format);
        }

        if (details.Bytes is { } bytes)
        {
            _ = line.Append(" bytes=").Append(bytes.ToString(CultureInfo.InvariantCulture));
        }

        if (details.Rejection != CaptureRejection.None)
        {
            _ = line.Append(" rejection=").Append(details.Rejection);
        }

        if (details.ExceptionType is { } exceptionType)
        {
            _ = line.Append(" exception=").Append(exceptionType.FullName ?? exceptionType.Name);
        }

        return line.ToString();
    }

    private void Rotate(string current)
    {
        if (_maxFiles == 1)
        {
            File.Delete(current);
            return;
        }

        File.Delete(Numbered(_maxFiles - 1));
        for (var i = _maxFiles - 2; i >= 1; i--)
        {
            var from = Numbered(i);
            if (File.Exists(from))
            {
                File.Move(from, Numbered(i + 1));
            }
        }

        File.Move(current, Numbered(1));
    }

    private string Numbered(int index) => Path.Combine(_directory, $"clipbench.{index.ToString(CultureInfo.InvariantCulture)}.log");
}
