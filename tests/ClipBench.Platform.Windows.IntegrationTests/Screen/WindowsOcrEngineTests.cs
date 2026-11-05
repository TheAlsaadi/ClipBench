using System.Drawing;
using System.Drawing.Imaging;
using ClipBench.Core.Results;
using ClipBench.Core.Screen;
using ClipBench.Platform.Windows.Screen;
using Windows.Media.Ocr;

namespace ClipBench.Platform.Windows.IntegrationTests.Screen;

public class WindowsOcrEngineTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Black text on white, drawn with GDI+ at a size the recognizer reads well, as a picture in memory.
    private static ScreenImage Render(string text, int width = 900, int height = 160)
    {
        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.White);
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            using var font = new Font("Arial", 48, FontStyle.Regular, GraphicsUnit.Pixel);
            graphics.DrawString(text, font, Brushes.Black, new PointF(20, 40));
        }

        var data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var pixels = new byte[width * height * 4];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
            return new ScreenImage(width, height, pixels);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    [Fact]
    public async Task Without_a_recognition_language_the_message_says_what_to_install_and_where()
    {
        var engine = new WindowsOcrEngine(() => null);
        using var image = new ScreenImage(10, 10, new byte[400]);

        var result = await engine.RecognizeAsync(image, Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ErrorCode.OcrLanguageMissing);
        result.Error.Message.ShouldContain("language");
        result.Error.Message.ShouldContain("Settings");
        result.Error.Message.ShouldContain("Time & language");
    }

    [Fact]
    public async Task A_cancelled_reading_throws_before_anything_is_read()
    {
        var engine = new WindowsOcrEngine(() => null);
        using var image = new ScreenImage(10, 10, new byte[400]);
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => engine.RecognizeAsync(image, source.Token));
    }

    [Fact]
    public async Task A_picture_that_has_been_let_go_of_cannot_be_read()
    {
        var engine = new WindowsOcrEngine(() => null);
        var image = new ScreenImage(10, 10, new byte[400]);
        image.Dispose();

        await Should.ThrowAsync<ObjectDisposedException>(() => engine.RecognizeAsync(image, Ct));
    }

    [Fact]
    public async Task Missing_parts_are_a_bug()
    {
        Should.Throw<ArgumentNullException>(() => new WindowsOcrEngine(null!));
        await Should.ThrowAsync<ArgumentNullException>(() => new WindowsOcrEngine(() => null).RecognizeAsync(null!, Ct));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task A_picture_with_known_text_is_read_back_by_the_real_recognizer()
    {
        if (OcrEngine.TryCreateFromUserProfileLanguages() is null)
        {
            Assert.Skip("Windows has no text-recognition language installed for this user, so the real recognizer cannot run.");
        }

        using var image = Render("HELLO ClipBench 2026");
        var engine = new WindowsOcrEngine();

        var result = await engine.RecognizeAsync(image, Ct);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : null);
        result.Value.ShouldContain("HELLO", Case.Insensitive);
        result.Value.ShouldContain("2026");
        result.Value.ShouldContain("ClipBench", Case.Insensitive);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task A_blank_picture_gives_no_text_and_no_error()
    {
        if (OcrEngine.TryCreateFromUserProfileLanguages() is null)
        {
            Assert.Skip("Windows has no text-recognition language installed for this user.");
        }

        using var image = Render(string.Empty);

        var result = await new WindowsOcrEngine().RecognizeAsync(image, Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Trim().ShouldBeEmpty();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task A_picture_larger_than_the_recognizer_reads_is_refused_with_a_message()
    {
        if (OcrEngine.TryCreateFromUserProfileLanguages() is null)
        {
            Assert.Skip("Windows has no text-recognition language installed for this user.");
        }

        var side = (int)OcrEngine.MaxImageDimension + 1;
        using var image = new ScreenImage(side, 1, new byte[side * 4]);

        var result = await new WindowsOcrEngine().RecognizeAsync(image, Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ErrorCode.OcrFailed);
    }
}
