using System.Diagnostics;
using ClipBench.Platform.Windows.Screen;

namespace ClipBench.Platform.Windows.IntegrationTests.Screen;

/// <summary>
/// How long copying text from the screen takes with the recognizer the app really uses: one Windows engine per profile
/// language, read one after another, then Tesseract. Phase 8 left this to be measured. It measures time, so it is tagged
/// Integration and runs on a real machine (docs/PERFORMANCE.md).
/// </summary>
[Trait("Category", "Integration")]
public sealed class OcrLatencyTests
{
    private const int Runs = 5;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Reading_a_1000_by_100_region_with_every_profile_language_takes_under_1_s()
    {
        var recognizer = WindowsOcr.CreateRecognizer();
        using var picture = WindowsOcrEngineTests.Render("Invoice 2026-10 for ACME Corp: total 1,250.00 > budget", 28, "Segoe UI", width: 1000, height: 100);

        // The first read starts the engines; the user pays that once per run of the app, so it is measured on its own.
        var first = Stopwatch.StartNew();
        (await recognizer.RecognizeAsync(picture, Ct)).IsSuccess.ShouldBeTrue();
        first.Stop();

        var times = new List<long>();
        for (var run = 0; run < Runs; run++)
        {
            var stopwatch = Stopwatch.StartNew();
            var read = await recognizer.RecognizeAsync(picture, Ct);
            stopwatch.Stop();
            read.IsSuccess.ShouldBeTrue();
            times.Add(stopwatch.ElapsedMilliseconds);
        }

        var median = times.Order().ElementAt(Runs / 2);
        TestContext.Current.TestOutputHelper!.WriteLine(
            $"OCR of 1000x100 with the profile languages ({string.Join(", ", global::Windows.System.UserProfile.GlobalizationPreferences.Languages)}) and Tesseract: first {first.ElapsedMilliseconds} ms, then median {median} ms ({string.Join(", ", times)})");
        first.ElapsedMilliseconds.ShouldBeLessThan(3000);
        median.ShouldBeLessThan(1000);
    }
}
