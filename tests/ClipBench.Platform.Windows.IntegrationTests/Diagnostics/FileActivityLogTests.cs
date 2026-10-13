using System.Text;
using ClipBench.Core.Capture;
using ClipBench.Core.Clips;
using ClipBench.Core.Diagnostics;
using ClipBench.Core.History;
using ClipBench.Core.Results;
using ClipBench.Platform.Windows.Diagnostics;
using ClipBench.Platform.Windows.IntegrationTests.Support;

namespace ClipBench.Platform.Windows.IntegrationTests.Diagnostics;

public sealed class FileActivityLogTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "clipbench-logtests-" + Guid.NewGuid().ToString("N"));
    private readonly ManualTimeProvider _time = new();

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private FileActivityLog Create(long maxBytes = 1024 * 1024, int maxFiles = 3) => new(_directory, _time, maxBytes, maxFiles);

    private string[] Lines(string name = FileActivityLog.FileName) =>
        File.Exists(Path.Combine(_directory, name)) ? File.ReadAllLines(Path.Combine(_directory, name)) : [];

    private static readonly string[] _fourFiles = ["clipbench.log", "clipbench.1.log", "clipbench.2.log", "clipbench.3.log"];

    // The number after the last '=' of the last line of a file: these tests write their line number as the byte count.
    private int LastByteCount(string name)
    {
        var last = Lines(name).Last();
        return int.Parse(last[(last.LastIndexOf('=') + 1)..], System.Globalization.CultureInfo.InvariantCulture);
    }

    private string[] LogFiles() =>
        Directory.Exists(_directory) ? [.. Directory.GetFiles(_directory).Select(file => Path.GetFileName(file)).Order(StringComparer.Ordinal)] : [];

    // --- what a line holds ---

    [Fact]
    public void A_line_holds_the_time_the_level_and_the_event()
    {
        var log = Create();

        log.Write(ActivityLevel.Information, ActivityEvent.CaptureListening);

        Lines().ShouldBe(["2026-10-13T09:00:00.0000000+00:00 INF CaptureListening"]);
    }

    [Theory]
    [InlineData(ActivityLevel.Information, "INF")]
    [InlineData(ActivityLevel.Warning, "WRN")]
    [InlineData(ActivityLevel.Error, "ERR")]
    public void Every_level_has_a_short_name(ActivityLevel level, string name)
    {
        Create().Write(level, ActivityEvent.CaptureListening);

        Lines().Single().ShouldContain($" {name} ");
    }

    [Fact]
    public void Details_are_written_as_names_and_numbers_and_unset_ones_are_left_out()
    {
        var log = Create();

        log.Write(
            ActivityLevel.Error,
            ActivityEvent.StoreFailed,
            new ActivityDetails
            {
                Error = ErrorCode.StorageFailure,
                Format = ClipFormat.Image,
                Bytes = 2048,
                Rejection = CaptureRejection.TooLarge,
                ExceptionType = typeof(InvalidOperationException),
            });
        log.Write(ActivityLevel.Information, ActivityEvent.CaptureStored, new ActivityDetails { Format = ClipFormat.Text, Bytes = 12 });

        var lines = Lines();
        lines[0].ShouldEndWith("StoreFailed error=StorageFailure format=Image bytes=2048 rejection=TooLarge exception=System.InvalidOperationException");
        lines[1].ShouldEndWith("CaptureStored format=Text bytes=12");
    }

    [Fact]
    public void The_time_comes_from_the_clock_it_was_given()
    {
        var log = Create();
        log.Write(ActivityLevel.Information, ActivityEvent.CaptureListening);
        _time.Advance(TimeSpan.FromMinutes(5));

        log.Write(ActivityLevel.Information, ActivityEvent.CaptureListening);

        var lines = Lines();
        lines[0].ShouldStartWith("2026-10-13T09:00:00");
        lines[1].ShouldStartWith("2026-10-13T09:05:00");
    }

    [Fact]
    public void Lines_are_added_in_order_and_the_folder_is_created_on_the_first_write()
    {
        Directory.Exists(_directory).ShouldBeFalse();
        var log = Create();

        log.Write(ActivityLevel.Information, ActivityEvent.CaptureListening);
        log.Write(ActivityLevel.Warning, ActivityEvent.CaptureFailed, new ActivityDetails { Error = ErrorCode.ClipboardBusy });
        log.Write(ActivityLevel.Information, ActivityEvent.PasteSucceeded);

        Lines().Select(line => line.Split(' ')[2]).ShouldBe(["CaptureListening", "CaptureFailed", "PasteSucceeded"]);
    }

    // --- size limits ---

    [Fact]
    public void A_full_file_is_rotated_and_only_the_newest_files_are_kept()
    {
        var log = Create(maxBytes: 300, maxFiles: 3);

        for (var i = 0; i < 40; i++)
        {
            log.Write(ActivityLevel.Information, ActivityEvent.CaptureStored, new ActivityDetails { Format = ClipFormat.Text, Bytes = i });
        }

        LogFiles().ShouldBe(["clipbench.1.log", "clipbench.2.log", "clipbench.log"]);
        Lines().Last().ShouldEndWith("bytes=39");
        Lines("clipbench.1.log").Last().ShouldEndWith("bytes=" + (39 - Lines().Length));
    }

    [Fact]
    public void With_more_files_each_one_moves_up_one_place_so_older_lines_are_in_higher_numbers()
    {
        var log = Create(maxBytes: 300, maxFiles: 4);

        for (var i = 0; i < 80; i++)
        {
            log.Write(ActivityLevel.Information, ActivityEvent.CaptureStored, new ActivityDetails { Bytes = i });
        }

        LogFiles().ShouldBe(["clipbench.1.log", "clipbench.2.log", "clipbench.3.log", "clipbench.log"]);
        var lastOf = _fourFiles.Select(LastByteCount).ToArray();
        lastOf[0].ShouldBe(79);
        lastOf.ShouldBe([.. lastOf.OrderByDescending(value => value)], "each older file ends with an older line");
        lastOf.Distinct().Count().ShouldBe(4);
    }

    [Fact]
    public void The_files_together_stay_within_the_limit()
    {
        const int MaxBytes = 400;
        var log = Create(maxBytes: MaxBytes, maxFiles: 3);

        for (var i = 0; i < 500; i++)
        {
            log.Write(ActivityLevel.Information, ActivityEvent.CaptureStored, new ActivityDetails { Format = ClipFormat.Html, Bytes = i });
        }

        LogFiles().Length.ShouldBeLessThanOrEqualTo(3);
        foreach (var file in Directory.GetFiles(_directory))
        {
            new FileInfo(file).Length.ShouldBeLessThanOrEqualTo(MaxBytes);
        }
    }

    [Fact]
    public void A_single_file_log_starts_over_instead_of_keeping_old_files()
    {
        var log = Create(maxBytes: 300, maxFiles: 1);

        for (var i = 0; i < 30; i++)
        {
            log.Write(ActivityLevel.Information, ActivityEvent.CaptureStored, new ActivityDetails { Bytes = i });
        }

        LogFiles().ShouldBe(["clipbench.log"]);
        new FileInfo(Path.Combine(_directory, "clipbench.log")).Length.ShouldBeLessThanOrEqualTo(300);
    }

    [Theory]
    [InlineData(255L, 3)]
    [InlineData(1024L, 0)]
    public void Limits_that_make_no_sense_are_refused(long maxBytes, int maxFiles)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Create(maxBytes, maxFiles));
    }

    // --- trouble ---

    [Fact]
    public void A_location_that_cannot_be_written_never_throws()
    {
        Directory.CreateDirectory(_directory);
        var blocker = Path.Combine(_directory, "not-a-folder");
        File.WriteAllText(blocker, "a file where the log folder should be");
        var log = new FileActivityLog(Path.Combine(blocker, "logs"), _time);

        Should.NotThrow(() => log.Write(ActivityLevel.Error, ActivityEvent.StoreFailed));
    }

    [Fact]
    public async Task Writes_from_many_threads_all_arrive_whole()
    {
        var log = Create();

        await Task.WhenAll(Enumerable.Range(0, 8).Select(thread => Task.Run(
            () =>
            {
                for (var i = 0; i < 100; i++)
                {
                    log.Write(ActivityLevel.Information, ActivityEvent.CaptureStored, new ActivityDetails { Format = ClipFormat.Text, Bytes = thread * 1000 + i });
                }
            },
            Ct)));

        var lines = Lines();
        lines.Length.ShouldBe(800);
        lines.ShouldAllBe(line => line.Contains(" INF CaptureStored format=Text bytes="));
        lines.Select(line => line[(line.LastIndexOf('=') + 1)..]).Distinct().Count().ShouldBe(800);
    }

    // --- content ---

    private sealed class MemoryStore : IClipStore
    {
        public Task<Result<StoredClips>> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(Result.Success(new StoredClips([], 0)));

        public Task<Result> SaveAsync(Clip clip, CancellationToken cancellationToken) => Task.FromResult(Result.Success());

        public Task<Result> SetPinnedAsync(ClipId id, bool pinned, CancellationToken cancellationToken) => Task.FromResult(Result.Success());

        public Task<Result> DeleteAsync(IReadOnlyCollection<ClipId> ids, CancellationToken cancellationToken) => Task.FromResult(Result.Success());
    }

    private sealed class ScriptedListener : IClipboardListener
    {
        public event EventHandler<ClipboardChangedEventArgs>? Changed;

        public Task<Result> StartAsync(CancellationToken cancellationToken) => Task.FromResult(Result.Success());

        public void Raise(long sequence) => Changed?.Invoke(this, new ClipboardChangedEventArgs(sequence));

        public void Dispose()
        {
        }
    }

    private sealed class ScriptedReader(Func<long, Result<ClipboardCapture>> next) : IClipboardReader
    {
        private long _reads;

        public Task<Result<ClipboardCapture>> ReadAsync(CaptureFilter filter, CancellationToken cancellationToken) =>
            Task.FromResult(next(Interlocked.Increment(ref _reads)));
    }

    [Fact]
    public async Task Nothing_a_user_copied_ever_reaches_the_log_files()
    {
        const string Marker = "ZEBRA-4f2a91c7-marker";
        const string Source = "secretprogram.exe";
        const string Jwt = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIiwibmFtZSI6IkpvaG4gRG9lIiwiaWF0IjoxNTE2MjM5MDIyfQ.SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c";

        var log = Create();
        var listener = new ScriptedListener();
        var reader = new ScriptedReader(read => read switch
        {
            1 => Result.Success(ClipboardCapture.Accepted(1, new TextContent(Marker + " plain text"), Source)),
            2 => Result.Success(ClipboardCapture.Accepted(2, new TextContent(Jwt + Marker), Source)),
            3 => Result.Success(ClipboardCapture.Accepted(3, new FilesContent([@"C:\Users\" + Marker + @"\file.txt"]), Source)),
            4 => Result.Success(ClipboardCapture.Rejected(4, CaptureRejection.PrivacyMarker)),
            5 => throw new InvalidOperationException("the message quotes " + Marker),
            _ => Result.Failure<ClipboardCapture>(new ResultError(ErrorCode.ClipboardEmpty, Marker)),
        });
        var library = new ClipLibrary(new ClipHistory(new HistoryOptions(), _time), new MemoryStore());
        using var service = new CaptureService(listener, reader, library, log, _time);
        (await service.StartAsync(Ct)).IsSuccess.ShouldBeTrue();

        for (var sequence = 1; sequence <= 6; sequence++)
        {
            listener.Raise(sequence);
            await WaitForLinesAsync(1 + sequence);
        }

        var allText = string.Concat(Directory.GetFiles(_directory).Select(File.ReadAllText));
        allText.Length.ShouldBeGreaterThan(0);
        allText.ShouldNotContain(Marker);
        allText.ShouldNotContain(Source);
        allText.ShouldNotContain("eyJ");
        var bytes = Directory.GetFiles(_directory).SelectMany(File.ReadAllBytes).ToArray();
        bytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes(Marker)).ShouldBe(-1);
        bytes.AsSpan().IndexOf(Encoding.Unicode.GetBytes(Marker)).ShouldBe(-1);
        library.Dispose();
    }

    private async Task WaitForLinesAsync(int count)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (Lines().Length >= count)
            {
                return;
            }

            await Task.Delay(10, Ct);
        }

        throw new TimeoutException($"the log never reached {count} lines");
    }
}
