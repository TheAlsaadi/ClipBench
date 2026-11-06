using System.Drawing;
using System.Windows.Forms;
using ClipBench.Core.Results;
using ClipBench.Core.Screen;
using ClipBench.Platform.Windows.Screen;

namespace ClipBench.Platform.Windows.IntegrationTests.Screen;

/// <summary>The capture itself needs a real desktop, so these run on demand; the checks that need none are first.</summary>
public class GdiScreenCapturerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(0, 10)]
    [InlineData(10, 0)]
    [InlineData(-5, 10)]
    public async Task A_region_with_no_area_is_refused_without_touching_the_screen(int width, int height)
    {
        var result = await new GdiScreenCapturer().CaptureAsync(new PixelRect(0, 0, width, height), Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ErrorCode.ScreenCaptureFailed);
    }

    [Fact]
    public async Task A_region_larger_than_the_most_the_recognizer_reads_is_refused()
    {
        var result = await new GdiScreenCapturer().CaptureAsync(new PixelRect(0, 0, RegionMath.MaxSide + 1, 10), Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ErrorCode.ScreenCaptureFailed);
    }

    [Fact]
    public async Task A_cancelled_capture_throws_before_it_reads_anything()
    {
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => new GdiScreenCapturer().CaptureAsync(new PixelRect(0, 0, 10, 10), source.Token));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task The_pixels_of_a_real_window_are_copied_exactly_and_nothing_outside_the_region_is()
    {
        using var window = ColorWindow.Show(Color.FromArgb(18, 52, 86), new Rectangle(120, 120, 220, 120));
        var inside = new PixelRect(window.Bounds.X + 40, window.Bounds.Y + 30, 60, 40);

        var result = await new GdiScreenCapturer().CaptureAsync(inside, Ct);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : null);
        using var image = result.Value;
        image.Width.ShouldBe(60);
        image.Height.ShouldBe(40);
        image.Pixels.Length.ShouldBe(60 * 40 * 4);

        // Blue, green, red, then an opaque alpha, for every pixel: the recognizer treats alpha 0 as see-through.
        for (var i = 0; i < image.Pixels.Length; i += 4)
        {
            image.Pixels[i].ShouldBe((byte)86);
            image.Pixels[i + 1].ShouldBe((byte)52);
            image.Pixels[i + 2].ShouldBe((byte)18);
            image.Pixels[i + 3].ShouldBe((byte)255);
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task A_window_hidden_from_capture_is_not_in_the_pixels()
    {
        using var window = ColorWindow.Show(Color.FromArgb(200, 10, 10), new Rectangle(120, 120, 220, 120));
        new Win32WindowEffects().ExcludeFromCapture(new ClipBench.Core.Pasting.WindowHandle(window.Handle)).IsSuccess.ShouldBeTrue();
        await Task.Delay(300, Ct);

        var result = await new GdiScreenCapturer().CaptureAsync(new PixelRect(window.Bounds.X + 40, window.Bounds.Y + 30, 60, 40), Ct);

        using var image = result.Value;
        var centerRed = image.Pixels[((20 * 60) + 30) * 4 + 2];
        centerRed.ShouldNotBe((byte)200, "a window that asked to be left out of captures showed up in the pixels");
    }

    /// <summary>A plain colored top-most window that does not take the focus, on a thread of its own.</summary>
    private sealed class ColorWindow : IDisposable
    {
        private readonly Thread _thread;
        private readonly ManualResetEventSlim _shown = new();
        private Form? _form;

        private ColorWindow(Color color, Rectangle bounds)
        {
            Bounds = bounds;
            _thread = new Thread(() =>
            {
                _form = new NoFocusForm { BackColor = color, FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.Manual, Bounds = bounds, TopMost = true, ShowInTaskbar = false };
                _form.Shown += (_, _) => _shown.Set();
                Application.Run(_form);
            })
            { IsBackground = true, Name = "Color window" };
            _thread.SetApartmentState(ApartmentState.STA);
        }

        public Rectangle Bounds { get; }

        public nint Handle => (nint)_form!.Invoke(() => _form.Handle);

        public static ColorWindow Show(Color color, Rectangle bounds)
        {
            var window = new ColorWindow(color, bounds);
            window._thread.Start();
            window._shown.Wait(TimeSpan.FromSeconds(10)).ShouldBeTrue("the test window did not show");
            Thread.Sleep(400);   // the desktop compositor needs a moment to put the new window on screen
            return window;
        }

        public void Dispose()
        {
            _form?.Invoke(_form.Close);
            _thread.Join(TimeSpan.FromSeconds(5));
            _shown.Dispose();
        }

        private sealed class NoFocusForm : Form
        {
            protected override bool ShowWithoutActivation => true;
        }
    }
}
