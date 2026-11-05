using System.Diagnostics;
using System.Drawing;
using System.Security.Cryptography;
using ClipBench.Core.Results;
using ClipBench.Core.Screen;
using ClipBench.Platform.Windows.Screen;

namespace ClipBench.Platform.Windows.IntegrationTests.Screen;

/// <summary>The Tesseract engine (ADR 0019) reading real pictures, drawn in memory the way Windows draws interface text.</summary>
public class TesseractEngineTests
{
    // The English model that ships with the app, from tessdata_fast (ADR 0019). A different file is a different engine.
    private const string ModelSha256 = "7d4322bd2a7749724879683fc3912cb542f19906c83bcc1a52132556427170b2";

    private static readonly Lazy<TesseractEngine?> _sharedEngine = new(() => TesseractEngine.TryCreate(ModelFolder));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string ModelFolder => Path.Combine(AppContext.BaseDirectory, "tessdata");

    private static TesseractEngine Engine() => _sharedEngine.Value ?? throw new InvalidOperationException("The Tesseract engine did not start from " + ModelFolder);

    // What the app does: a margin and an enlargement first (ImageScaler), then every engine reads the same enlarged picture.
    private static async Task<string> TextOf(ScreenImage picture)
    {
        using (picture)
        {
            var prepared = ImageScaler.Prepare(picture);
            try
            {
                var result = await Engine().ReadAsync(prepared ?? picture, Ct);
                result.IsSuccess.ShouldBeTrue();
                return ReadingMerge.ToText(result.Value);
            }
            finally
            {
                prepared?.Dispose();
            }
        }
    }

    private static ScreenImage Code(string text, int size = 14) => WindowsOcrEngineTests.RenderTight(text, size, "Consolas", Color.Black, Color.White);

    // --- starting ---

    [Fact]
    public void The_model_in_the_output_folder_is_the_pinned_one()
    {
        var bytes = File.ReadAllBytes(Path.Combine(ModelFolder, "eng.traineddata"));

        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant().ShouldBe(ModelSha256);
    }

    [Fact]
    public void Without_the_model_there_is_no_engine_and_no_exception()
    {
        TesseractEngine.TryCreate(Path.Combine(AppContext.BaseDirectory, "no-such-folder")).ShouldBeNull();
    }

