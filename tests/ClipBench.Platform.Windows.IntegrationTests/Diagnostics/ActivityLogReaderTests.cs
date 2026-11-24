using System.Text;
using ClipBench.Core.Capture;
using ClipBench.Core.Clips;
using ClipBench.Core.Diagnostics;
using ClipBench.Core.Results;
using ClipBench.Platform.Windows.Diagnostics;
using ClipBench.Platform.Windows.IntegrationTests.Support;

namespace ClipBench.Platform.Windows.IntegrationTests.Diagnostics;

public sealed class ActivityLogReaderTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "clipbench-logreader-" + Guid.NewGuid().ToString("N"));
    private readonly ManualTimeProvider _time = new();

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private void AppendRaw(string line) =>
        File.AppendAllText(Path.Combine(_directory, FileActivityLog.FileName), line + "\r\n", new UTF8Encoding(false));

    [Fact]
    public void Every_detail_the_log_can_hold_has_a_place_in_the_lines_the_reader_accepts()
    {
        // A detail added to ActivityDetails and written by the log, but unknown to the reader, would make every line that
        // carries it vanish from reports.
        ActivityLogReader.DetailKeys.Count.ShouldBe(typeof(ActivityDetails).GetProperties().Length);
    }

    [Fact]
    public async Task Every_line_the_log_writes_is_read_back()
    {
        var log = new FileActivityLog(_directory, _time);
        var full = new ActivityDetails
        {
            Error = ErrorCode.StorageFailure,
            Format = ClipFormat.Image,
            Bytes = 2048,
            Count = -1,
            Rejection = CaptureRejection.TooLarge,
            ExceptionType = typeof(InvalidOperationException),
            Source = CrashSource.UiThread,
            Milliseconds = 42,
        };
        var events = Enum.GetValues<ActivityEvent>();
        foreach (var activity in events)
        {
            log.Write(ActivityLevel.Warning, activity, full);
            log.Write(ActivityLevel.Information, activity);
        }

        var excerpt = await ActivityLogReader.ReadRecentAsync(_directory, maxLines: 1000, cancellationToken: Ct);

        excerpt.Dropped.ShouldBe(0);
        excerpt.Lines.Count.ShouldBe(events.Length * 2);
    }

    [Fact]
    public async Task Lines_the_log_could_not_have_written_are_left_out_and_counted()
    {
        var log = new FileActivityLog(_directory, _time);
        log.Write(ActivityLevel.Information, ActivityEvent.AppStarted);
        AppendRaw("2026-01-01T00:00:00Z INF CaptureStored hello world");
        log.Write(ActivityLevel.Information, ActivityEvent.CaptureStored, new ActivityDetails { Format = ClipFormat.Text, Bytes = 12 });
        AppendRaw("2026-01-01T00:00:00.0000000+00:00 INF CaptureStored format=Text bytes=12 text=secret");
        AppendRaw("2026-01-01T00:00:00.0000000+00:00 INF NoSuchEvent");
        AppendRaw("2026-01-01T00:00:00.0000000+00:00 INF CaptureStored format=Secret");
        log.Write(ActivityLevel.Error, ActivityEvent.AppCrashed, new ActivityDetails { ExceptionType = typeof(ArgumentException), Source = CrashSource.Process });

        var excerpt = await ActivityLogReader.ReadRecentAsync(_directory, cancellationToken: Ct);

        excerpt.Lines.Count.ShouldBe(3);
        excerpt.Lines[0].ShouldEndWith(" INF AppStarted");
        excerpt.Lines[1].ShouldEndWith(" INF CaptureStored format=Text bytes=12");
        excerpt.Lines[2].ShouldEndWith(" ERR AppCrashed exception=System.ArgumentException source=Process");
        excerpt.Dropped.ShouldBe(4);
        excerpt.Lines.ShouldAllBe(line => !line.Contains("secret", StringComparison.OrdinalIgnoreCase) && !line.Contains("hello", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Only_the_last_lines_are_read_across_rotated_files_oldest_first()
    {
        // Small files, so the lines spread over the current file and two rotated ones.
        var log = new FileActivityLog(_directory, _time, maxBytesPerFile: 512, maxFiles: 3);
        for (var i = 0; i < 30; i++)
        {
            log.Write(ActivityLevel.Information, ActivityEvent.HistoryLoaded, new ActivityDetails { Count = i });
        }

        Directory.GetFiles(_directory).Length.ShouldBe(3);

        var excerpt = await ActivityLogReader.ReadRecentAsync(_directory, maxLines: 5, cancellationToken: Ct);

        excerpt.Lines.Select(line => line[(line.LastIndexOf('=') + 1)..]).ShouldBe(["25", "26", "27", "28", "29"]);
        excerpt.Dropped.ShouldBe(0);
    }

    [Fact]
    public async Task Two_hundred_lines_are_read_by_default()
    {
        var log = new FileActivityLog(_directory, _time);
        for (var i = 0; i < 250; i++)
        {
            log.Write(ActivityLevel.Information, ActivityEvent.QueuePasted, new ActivityDetails { Count = i });
        }

        (await ActivityLogReader.ReadRecentAsync(_directory, cancellationToken: Ct)).Lines.Count.ShouldBe(200);
    }

    [Fact]
    public async Task A_missing_log_folder_gives_an_empty_excerpt()
    {
        var excerpt = await ActivityLogReader.ReadRecentAsync(_directory, cancellationToken: Ct);

        excerpt.Lines.ShouldBeEmpty();
        excerpt.Dropped.ShouldBe(0);
    }

    [Fact]
    public async Task A_file_full_of_noise_gives_no_lines_and_a_count()
    {
        _ = Directory.CreateDirectory(_directory);
        AppendRaw("first line of a letter");
        AppendRaw(string.Empty);
        AppendRaw(new string('x', 10_000));

        var excerpt = await ActivityLogReader.ReadRecentAsync(_directory, cancellationToken: Ct);

        excerpt.Lines.ShouldBeEmpty();
        excerpt.Dropped.ShouldBe(2);
    }
}
