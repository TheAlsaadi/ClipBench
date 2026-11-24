using System.Diagnostics;
using ClipBench.Core.Clips;
using ClipBench.Storage.Tests.Support;

namespace ClipBench.Storage.Tests.Performance;

/// <summary>
/// How long the largest history the settings allow (10,000 clips) takes to open and read. These measure time, so they
/// are tagged Integration: the CI unit run skips them and they run on a real machine (docs/PERFORMANCE.md).
/// </summary>
[Trait("Category", "Integration")]
public sealed class LargeHistoryTests : IAsyncLifetime
{
    private const int TextClips = 9_900;
    private const int ImageClips = 100;
    private const int Runs = 5;

    private readonly TempDirectory _directory = new();
    private readonly FakeKeyProtector _protector = new();
    private readonly FakeTimeProvider _clock = new();
    private readonly ITestOutputHelper _output = TestContext.Current.TestOutputHelper!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private StoragePaths Paths => new(_directory.Path);

    public async ValueTask InitializeAsync()
    {
        var opened = await ClipStorage.OpenAsync(Paths, _protector, _clock, Ct);
        await using var storage = opened.Value;
        var random = new Random(9);
        for (var index = 0; index < TextClips + ImageClips; index++)
        {
            ClipContent content;
            if (index % 100 == 50)
            {
                var image = new byte[1024 * 1024];
                random.NextBytes(image);
                content = new ImageContent(image);
            }
            else
            {
                content = new TextContent($"Clip {index}: " + new string((char)('a' + (index % 26)), 40 + (index % 400)));
            }

            var saved = await storage.Repository.SaveAsync(new Clip(ClipId.New(), content, _clock.GetUtcNow().AddSeconds(-index)), Ct);
            saved.IsSuccess.ShouldBeTrue();
        }
    }

    public ValueTask DisposeAsync()
    {
        _directory.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Opening_and_reading_10000_clips_with_100_images_takes_under_500_ms()
    {
        var times = new List<long>();
        for (var run = 0; run < Runs; run++)
        {
            var stopwatch = Stopwatch.StartNew();
            var opened = await ClipStorage.OpenAsync(Paths, _protector, _clock, Ct);
            await using var storage = opened.Value;
            var loaded = await storage.Repository.LoadAllAsync(Ct);
            stopwatch.Stop();

            loaded.IsSuccess.ShouldBeTrue();
            loaded.Value.Items.Length.ShouldBe(TextClips + ImageClips);
            times.Add(stopwatch.ElapsedMilliseconds);
        }

        var median = times.Order().ElementAt(Runs / 2);
        _output.WriteLine($"open and read 10,000 clips: median {median} ms, runs {string.Join(", ", times)} ms");
        median.ShouldBeLessThan(500);
    }
}