    [Fact]
    public void A_model_that_is_not_a_model_gives_no_engine_and_no_exception()
    {
        var folder = Path.Combine(Path.GetTempPath(), "clipbench-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllBytes(Path.Combine(folder, "eng.traineddata"), [1, 2, 3, 4, 5, 6, 7, 8]);

            TesseractEngine.TryCreate(folder).ShouldBeNull();
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void The_engine_is_a_latin_engine()
    {
        Engine().ShouldBeAssignableTo<ILatinWordRecognizer>();
    }

    [Fact]
    public void The_build_output_has_the_x64_files_and_none_of_the_x86_files_or_the_program()
    {
        File.Exists(Path.Combine(AppContext.BaseDirectory, "x64", "tesseract55.dll")).ShouldBeTrue();
        File.Exists(Path.Combine(AppContext.BaseDirectory, "x64", "leptonica-1.85.0.dll")).ShouldBeTrue();
        Directory.Exists(Path.Combine(AppContext.BaseDirectory, "x86")).ShouldBeFalse();
        Directory.GetFiles(AppContext.BaseDirectory, "tesseract.exe", SearchOption.AllDirectories).ShouldBeEmpty();
    }

    // --- reading ---

    [Fact]
    public async Task A_line_of_code_is_read_with_its_symbols()
    {
        var text = await TextOf(Code("if (count > max) { return items[i]; }"));

        text.ShouldContain(">");
        text.ShouldContain("return");
        text.ShouldContain("items[i]");
    }

    [Theory]
    [InlineData("x > 0")]
    [InlineData("=> value")]
    [InlineData("a >= b && c != d")]
    public async Task Short_symbol_heavy_text_that_windows_drops_is_kept(string line)
    {
        var text = await TextOf(Code(line));

        text.ShouldContain(">");
    }

    [Fact]
    public async Task Light_text_on_a_dark_background_is_read()
    {
        using var picture = WindowsOcrEngineTests.RenderTight("var list = new List<int>();", 14, "Consolas", Color.FromArgb(235, 235, 235), Color.FromArgb(30, 30, 30));

        var prepared = ImageScaler.Prepare(picture);
        try
        {
            var result = await Engine().ReadAsync(prepared!, Ct);

            ReadingMerge.ToText(result.Value).ShouldContain("List<int>");
        }
        finally
        {
            prepared?.Dispose();
        }
    }

    [Fact]
    public async Task A_word_comes_back_with_a_place_inside_the_picture()
    {
        using var picture = Code("alpha beta");
        using var prepared = ImageScaler.Prepare(picture)!;

        var result = await Engine().ReadAsync(prepared, Ct);

        var words = result.Value.SelectMany(line => line.Words).ToList();
        words.Select(word => word.Text).ShouldBe(["alpha", "beta"]);
        words.ShouldAllBe(word => word.X >= 0 && word.Y >= 0 && word.Width > 0 && word.Height > 0
            && word.X + word.Width <= prepared.Width && word.Y + word.Height <= prepared.Height);
        words[1].X.ShouldBeGreaterThan(words[0].X);
    }

    [Fact]
    public async Task Each_word_carries_the_confidence_of_the_engine()
    {
        using var picture = Code("alpha beta");
        using var prepared = ImageScaler.Prepare(picture)!;

        var result = await Engine().ReadAsync(prepared, Ct);

        var words = result.Value.SelectMany(line => line.Words).ToList();
        words.ShouldNotBeEmpty();
        words.ShouldAllBe(word => word.Confidence != null && word.Confidence >= 0 && word.Confidence <= 100);
        words.ShouldAllBe(word => word.Confidence >= 50, "clean text at a normal size is read with confidence");
    }

    [Fact]
    public async Task A_blank_picture_gives_no_text()
    {
        var pixels = new byte[200 * 40 * 4];
        Array.Fill(pixels, (byte)255);
        using var picture = new ScreenImage(200, 40, pixels);

        var result = await Engine().ReadAsync(picture, Ct);

        result.IsSuccess.ShouldBeTrue();
        ReadingMerge.ToText(result.Value).Trim().ShouldBeEmpty();
    }

    [Fact]
    public async Task The_picture_that_was_given_is_not_changed()
    {
        using var picture = WindowsOcrEngineTests.RenderTight("x > 0", 14, "Consolas", Color.White, Color.Black);
        var before = picture.Pixels.ToArray();

        await Engine().ReadAsync(picture, Ct);

        picture.Pixels.ToArray().ShouldBe(before);
    }

    [Fact]
    public async Task Several_readings_at_once_all_succeed()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => TextOf(Code("x > 0"))));

