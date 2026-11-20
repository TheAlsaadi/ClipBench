using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using ClipBench.Core.Clips;
using ClipBench.Storage;

namespace ClipBench.UiSmokeTests;

/// <summary>
/// The app on the largest history the settings allow: 10,000 clips, 100 of them pictures of about a megabyte. The times
/// and the memory it measures are written to the test output; docs/PERFORMANCE.md keeps them with the budgets.
/// </summary>
public sealed class LargeHistoryApp : AppUnderTest
{
    public const int Clips = 10_000;
    public const int Images = 100;

    /// <inheritdoc/>
    protected override string SettingsJson =>
        "{ \"version\": 1, \"firstRunCompleted\": true, \"captureTextHotkey\": \"F22\", \"historyMaxCount\": 10000, \"historyMaxMegabytes\": 1024 }";

    /// <inheritdoc/>
    protected override async Task SeedMoreAsync(ClipStorage storage, DateTimeOffset before)
    {
        var random = new Random(10_000);
        var texts = Clips - SeededTexts.Length - Images;
        for (var index = 0; index < texts + Images; index++)
        {
            ClipContent content = index % 100 == 50
                ? new ImageContent(NoisePng(random))
                : new TextContent($"Clip {index.ToString(CultureInfo.InvariantCulture)}: {Words(random)}");
            var saved = await storage.Repository.SaveAsync(new Clip(ClipId.New(), content, before.AddSeconds(-index), "notepad.exe"), TestContext.Current.CancellationToken);
            saved.IsSuccess.ShouldBeTrue();
        }
    }

    private static string Words(Random random)
    {
        string[] words = ["invoice", "meeting", "budget", "deploy", "review", "customer", "report", "update", "release", "notes"];
        return string.Join(' ', Enumerable.Range(0, random.Next(5, 60)).Select(_ => words[random.Next(words.Length)]));
    }

    // Random pixels do not compress, so a 600 by 450 picture is about a megabyte as a PNG, like a screenshot of a photo.
    private static byte[] NoisePng(Random random)
    {
        using var bitmap = new Bitmap(600, 450, PixelFormat.Format24bppRgb);
        var area = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(area, ImageLockMode.WriteOnly, bitmap.PixelFormat);
        var pixels = new byte[data.Stride * data.Height];
        random.NextBytes(pixels);
        System.Runtime.InteropServices.Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
        bitmap.UnlockBits(data);
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }
}

[Trait("Category", "Integration")]
[Collection(SharedDesktop.Name)]
public sealed class PerformanceSmokeTests(LargeHistoryApp app) : IClassFixture<LargeHistoryApp>
{
    private const int Runs = 5;

    // The measured private memory with 10,000 clips (docs/PERFORMANCE.md) plus a quarter, rounded up to 10 MB.
    private const long MemoryBudgetMegabytes = 370;

    private readonly ITestOutputHelper _output = TestContext.Current.TestOutputHelper!;

    private static long Median(IEnumerable<long> values)
    {
        var sorted = values.Order().ToList();
        return sorted[sorted.Count / 2];
    }

    private static IEnumerable<string> LogLines(string log, string activity) =>
        log.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.Split(' ').ElementAtOrDefault(2) == activity);

    private static long Milliseconds(string line) =>
        long.Parse(line[(line.IndexOf(" ms=", StringComparison.Ordinal) + 4)..].Split(' ')[0], CultureInfo.InvariantCulture);

    [Fact]
    public void Startup_with_10000_clips_is_ready_in_under_a_second()
    {
        var fromProcessStart = new List<long>();
        var fromMain = new List<long>();
        for (var run = 0; run < Runs; run++)
        {
            if (run > 0)
            {
                app.Restart();
            }

            var ready = LogLines(app.ReadLog(), "AppReady").Last();
            var readyAt = DateTimeOffset.Parse(ready.Split(' ')[0], CultureInfo.InvariantCulture);
            fromProcessStart.Add((long)(readyAt - new DateTimeOffset(app.Process.StartTime)).TotalMilliseconds);
            fromMain.Add(Milliseconds(ready));
        }

        _output.WriteLine($"ready, from process start: median {Median(fromProcessStart)} ms ({string.Join(", ", fromProcessStart)})");
        _output.WriteLine($"ready, from the first line of Main: median {Median(fromMain)} ms ({string.Join(", ", fromMain)})");
        Median(fromProcessStart).ShouldBeLessThan(1000);
    }

    [Fact]
    public void The_popup_opens_in_under_100_ms()
    {
        var before = LogLines(app.ReadLog(), "PopupShown").Count();
        for (var run = 0; run < Runs; run++)
        {
            _ = app.OpenPopup();
            AppUnderTest.WaitFor(() => LogLines(app.ReadLog(), "PopupShown").Count() > before + run, 5_000).ShouldBeTrue("the popup was not logged as shown");
            app.ClosePopup();
        }

        var times = LogLines(app.ReadLog(), "PopupShown").Skip(before).Select(Milliseconds).ToList();
        _output.WriteLine($"popup shown after its shortcut: median {Median(times)} ms ({string.Join(", ", times)})");
        Median(times).ShouldBeLessThan(100);
    }

    [Fact]
    public void Memory_after_loading_10000_clips_stays_within_its_budget()
    {
        // The popup is opened once, so the rows it builds are counted too.
        _ = app.OpenPopup();
        app.ClosePopup();
        app.Process.Refresh();
        var privateMegabytes = app.Process.PrivateMemorySize64 / (1024 * 1024);
        var workingSetMegabytes = app.Process.WorkingSet64 / (1024 * 1024);

        _output.WriteLine($"memory with 10,000 clips: private {privateMegabytes} MB, working set {workingSetMegabytes} MB");
        privateMegabytes.ShouldBeLessThanOrEqualTo(MemoryBudgetMegabytes);
    }
}
