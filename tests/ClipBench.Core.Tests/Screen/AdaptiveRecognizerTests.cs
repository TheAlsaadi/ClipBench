using System.Collections.Immutable;
using ClipBench.Core.Results;
using ClipBench.Core.Screen;

namespace ClipBench.Core.Tests.Screen;

public class AdaptiveRecognizerTests
{
    private const string NoLanguage = "Windows has no text-recognition language for your languages.";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class FakeEngine(Func<ScreenImage, Result<string>> answer) : ITextRecognizer
    {
        public List<(int Width, int Height)> Saw { get; } = [];

        public int Calls { get; private set; }

        public Task<Result<string>> RecognizeAsync(ScreenImage image, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            Saw.Add((image.Width, image.Height));
            return Task.FromResult(answer(image));
        }
    }

    private static FakeEngine Says(string text) => new(_ => Result.Success(text));

    private static FakeEngine Fails(ErrorCode code, string message) => new(_ => Result.Failure<string>(new ResultError(code, message)));

    private static ScreenImage Image(int width = 200, int height = 40) => new(width, height, new byte[width * height * 4]);

    private static AdaptiveRecognizer Create(params ITextRecognizer[] engines) => new(() => [.. engines], NoLanguage);

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
    public async Task The_picture_is_enlarged_twofold_before_it_is_read()
    {
        var engine = Says("text");
        using var image = Image(300, 40);

        await Create(engine).RecognizeAsync(image, Ct);

        engine.Saw.ShouldBe([(600, 80)]);
    }

    [Fact]
    public async Task A_picture_too_large_to_enlarge_is_read_as_it_is()
    {
        var engine = Says("text");
        using var image = Image(4000, 1000);

        await Create(engine).RecognizeAsync(image, Ct);

        engine.Saw.ShouldBe([(4000, 1000)]);
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
        Should.Throw<ArgumentException>(() => new AdaptiveRecognizer(() => ImmutableArray<ITextRecognizer>.Empty, " "));
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
}
