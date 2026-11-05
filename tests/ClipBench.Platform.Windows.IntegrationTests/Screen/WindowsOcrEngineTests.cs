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
    private static ScreenImage Render(string text, int pixelSize = 48, string family = "Arial", int width = 900, int height = 0, bool rightToLeft = false)
    {
        height = height > 0 ? height : Math.Max(40, pixelSize * 3);
        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.White);
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            using var font = new Font(family, pixelSize, FontStyle.Regular, GraphicsUnit.Pixel);
            if (rightToLeft)
            {
                // A right-to-left paragraph: the first word of the sentence is at the right edge.
                using var format = new StringFormat(StringFormatFlags.DirectionRightToLeft);
                graphics.DrawString(text, font, Brushes.Black, new RectangleF(pixelSize / 2f, pixelSize, width - pixelSize, height - pixelSize), format);
            }
            else
            {
                graphics.DrawString(text, font, Brushes.Black, new PointF(pixelSize / 2f, pixelSize));
            }
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

    // A File Explorer breadcrumb in an English window: chevron, Arabic name, chevron, folder, chevron, light text on a dark bar.
    private static ScreenImage RenderBreadcrumb(string arabicName, string folder)
    {
        const int pixelSize = 14;
        var background = Color.FromArgb(56, 56, 56);
        var foreground = Color.FromArgb(240, 240, 240);
        var width = 520;
        var height = 28;
        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(background);
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            using var text = new Font("Segoe UI", pixelSize, FontStyle.Regular, GraphicsUnit.Pixel);
            using var icons = new Font("Segoe MDL2 Assets", 10, FontStyle.Regular, GraphicsUnit.Pixel);
            using var brush = new SolidBrush(foreground);
            using var rightToLeft = new StringFormat(StringFormatFlags.DirectionRightToLeft);
            var chevron = ((char)0xE76C).ToString();

            graphics.DrawString(chevron, icons, brush, 10, 9);
            graphics.DrawString(arabicName, text, brush, new RectangleF(40, 5, 120, 20), rightToLeft);
            graphics.DrawString(chevron, icons, brush, 170, 9);
            graphics.DrawString(folder, text, brush, 195, 5);
            graphics.DrawString(chevron, icons, brush, 195 + 165, 9);
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

    [Theory]
    [Trait("Category", "Integration")]
    [InlineData(16)]
    [InlineData(20)]
    [InlineData(32)]
    public async Task Arabic_words_come_back_in_reading_order_not_in_the_order_they_are_drawn_left_to_right(int pixelSize)
    {
        RequireLanguage("ar-SA");
        var mohammed = Letters(0x645, 0x62D, 0x645, 0x62F);
        var alsaadi = Letters(0x627, 0x644, 0x635, 0x627, 0x639, 0x62F, 0x64A);
        using var image = Render(mohammed + " " + alsaadi, pixelSize, "Segoe UI", 400);

        var result = await WindowsOcr.CreateRecognizer(() => ["ar-SA"]).RecognizeAsync(image, Ct);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : null);
        result.Value.Trim().ShouldBe(mohammed + " " + alsaadi);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task English_inside_an_Arabic_sentence_stays_where_it_was_written()
    {
        RequireLanguage("en-US");
        RequireLanguage("ar-SA");
        var sentence = Letters(0x645, 0x631, 0x62D, 0x628, 0x627) + " " + Letters(0x628, 0x643, 0x645) + " " + Letters(0x641, 0x64A) + " " + Letters(0x631, 0x642, 0x645) + " " + Letters(0x644, 0x644, 0x634, 0x631, 0x643, 0x629) + " ClipBench 2026";
        using var image = Render(sentence, 28, "Segoe UI", 600, rightToLeft: true);

        var result = await WindowsOcr.CreateRecognizer(() => ["ar-SA", "en-US"]).RecognizeAsync(image, Ct);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : null);
        result.Value.Trim().ShouldBe(sentence);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task An_Arabic_name_inside_an_English_sentence_is_read_in_order_and_the_English_stays_put()
    {
        RequireLanguage("en-US");
        RequireLanguage("ar-SA");
        var sentence = "Hello there " + Letters(0x645, 0x62D, 0x645, 0x62F) + " " + Letters(0x627, 0x644, 0x635, 0x627, 0x639, 0x62F, 0x64A) + " my friend";
        using var image = Render(sentence, 28, "Segoe UI", 700);

        var result = await WindowsOcr.CreateRecognizer(() => ["en-US", "ar-SA"]).RecognizeAsync(image, Ct);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : null);
        result.Value.Trim().ShouldBe(sentence);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task A_breadcrumb_with_an_Arabic_name_keeps_its_order_and_its_chevron_as_a_greater_than_sign()
    {
        RequireLanguage("en-US");
        RequireLanguage("ar-SA");
        var mohammed = Letters(0x645, 0x62D, 0x645, 0x62F);
        var alsaadi = Letters(0x627, 0x644, 0x635, 0x627, 0x639, 0x62F, 0x64A);
        using var image = RenderBreadcrumb(mohammed + " " + alsaadi, "Minecraft-mod-porting");

        var result = await WindowsOcr.CreateRecognizer(() => ["en-US", "ar-SA"]).RecognizeAsync(image, Ct);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : null);
        result.Value.ShouldNotContain("(");
        result.Value.ShouldNotContain(")");
        // The chevrons at the two ends of the drawn bar are not found by either engine in this picture; the one in the middle is.
        result.Value.ShouldContain(mohammed + " " + alsaadi + " > Minecraft-mod-porting");
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
