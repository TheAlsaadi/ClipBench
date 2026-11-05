using System.Drawing;
using System.Drawing.Imaging;
using ClipBench.Core.Results;
using ClipBench.Core.Screen;
using ClipBench.Platform.Windows.Screen;
using Windows.Globalization;
using Windows.Media.Ocr;

namespace ClipBench.Platform.Windows.IntegrationTests.Screen;

public class WindowsOcrEngineTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Black text on white, drawn with GDI+ the way Windows draws interface text (ClearType), as a picture in memory.
    private static ScreenImage Render(string text, int pixelSize = 48, string family = "Arial", int width = 900, int height = 0)
    {
        height = height > 0 ? height : Math.Max(40, pixelSize * 3);
        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.White);
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            using var font = new Font(family, pixelSize, FontStyle.Regular, GraphicsUnit.Pixel);
            graphics.DrawString(text, font, Brushes.Black, new PointF(pixelSize / 2f, pixelSize));
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

    // The way a dragged rectangle is cut: 2 pixels around the text, in the colors given.
    private static ScreenImage RenderTight(string text, int pixelSize, string family, Color foreground, Color background)
    {
        using var font = new Font(family, pixelSize, FontStyle.Regular, GraphicsUnit.Pixel);
        using var probe = new Bitmap(10, 10);
        using var measuring = Graphics.FromImage(probe);
        var size = measuring.MeasureString(text, font, 4000, StringFormat.GenericTypographic);
        var width = (int)size.Width + 4;
        var height = (int)size.Height + 4;

        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(background);
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            using var brush = new SolidBrush(foreground);
            graphics.DrawString(text, font, brush, 2, 2, StringFormat.GenericTypographic);
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

    private static OcrEngine? Engine(string tag) => OcrEngine.TryCreateFromLanguage(new Language(tag));

    private static void RequireLanguage(string tag)
    {
        if (Engine(tag) is null)
        {
            Assert.Skip($"Windows has no text-recognition feature for {tag} on this machine.");
        }
    }

    private static string Letters(params int[] codePoints) => string.Concat(codePoints.Select(cp => (char)cp));

    // --- the factory: which languages are read ---

    [Fact]
    public async Task Without_any_recognition_language_the_message_says_what_to_install_and_where()
    {
        var recognizer = WindowsOcr.CreateRecognizer(() => ["zz-ZZ"]);
        using var image = new ScreenImage(10, 10, new byte[400]);

        var result = await recognizer.RecognizeAsync(image, Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ErrorCode.OcrLanguageMissing);
        result.Error.Message.ShouldContain("language");
        result.Error.Message.ShouldContain("Settings");
        result.Error.Message.ShouldContain("Time & language");
    }

    [Fact]
    public async Task A_cancelled_reading_throws_before_anything_is_read()
    {
        var recognizer = WindowsOcr.CreateRecognizer(() => []);
        using var image = new ScreenImage(10, 10, new byte[400]);
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => recognizer.RecognizeAsync(image, source.Token));
    }

    [Fact]
    public async Task A_picture_that_has_been_let_go_of_cannot_be_read()
    {
        var recognizer = WindowsOcr.CreateRecognizer(() => []);
        var image = new ScreenImage(10, 10, new byte[400]);
        image.Dispose();

        await Should.ThrowAsync<ObjectDisposedException>(() => recognizer.RecognizeAsync(image, Ct));
    }

    [Fact]
    public void Missing_parts_are_a_bug()
    {
        Should.Throw<ArgumentNullException>(() => WindowsOcr.CreateRecognizer(null!));
        Should.Throw<ArgumentNullException>(() => new WindowsOcrEngine(null!));
    }

    // --- one language ---

    [Fact]
    [Trait("Category", "Integration")]
    public async Task A_picture_with_known_text_is_read_back_by_the_real_recognizer()
    {
        RequireLanguage("en-US");
        using var image = Render("HELLO ClipBench 2026");

        var result = await new WindowsOcrEngine(Engine("en-US")!).RecognizeAsync(image, Ct);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : null);
        result.Value.ShouldContain("HELLO", Case.Insensitive);
        result.Value.ShouldContain("2026");
        result.Value.ShouldContain("ClipBench", Case.Insensitive);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task A_blank_picture_gives_no_text_and_no_error()
    {
        RequireLanguage("en-US");
        using var image = Render(string.Empty);

        var result = await new WindowsOcrEngine(Engine("en-US")!).RecognizeAsync(image, Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Trim().ShouldBeEmpty();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task A_picture_larger_than_the_recognizer_reads_is_refused_with_a_message()
    {
        RequireLanguage("en-US");
        var side = (int)OcrEngine.MaxImageDimension + 1;
        using var image = new ScreenImage(side, 1, new byte[side * 4]);

        var result = await new WindowsOcrEngine(Engine("en-US")!).RecognizeAsync(image, Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ErrorCode.OcrFailed);
    }

    // --- the recognizer the app uses: small text and more than one language ---

    [Theory]
    [Trait("Category", "Integration")]
    [InlineData(10, "Segoe UI")]
    [InlineData(11, "Segoe UI")]
    [InlineData(12, "Arial")]
    [InlineData(13, "Verdana")]
    [InlineData(13, "Consolas")]
    public async Task Small_interface_text_is_read_which_the_recognizer_alone_returns_nothing_for(int pixelSize, string family)
    {
        RequireLanguage("en-US");
        using var image = Render("Invoice 2026-10 for ACME Corp", pixelSize, family);

        var result = await WindowsOcr.CreateRecognizer(() => ["en-US"]).RecognizeAsync(image, Ct);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : null);
        result.Value.ShouldContain("Invoice", Case.Insensitive);
        result.Value.ShouldContain("ACME");
    }

    [Theory]
    [Trait("Category", "Integration")]
    [InlineData(11, "Consolas", false)]
    [InlineData(13, "Consolas", false)]
    [InlineData(13, "Consolas", true)]
    [InlineData(16, "Segoe UI", true)]
    public async Task Code_cut_tightly_around_the_text_is_read_which_the_recognizer_alone_returns_nothing_for(int pixelSize, string family, bool lightOnDark)
    {
        RequireLanguage("en-US");
        var foreground = lightOnDark ? Color.FromArgb(204, 204, 204) : Color.Black;
        var background = lightOnDark ? Color.FromArgb(12, 12, 12) : Color.White;
        using var image = RenderTight("if (count > max) { return items[i]; }", pixelSize, family, foreground, background);

        var result = await WindowsOcr.CreateRecognizer(() => ["en-US"]).RecognizeAsync(image, Ct);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : null);
        result.Value.ShouldContain("count");
        result.Value.ShouldContain("return");
        result.Value.ShouldContain("items");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Arabic_text_is_read_when_English_is_the_first_language()
    {
        RequireLanguage("en-US");
        RequireLanguage("ar-SA");
        var arabic = Letters(0x641, 0x627, 0x62A, 0x648, 0x631, 0x629) + " " + Letters(0x631, 0x642, 0x645) + " 2026 " + Letters(0x644, 0x644, 0x634, 0x631, 0x643, 0x629);
        using var image = Render(arabic, 28, "Segoe UI");

        var result = await WindowsOcr.CreateRecognizer(() => ["en-US", "ar-SA"]).RecognizeAsync(image, Ct);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : null);
        result.Value.Count(c => c is >= (char)0x600 and <= (char)0x6FF).ShouldBeGreaterThanOrEqualTo(10);
        result.Value.ShouldContain("2026");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task English_is_still_read_when_Arabic_is_the_first_language()
    {
        RequireLanguage("en-US");
        RequireLanguage("ar-SA");
        using var image = Render("Invoice 2026-10 for ACME Corp", 22, "Segoe UI");

        var result = await WindowsOcr.CreateRecognizer(() => ["ar-SA", "en-US"]).RecognizeAsync(image, Ct);

        // "Invoice" may come back as "lnvoice": a capital I and a lowercase l look the same in this font, to people and to the recognizer.
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : null);
        result.Value.ShouldContain("voice", Case.Insensitive);
        result.Value.ShouldContain("2026-10");
        result.Value.ShouldContain("ACME");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void Varieties_of_one_language_are_read_once_so_a_second_script_is_not_crowded_out()
    {
        RequireLanguage("en-US");
        RequireLanguage("ar-SA");

        // en-AU has no engine of its own and falls back to en-GB; the four engines allowed must not all go to English.
        var tags = WindowsOcr.EngineTags(() => ["en-US", "en-AU", "en-GB", "en-CA", "ar-SA"]);

        tags.Count(tag => tag.StartsWith("en", StringComparison.OrdinalIgnoreCase)).ShouldBe(1);
        tags.ShouldContain("ar-SA");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void The_users_order_is_kept_and_a_tag_that_is_not_a_language_is_skipped()
    {
        RequireLanguage("en-US");
        RequireLanguage("ar-SA");

        WindowsOcr.EngineTags(() => ["ar-SA", "not a tag!", "en-US"]).ShouldBe(["ar-SA", "en-US"]);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task A_language_listed_twice_or_in_another_case_is_read_once()
    {
        RequireLanguage("en-US");
        using var image = Render("HELLO ClipBench 2026", 32);

        var result = await WindowsOcr.CreateRecognizer(() => ["en-US", "EN-us", "en-US"]).RecognizeAsync(image, Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldContain("HELLO", Case.Insensitive);
    }
}
