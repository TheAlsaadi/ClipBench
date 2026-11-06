using System.Collections.Immutable;
using ClipBench.Core.Results;
using ClipBench.Core.Screen;

namespace ClipBench.Core.Tests.Screen;

public class AdaptiveRecognizerTests
{
    private const string NoLanguage = "Windows has no text-recognition language for your languages.";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // A Latin-script engine such as Tesseract: its reading is preferred for everything that is not Arabic or Hebrew.
    private sealed class LatinEngine(IReadOnlyList<RecognizedLine> lines, int preferredScale = 0) : ILatinWordRecognizer
    {
        public int PreferredScale => preferredScale;

        public ScreenImage? Saw { get; private set; }

        public Task<Result<IReadOnlyList<RecognizedLine>>> ReadAsync(ScreenImage image, CancellationToken cancellationToken)
        {
            Saw = image;
            return Task.FromResult(Result.Success(lines));
        }
    }

    private sealed class FakeEngine(Func<ScreenImage, Result<IReadOnlyList<RecognizedLine>>> answer) : IWordRecognizer
    {
        public FakeEngine(Func<ScreenImage, Result<string>> text)
            : this(image => From(text(image)))
        {
        }

        public List<(int Width, int Height)> Saw { get; } = [];

        public int Calls { get; private set; }

        public Task<Result<IReadOnlyList<RecognizedLine>>> ReadAsync(ScreenImage image, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            Saw.Add((image.Width, image.Height));
            return Task.FromResult(answer(image));
        }

        // Each line of the text becomes a line whose words sit side by side.
        private static Result<IReadOnlyList<RecognizedLine>> From(Result<string> text)
        {
            if (text.IsFailure)
            {
                return Result.Failure<IReadOnlyList<RecognizedLine>>(text.Error);
            }

            var lines = new List<RecognizedLine>();
            var row = 0;
            foreach (var line in text.Value.Split('\n'))
            {
                var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Select((word, index) => new RecognizedWord(word, index * 100, row * 20, 90, 18))
                    .ToList();
                lines.Add(new RecognizedLine(line.TrimEnd('\r'), words));
                row++;
            }

            return Result.Success<IReadOnlyList<RecognizedLine>>(lines);
        }
    }

    private static FakeEngine Says(string text) => new(_ => Result.Success(text));

    private static FakeEngine Fails(ErrorCode code, string message) => new(_ => Result.Failure<string>(new ResultError(code, message)));

    private static FakeEngine Reads(params RecognizedLine[] lines) => new(_ => Result.Success<IReadOnlyList<RecognizedLine>>(lines));

    private static ScreenImage Image(int width = 200, int height = 40) => new(width, height, new byte[width * height * 4]);

    private static AdaptiveRecognizer Create(params IWordRecognizer[] engines) => new(() => [.. engines], NoLanguage);

    // --- no language ---

    [Fact]
    public async Task With_no_engine_at_all_the_message_says_what_to_install()
    {
        using var image = Image();

        var result = await Create().RecognizeAsync(image, Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ErrorCode.OcrLanguageMissing);
        result.Error.Message.ShouldBe(NoLanguage);
    }

    [Fact]
    public async Task The_engines_are_asked_for_each_time_because_languages_can_be_added_while_the_app_runs()
    {
        var asked = 0;
        var engine = Says("text");
        var recognizer = new AdaptiveRecognizer(() => { asked++; return [engine]; }, NoLanguage);
        using var image = Image();

        await recognizer.RecognizeAsync(image, Ct);
        await recognizer.RecognizeAsync(image, Ct);

        asked.ShouldBe(2);
    }

    // --- enlarging ---

    [Fact]
    public async Task The_picture_gets_a_margin_and_is_enlarged_threefold_before_it_is_read()
    {
        var engine = Says("text");
        using var image = Image(300, 40);

        await Create(engine).RecognizeAsync(image, Ct);

        engine.Saw.ShouldBe([((300 + 16) * 3, (40 + 16) * 3)]);
    }

    [Fact]
    public async Task A_picture_too_large_to_enlarge_is_read_with_its_margin_only()
    {
        var engine = Says("text");
        using var image = Image(4000, 1000);

        await Create(engine).RecognizeAsync(image, Ct);

        engine.Saw.ShouldBe([(4016, 1016)]);
    }

    [Fact]
    public async Task A_picture_so_wide_that_a_margin_would_pass_the_limit_is_read_as_it_is()
    {
        var engine = Says("text");
        using var image = Image(ImageScaler.MaxSide - 1, 4);

        await Create(engine).RecognizeAsync(image, Ct);

        engine.Saw.ShouldBe([(ImageScaler.MaxSide - 1, 4)]);
    }

