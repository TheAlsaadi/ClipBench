using ClipBench.Core.Capture;
using ClipBench.Core.Results;
using ClipBench.Core.Screen;

namespace ClipBench.Core.Tests.Support;

/// <summary>Answers the selection the way the test says: a region, or a cancel.</summary>
internal sealed class FakeRegionPicker : IRegionPicker
{
    public Result<PixelRect> Answer { get; set; } = Result.Success(new PixelRect(10, 20, 300, 100));

    /// <summary>Runs inside the selection, so a test can hold it open.</summary>
    public Func<Task>? During { get; set; }

    public int Calls { get; private set; }

    public async Task<Result<PixelRect>> PickAsync(CancellationToken cancellationToken)
    {
        Calls++;
        if (During is { } hold)
        {
            await hold();
        }

        return Answer;
    }
}

/// <summary>Hands out images that remember whether they were let go of, and shows what the pixels held at that moment.</summary>
internal sealed class FakeScreenCapturer : IScreenCapturer
{
    public ResultError? Failure { get; set; }

    public List<PixelRect> Regions { get; } = [];

    public List<ScreenImage> Images { get; } = [];

    public Task<Result<ScreenImage>> CaptureAsync(PixelRect region, CancellationToken cancellationToken)
    {
        Regions.Add(region);
        if (Failure is { } error)
        {
            return Task.FromResult(Result.Failure<ScreenImage>(error));
        }

        var image = new ScreenImage(region.Width, region.Height, Enumerable.Repeat((byte)0xAB, region.Width * region.Height * 4).ToArray());
        Images.Add(image);
        return Task.FromResult(Result.Success(image));
    }
}

/// <summary>Answers a recognition with the text the test chose, and records what the pixels were while it ran.</summary>
internal sealed class FakeTextRecognizer : ITextRecognizer
{
    public Result<string> Answer { get; set; } = Result.Success("Hello from the screen");

    public int Calls { get; private set; }

    public bool PixelsWereIntactDuringTheCall { get; private set; }

    public Task<Result<string>> RecognizeAsync(ScreenImage image, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Calls++;
        PixelsWereIntactDuringTheCall = image.Pixels.Length > 0 && image.Pixels[0] == 0xAB;
        return Task.FromResult(Answer);
    }
}

/// <summary>Capture control with incognito that the test sets directly.</summary>
internal sealed class FakeIncognito : ICaptureControl
{
    public bool IsPaused { get; set; }

    public event EventHandler? PausedChanged
    {
        add { }
        remove { }
    }

    public void SetPaused(bool paused) => IsPaused = paused;
}