        results.ShouldAllBe(text => text.Contains('>'));
    }

    // --- limits and cancellation ---

    [Fact]
    public async Task A_picture_with_a_side_over_the_limit_is_refused_without_reading_it()
    {
        using var picture = new ScreenImage(10_001, 2, new byte[10_001 * 2 * 4]);

        var result = await Engine().ReadAsync(picture, Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ErrorCode.OcrFailed);
    }

    [Fact]
    public async Task A_picture_with_too_many_pixels_is_refused_without_reading_it()
    {
        using var picture = new ScreenImage(4_000, 3_001, new byte[4_000 * 3_001 * 4]);

        var result = await Engine().ReadAsync(picture, Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ErrorCode.OcrFailed);
    }

    [Fact]
    public async Task A_cancelled_reading_throws_and_a_missing_picture_is_a_bug()
    {
        using var picture = Code("x > 0");
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => Engine().ReadAsync(picture, source.Token));
        await Should.ThrowAsync<ArgumentNullException>(() => Engine().ReadAsync(null!, Ct));
    }

    // --- no network ---

    // The TCP connections and the UDP endpoints that belong to this process, one string each. Windows PowerShell asks Windows for them;
    // netstat and the .NET network information classes did not list every socket in the test run's sandbox.
    private static HashSet<string> Endpoints()
    {
        var script = "$p = " + Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture) + "; "
            + "Get-NetTCPConnection -OwningProcess $p -ErrorAction SilentlyContinue | ForEach-Object { 'tcp ' + $_.LocalAddress + ':' + $_.LocalPort + ' to ' + $_.RemoteAddress + ':' + $_.RemotePort }; "
            + "Get-NetUDPEndpoint -OwningProcess $p -ErrorAction SilentlyContinue | ForEach-Object { 'udp ' + $_.LocalAddress + ':' + $_.LocalPort }; "
            + "'listed'";
        var start = new ProcessStartInfo("powershell.exe") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add(script);
        using var powershell = Process.Start(start)!;
        var output = powershell.StandardOutput.ReadToEnd();
        powershell.WaitForExit();

        // The last line proves the listing ran to the end; without it a failure to list would look like "no connections".
        var lines = output.Split((char)10, StringSplitOptions.RemoveEmptyEntries).Select(line => line.Trim()).ToList();
        lines.ShouldContain("listed", $"the connection listing did not run (exit code {powershell.ExitCode})");
        return [.. lines.Where(line => line != "listed")];
    }

    [Fact]
    public void The_watch_does_see_a_listener_that_this_process_opens()
    {
        // Proves the check below can fail: it must notice a socket opened by this very process.
        var before = Endpoints();
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            Endpoints().Except(before).ShouldNotBeEmpty();
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task Starting_and_reading_open_no_network_connection_or_listener()
    {
        // The library carries a network client that fetches pictures from a web address. ClipBench never gives it one (ADR 0019);
        // this watches the process while an engine starts and reads, as a check that nothing else makes it connect.
        var before = Endpoints();
        var seen = new HashSet<string>();
        using var stop = new CancellationTokenSource();
        var sampler = Task.Run(
            () =>
            {
                while (!stop.IsCancellationRequested)
                {
                    seen.UnionWith(Endpoints());
                }
            },
            Ct);

        using (var engine = TesseractEngine.TryCreate(ModelFolder))
        {
            engine.ShouldNotBeNull();
            for (var i = 0; i < 20; i++)
            {
                using var picture = Code("if (count > max) { return items[i]; }");
                using var prepared = ImageScaler.Prepare(picture)!;
                (await engine.ReadAsync(prepared, Ct)).IsSuccess.ShouldBeTrue();
            }
        }

        await stop.CancelAsync();
        await sampler;
        seen.UnionWith(Endpoints());

        seen.Except(before).ShouldBeEmpty();
    }

    // --- in the app's recognizer ---

    [Fact]
    public async Task The_app_recognizer_keeps_the_chevron_that_the_windows_engines_dropped()
    {
        var recognizer = WindowsOcr.CreateRecognizer(() => ["en-US"], () => Engine());
        using var picture = WindowsOcrEngineTests.RenderTight("C:/Users/me> dir", 14, "Consolas", Color.White, Color.Black);

        var result = await recognizer.RecognizeAsync(picture, Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldContain(">");
        result.Value.ShouldContain("dir");
    }

    [Fact]
    public async Task The_app_recognizer_works_when_windows_has_no_language_at_all()
    {
        var recognizer = WindowsOcr.CreateRecognizer(() => ["zz-ZZ"], () => Engine());
        using var picture = Code("x > 0");

        var result = await recognizer.RecognizeAsync(picture, Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldContain(">");
    }

    [Fact]
    public async Task Without_the_engine_the_windows_engines_read_as_before()
    {
        WindowsOcrEngineTests.RequireLanguage("en-US");
        var recognizer = WindowsOcr.CreateRecognizer(() => ["en-US"], () => null);
        using var picture = WindowsOcrEngineTests.Render("Hello world", 48);

        var result = await recognizer.RecognizeAsync(picture, Ct);

        result.Value.ShouldContain("Hello");
    }

    [Fact]
    public async Task Without_the_engine_and_without_a_language_the_message_says_what_to_install()
    {
        var recognizer = WindowsOcr.CreateRecognizer(() => ["zz-ZZ"], () => null);
        using var picture = Code("x > 0");

        var result = await recognizer.RecognizeAsync(picture, Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ErrorCode.OcrLanguageMissing);
    }

    [Fact]
    public void Missing_parts_are_a_bug()
    {
        Should.Throw<ArgumentNullException>(() => WindowsOcr.CreateRecognizer(() => [], null!));
        Should.Throw<ArgumentNullException>(() => WindowsOcr.CreateRecognizer(null!, () => null));
    }
}