    [Fact]
    public async Task The_enlarged_copy_is_zeroed_when_the_reading_is_done_and_the_original_is_left_for_its_owner()
    {
        ScreenImage? seen = null;
        var engine = new FakeEngine(image =>
        {
            seen = image;
            return Result.Success("text");
        });
        using var image = Image(10, 10);

        await Create(engine).RecognizeAsync(image, Ct);

        seen.ShouldNotBeNull();
        seen.ShouldNotBeSameAs(image);
        seen.IsDisposed.ShouldBeTrue();
        image.IsDisposed.ShouldBeFalse();
    }

    [Fact]
    public async Task The_enlarged_copy_is_zeroed_when_an_engine_fails_or_the_reading_is_cancelled()
    {
        ScreenImage? seen = null;
        var failing = new FakeEngine(image =>
        {
            seen = image;
            return Result.Failure<string>(new ResultError(ErrorCode.OcrFailed, "x"));
        });
        using var image = Image(10, 10);
        await Create(failing).RecognizeAsync(image, Ct);
        seen!.IsDisposed.ShouldBeTrue();

        using var source = new CancellationTokenSource();
        var engine = new FakeEngine(scaled =>
        {
            seen = scaled;
            source.Cancel();
            return Result.Success("text");
        });
        var two = Create(engine, Says("more"));

        await Should.ThrowAsync<OperationCanceledException>(() => two.RecognizeAsync(image, source.Token));

        seen!.IsDisposed.ShouldBeTrue();
    }

    // --- several languages ---

    [Fact]
    public async Task Every_language_is_tried_and_the_reading_with_the_most_letters_wins()
    {
        var english = Says("2026");
        var arabic = Says("hello world 2026");
        using var image = Image();

        var result = await Create(english, arabic).RecognizeAsync(image, Ct);

        result.Value.ShouldBe("hello world 2026");
        english.Calls.ShouldBe(1);
        arabic.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task When_two_languages_read_the_same_number_of_letters_the_first_language_wins()
    {
        using var image = Image();

        var result = await Create(Says("Invoice"), Says("lnvoice")).RecognizeAsync(image, Ct);

        result.Value.ShouldBe("Invoice");
    }

    [Fact]
    public async Task One_language_that_fails_does_not_stop_another_that_reads()
    {
        using var image = Image();

        var result = await Create(Fails(ErrorCode.OcrFailed, "no"), Says("it works")).RecognizeAsync(image, Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe("it works");
    }

    [Fact]
    public async Task When_every_language_fails_the_first_failure_is_the_answer()
    {
        using var image = Image();

        var result = await Create(Fails(ErrorCode.OcrFailed, "first"), Fails(ErrorCode.OcrLanguageMissing, "second")).RecognizeAsync(image, Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Message.ShouldBe("first");
    }

    [Fact]
    public async Task When_nothing_is_read_in_any_language_the_answer_is_empty_text_not_a_failure()
    {
        using var image = Image();

        var result = await Create(Says(string.Empty), Says("   ")).RecognizeAsync(image, Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Trim().ShouldBeEmpty();
    }

    // --- choosing between readings ---

    [Fact]
    public void The_reading_with_the_most_letters_is_chosen_digits_and_marks_do_not_count()
    {
        RecognitionChoice.PickBest(["2026-10 ...", "ab", "abc"]).ShouldBe(2);
    }

    [Fact]
    public void Letters_of_any_script_count()
    {
        // Arabic letters (U+0641 U+0627 U+062A) against two Latin letters.
        RecognitionChoice.PickBest(["ab", string.Concat((char)0x641, (char)0x627, (char)0x62A)]).ShouldBe(1);
    }

    [Fact]
    public void A_tie_goes_to_the_earlier_reading_and_nothing_at_all_goes_to_the_first()
    {
        RecognitionChoice.PickBest(["abc", "xyz"]).ShouldBe(0);
        RecognitionChoice.PickBest(["", "  ", "42"]).ShouldBe(0);
        RecognitionChoice.PickBest([]).ShouldBe(-1);
    }

    [Fact]
    public void Missing_parts_are_a_bug()
    {
        Should.Throw<ArgumentNullException>(() => new AdaptiveRecognizer(null!, NoLanguage));
        Should.Throw<ArgumentException>(() => new AdaptiveRecognizer(() => ImmutableArray<IWordRecognizer>.Empty, " "));
        Should.Throw<ArgumentNullException>(() => RecognitionChoice.PickBest(null!));
    }

    [Fact]
    public async Task A_missing_picture_is_a_bug_and_a_cancelled_token_throws_before_anything_is_read()
    {
        var engine = Says("text");
        await Should.ThrowAsync<ArgumentNullException>(() => Create(engine).RecognizeAsync(null!, Ct));

        using var source = new CancellationTokenSource();
        await source.CancelAsync();
        using var image = Image();
        await Should.ThrowAsync<OperationCanceledException>(() => Create(engine).RecognizeAsync(image, source.Token));
        engine.Calls.ShouldBe(0);
    }

    // --- words from the engine that is good at them ---

    [Fact]
    public async Task An_arabic_name_in_an_english_window_comes_out_with_the_chevrons_of_the_english_engine_and_the_name_of_the_arabic_one()
    {
        var mohammed = string.Concat((char)0x645, (char)0x62D, (char)0x645, (char)0x62F);
        var alsaadi = string.Concat((char)0x627, (char)0x644, (char)0x635, (char)0x627, (char)0x639, (char)0x62F, (char)0x64A);
        RecognizedWord W(string text, double x, double width) => new(text, x, 5, width, 10);
        RecognizedLine Line(params RecognizedWord[] words) => new(string.Join(' ', words.Select(w => w.Text)), words);

        var english = Reads(Line(W(">", 59, 16), W("EIOJI", 134, 151), W(">", 446, 16), W("Minecraft-mod-porting", 518, 428), W(">", 1004, 16)));
        var arabic = Reads(Line(W("(", 59, 17), W(alsaadi, 134, 152), W(mohammed, 298, 92), W("(", 446, 17), W("Minecraft-mod-porting>", 518, 502)));
        using var image = Image(345, 28);

        var result = await Create(english, arabic).RecognizeAsync(image, Ct);

        result.Value.ShouldBe("> " + mohammed + " " + alsaadi + " > Minecraft-mod-porting >");
    }

    [Fact]
    public async Task A_latin_engine_is_preferred_over_an_engine_with_more_letters()
    {
        var windows = Says("hello world again");
        var latin = new LatinEngine([new RecognizedLine("x > 0", [new RecognizedWord("x", 0, 0, 10, 10), new RecognizedWord(">", 20, 0, 10, 10), new RecognizedWord("0", 40, 0, 10, 10)])]);
        using var image = Image();

        var result = await Create(windows, latin).RecognizeAsync(image, Ct);

        result.Value.ShouldBe("x > 0");
    }

    [Fact]
    public async Task Without_a_latin_engine_the_reading_with_the_most_letters_still_wins()
    {
        using var image = Image();

        var result = await Create(Says("12"), Says("hello")).RecognizeAsync(image, Ct);

        result.Value.ShouldBe("hello");
    }

    // --- a Latin engine that wants its own enlargement ---

    [Fact]
    public async Task A_latin_engine_that_prefers_twofold_gets_a_twofold_picture_while_the_others_get_threefold()
    {
        ScreenImage? shared = null;
        var windows = new FakeEngine(image =>
        {
            shared = image;
            return Result.Success("text");
        });
        var latin = new LatinEngine([], preferredScale: 2);
        using var image = Image(100, 20);

        await Create(windows, latin).RecognizeAsync(image, Ct);

        // 100 x 20 plus a margin of 8 on every side is 116 x 36, enlarged threefold for Windows and twofold for the Latin engine.
        shared.ShouldNotBeNull();
        (shared.Width, shared.Height).ShouldBe((348, 108));
        latin.Saw.ShouldNotBeNull();
        (latin.Saw.Width, latin.Saw.Height).ShouldBe((232, 72));
        latin.Saw.IsDisposed.ShouldBeTrue("the extra enlarged copy is zeroed like the first");
    }

    [Fact]
    public async Task A_latin_engine_with_the_same_enlargement_as_the_others_gets_the_same_picture()
    {
        ScreenImage? shared = null;
        var windows = new FakeEngine(image =>
        {
            shared = image;
            return Result.Success("text");
        });
        var latin = new LatinEngine([], preferredScale: 3);
        using var image = Image(100, 20);

        await Create(windows, latin).RecognizeAsync(image, Ct);

        latin.Saw.ShouldBeSameAs(shared);
    }

    [Fact]
    public async Task The_words_of_a_latin_engine_read_at_twofold_are_put_in_the_places_of_the_threefold_picture()
    {
        // The Arabic engine read a word at x 60 of the threefold picture; the Latin engine read the same place at x 40 of the twofold one.
        // Moved to the threefold picture they are the same place, so the Latin engine's sure word wins and the Arabic letters made up there do not.
        var arabic = string.Concat((char)0x0644, (char)0x0627);
        var windows = Reads(new RecognizedLine(arabic, [new RecognizedWord(arabic, 60, 15, 30, 12)]));
        var latin = new LatinEngine([new RecognizedLine("x", [new RecognizedWord("x", 40, 10, 20, 8, 90)])], preferredScale: 2);
        using var image = Image(100, 20);

        var result = await Create(windows, latin).RecognizeAsync(image, Ct);

        result.Value.ShouldBe("x");
    }

    [Fact]
    public async Task A_picture_too_large_to_enlarge_is_read_as_it_is_by_a_latin_engine_that_prefers_an_enlargement()
    {
        var latin = new LatinEngine([], preferredScale: 2);
        using var image = Image(9_995, 1);

        await Create(latin).RecognizeAsync(image, Ct);

        latin.Saw.ShouldBeSameAs(image);
    }
}
