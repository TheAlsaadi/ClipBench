using ClipBench.Core.Diagnostics;
using ClipBench.Core.Results;
using ClipBench.Core.Transforms;
using ClipBench.Presentation.Rows;
using ClipBench.Presentation.Tests.Support;
using ClipBench.Presentation.Transforms;

namespace ClipBench.Presentation.Tests.Transforms;

public class TransformPickerTests
{
    private readonly FakeActivityLog _log = new();
    private readonly TransformPickerViewModel _picker;

    public TransformPickerTests()
    {
        _picker = new TransformPickerViewModel(TransformRegistry.Default, new TransformPipeline(), _log);
    }

    private void Highlight(string name)
    {
        var index = _picker.Items.ToList().FindIndex(item => item.Name == name);
        index.ShouldBeGreaterThanOrEqualTo(0, $"no transform named {name}");
        _picker.SelectedIndex = index;
    }

    private string Confirm()
    {
        _picker.TryConfirm(out var text).ShouldBeTrue(_picker.ErrorMessage);
        return text;
    }

    // --- opening ---

    [Fact]
    public void Opening_lists_every_transform_and_find_and_replace_last_with_the_first_highlighted()
    {
        _picker.Open("Hello", isSensitive: false);

        _picker.IsOpen.ShouldBeTrue();
        _picker.Items.Select(item => item.Name).ShouldBe(
        [
            "Plain text", "UPPER CASE", "lower case", "Title Case", "Sentence case", "Trim", "Remove line breaks",
            "JSON format", "JSON minify", "URL encode", "URL decode", "Base64 encode", "Base64 decode", "Find and replace\u2026",
        ]);
        _picker.Items[^1].NeedsInput.ShouldBeTrue();
        _picker.Items.Take(..^1).ShouldAllBe(item => !item.NeedsInput);
        _picker.SelectedIndex.ShouldBe(0);
        _picker.ChainText.ShouldBeEmpty();
        _picker.ErrorMessage.ShouldBeNull();
    }

    [Fact]
    public void Opening_again_starts_from_scratch()
    {
        _picker.Open("one", isSensitive: false);
        _picker.FilterText = "json";
        Highlight("JSON minify");
        _picker.AddToChainCommand.Execute(null);

        _picker.Open("two", isSensitive: false);

        _picker.FilterText.ShouldBeEmpty();
        _picker.ChainText.ShouldBeEmpty();
        _picker.Items.Count.ShouldBe(14);
        _picker.SelectedIndex.ShouldBe(0);
    }

    // --- the list ---

    [Fact]
    public void The_filter_narrows_the_list_by_name_or_description_ignoring_case()
    {
        _picker.Open("x", isSensitive: false);

        _picker.FilterText = "JSON";
        _picker.Items.Select(item => item.Name).ShouldBe(["JSON format", "JSON minify"]);

        _picker.FilterText = "base64";
        _picker.Items.Select(item => item.Name).ShouldBe(["Base64 encode", "Base64 decode"]);

        _picker.FilterText = "web address";
        _picker.Items.Select(item => item.Name).ShouldBe(["URL encode"]);

        _picker.FilterText = "nothing matches this";
        _picker.Items.ShouldBeEmpty();
        _picker.SelectedIndex.ShouldBe(-1);
    }

    [Fact]
    public void Changing_the_filter_highlights_the_first_match()
    {
        _picker.Open("x", isSensitive: false);
        _picker.SelectedIndex = 5;

        _picker.FilterText = "case";

        _picker.SelectedIndex.ShouldBe(0);
        _picker.Current!.Name.ShouldBe("UPPER CASE");
    }

    [Fact]
    public void Arrow_keys_move_the_highlight_and_wrap_around()
    {
        _picker.Open("x", isSensitive: false);

        _picker.MoveUpCommand.Execute(null);
        _picker.SelectedIndex.ShouldBe(13);

        _picker.MoveDownCommand.Execute(null);
        _picker.SelectedIndex.ShouldBe(0);

        _picker.MoveDownCommand.Execute(null);
        _picker.SelectedIndex.ShouldBe(1);
    }

    // --- preview ---

    [Fact]
    public void The_preview_shows_what_the_highlighted_transform_would_paste()
    {
        _picker.Open("hello world", isSensitive: false);
        _picker.Preview.ShouldBe("hello world");

        Highlight("UPPER CASE");
        _picker.Preview.ShouldBe("HELLO WORLD");
        _picker.PreviewIsError.ShouldBeFalse();
    }

    [Fact]
    public void The_preview_shows_the_chain_when_there_is_one()
    {
        _picker.Open("  hello  ", isSensitive: false);
        Highlight("Trim");
        _picker.AddToChainCommand.Execute(null);

        Highlight("UPPER CASE");
        _picker.Preview.ShouldBe("  hello  ".Trim(), "the highlighted transform is not part of the chain until it is added");

        _picker.AddToChainCommand.Execute(null);
        _picker.Preview.ShouldBe("HELLO");
    }

    [Fact]
    public void The_preview_of_a_transform_that_cannot_work_is_its_reason()
    {
        _picker.Open("this is not json", isSensitive: false);

        Highlight("JSON format");

        _picker.PreviewIsError.ShouldBeTrue();
        _picker.Preview.ShouldStartWith("JSON format: ");
    }

    [Fact]
    public void The_preview_is_cut_to_a_couple_of_lines()
    {
        _picker.Open(string.Join("\n", Enumerable.Repeat(new string('a', 300), 10)), isSensitive: false);

        // The lines shown, then a line that says there is more.
        _picker.Preview.Split('\n').Length.ShouldBe(PreviewText.DefaultMaxLines + 1);
        _picker.Preview.ShouldEndWith("\u2026");
        _picker.Preview.Length.ShouldBeLessThan(500);
    }

    [Fact]
    public void A_secret_is_never_previewed_but_is_still_transformed()
    {
        _picker.Open("hunter2-secret", isSensitive: true);
        Highlight("UPPER CASE");

        _picker.Preview.ShouldBe(ClipRowViewModel.Mask);
        Confirm().ShouldBe("HUNTER2-SECRET");
    }

    [Fact]
    public void A_large_text_has_no_preview_but_is_still_transformed_on_enter()
    {
        var large = new string('a', TransformPickerViewModel.LivePreviewLimit + 1);
        _picker.Open(large, isSensitive: false);

        Highlight("UPPER CASE");

        _picker.Preview.ShouldBe(TransformPickerViewModel.PreviewSkipped);
        _picker.PreviewIsError.ShouldBeFalse();
        Confirm().ShouldBe(large.ToUpperInvariant());
    }

    // --- applying ---

    [Fact]
    public void Enter_with_an_empty_chain_applies_the_highlighted_transform_and_closes()
    {
        _picker.Open("hello", isSensitive: false);
        Highlight("UPPER CASE");

        Confirm().ShouldBe("HELLO");

        _picker.IsOpen.ShouldBeFalse();
        _log.Entries.ShouldContain(entry => entry.Activity == ActivityEvent.TransformApplied && entry.Details.Count == 1);
    }

    [Fact]
    public void Enter_with_a_chain_applies_the_chain_in_order_and_not_the_highlighted_transform()
    {
        _picker.Open("  hello world  ", isSensitive: false);
        Highlight("Trim");
        _picker.AddToChainCommand.Execute(null);
        Highlight("Title Case");
        _picker.AddToChainCommand.Execute(null);
        _picker.ChainText.ShouldBe("Trim \u2192 Title Case");
        Highlight("Base64 encode");

        Confirm().ShouldBe("Hello World");

        _log.Entries.ShouldContain(entry => entry.Activity == ActivityEvent.TransformApplied && entry.Details.Count == 2);
    }

    [Fact]
    public void The_last_transform_can_be_taken_off_the_chain()
    {
        _picker.Open("x", isSensitive: false);
        Highlight("Trim");
        _picker.AddToChainCommand.Execute(null);
        Highlight("UPPER CASE");
        _picker.AddToChainCommand.Execute(null);

        _picker.RemoveLastFromChainCommand.Execute(null);
        _picker.ChainText.ShouldBe("Trim");

        _picker.RemoveLastFromChainCommand.Execute(null);
        _picker.ChainText.ShouldBeEmpty();

        _picker.RemoveLastFromChainCommand.Execute(null);
        _picker.ChainText.ShouldBeEmpty();
    }

    [Fact]
    public void A_chain_has_a_length_limit()
    {
        _picker.Open("x", isSensitive: false);
        Highlight("Trim");

        for (var i = 0; i < TransformPickerViewModel.MaxChainLength; i++)
        {
            _picker.AddToChainCommand.Execute(null);
        }

        _picker.ErrorMessage.ShouldBeNull();
        _picker.AddToChainCommand.Execute(null);

        _picker.ErrorMessage.ShouldNotBeNull();
        _picker.ChainText.Split('\u2192').Length.ShouldBe(TransformPickerViewModel.MaxChainLength);
    }

    [Fact]
    public void A_transform_that_fails_keeps_the_picker_open_and_says_why()
    {
        _picker.Open("this is not json", isSensitive: false);
        Highlight("JSON format");

        _picker.TryConfirm(out var text).ShouldBeFalse();

        text.ShouldBeEmpty();
        _picker.IsOpen.ShouldBeTrue();
        _picker.ErrorMessage.ShouldStartWith("JSON format: ");
        _log.Entries.ShouldContain(entry => entry.Activity == ActivityEvent.TransformFailed && entry.Details.Error == ErrorCode.TransformInputInvalid);
    }

    [Fact]
    public void The_error_goes_away_when_the_user_moves_on()
    {
        _picker.Open("this is not json", isSensitive: false);
        Highlight("JSON format");
        _picker.TryConfirm(out _).ShouldBeFalse();

        _picker.MoveDownCommand.Execute(null);

        _picker.ErrorMessage.ShouldBeNull();
    }

    [Fact]
    public void Enter_with_nothing_listed_does_nothing()
    {
        _picker.Open("x", isSensitive: false);
        _picker.FilterText = "no such transform";

        _picker.TryConfirm(out _).ShouldBeFalse();

        _picker.IsOpen.ShouldBeTrue();
        _log.Entries.ShouldBeEmpty();
    }

    [Fact]
    public void A_closed_picker_applies_nothing()
    {
        _picker.TryConfirm(out _).ShouldBeFalse();
        _picker.TrySubmitFindReplace(out _).ShouldBeFalse();
        _log.Entries.ShouldBeEmpty();
    }

    [Fact]
    public void The_log_never_gets_any_text()
    {
        _picker.Open("secret-marker", isSensitive: false);
        Highlight("Base64 encode");
        Confirm();

        _log.Entries.ShouldAllBe(entry => entry.Details.ExceptionType == null);
        _log.Entries.Select(entry => entry.ToString()).ShouldAllBe(text => !text!.Contains("secret-marker", StringComparison.Ordinal));
    }

    // --- find and replace ---

    [Fact]
    public void Enter_on_find_and_replace_asks_for_the_text_first_and_applies_it_once_given()
    {
        _picker.Open("one two one", isSensitive: false);
        Highlight("Find and replace\u2026");

        _picker.TryConfirm(out _).ShouldBeFalse();
        _picker.IsAskingFindReplace.ShouldBeTrue();
        _picker.IsOpen.ShouldBeTrue();

        _picker.FindText = "one";
        _picker.ReplaceText = "1";
        _picker.TrySubmitFindReplace(out var text).ShouldBeTrue();

        text.ShouldBe("1 two 1");
        _picker.IsOpen.ShouldBeFalse();
    }

    [Fact]
    public void Ctrl_Space_on_find_and_replace_adds_it_to_the_chain_instead_of_applying_it()
    {
        _picker.Open("  one two one  ", isSensitive: false);
        Highlight("Find and replace\u2026");

        _picker.AddToChainCommand.Execute(null);
        _picker.IsAskingFindReplace.ShouldBeTrue();
        _picker.FindText = "one";
        _picker.ReplaceText = "1";
        _picker.TrySubmitFindReplace(out _).ShouldBeFalse();

        _picker.IsAskingFindReplace.ShouldBeFalse();
        _picker.IsOpen.ShouldBeTrue();
        _picker.ChainText.ShouldBe("Find and replace");

        Highlight("Trim");
        _picker.AddToChainCommand.Execute(null);
        Confirm().ShouldBe("1 two 1");
    }

    [Fact]
    public void Find_and_replace_can_use_a_pattern_and_ignore_case()
    {
        _picker.Open("Item 12, item 7", isSensitive: false);
        Highlight("Find and replace\u2026");
        _picker.TryConfirm(out _).ShouldBeFalse();

        _picker.FindText = @"item (\d+)";
        _picker.ReplaceText = "#$1";
        _picker.UseRegex = true;
        _picker.IgnoreCase = true;

        _picker.TrySubmitFindReplace(out var text).ShouldBeTrue();
        text.ShouldBe("#12, #7");
    }

    [Fact]
    public void Nothing_to_find_is_not_accepted()
    {
        _picker.Open("x", isSensitive: false);
        Highlight("Find and replace\u2026");
        _picker.TryConfirm(out _).ShouldBeFalse();

        _picker.TrySubmitFindReplace(out _).ShouldBeFalse();

        _picker.IsAskingFindReplace.ShouldBeTrue();
        _picker.ErrorMessage.ShouldBe("Enter the text to find.");
    }

    [Fact]
    public void A_pattern_that_is_not_valid_is_reported_without_repeating_it()
    {
        _picker.Open("x", isSensitive: false);
        Highlight("Find and replace\u2026");
        _picker.TryConfirm(out _).ShouldBeFalse();
        _picker.FindText = "(secret-marker";
        _picker.UseRegex = true;

        _picker.TrySubmitFindReplace(out _).ShouldBeFalse();

        _picker.ErrorMessage.ShouldNotBeNull();
        _picker.ErrorMessage.ShouldNotContain("secret-marker");
        _picker.IsAskingFindReplace.ShouldBeTrue();
    }

    [Fact]
    public void Submitting_without_having_asked_does_nothing()
    {
        _picker.Open("x", isSensitive: false);

        _picker.TrySubmitFindReplace(out _).ShouldBeFalse();

        _picker.ChainText.ShouldBeEmpty();
    }

    // --- backing out ---

    [Fact]
    public void Cancel_leaves_the_find_and_replace_fields_first_and_then_the_picker()
    {
        _picker.Open("x", isSensitive: false);
        Highlight("Find and replace\u2026");
        _picker.TryConfirm(out _).ShouldBeFalse();

        _picker.Cancel();
        _picker.IsAskingFindReplace.ShouldBeFalse();
        _picker.IsOpen.ShouldBeTrue();

        _picker.Cancel();
        _picker.IsOpen.ShouldBeFalse();
    }

    [Fact]
    public void Closing_forgets_everything_that_was_typed()
    {
        _picker.Open("x", isSensitive: false);
        Highlight("Find and replace\u2026");
        _picker.TryConfirm(out _).ShouldBeFalse();
        _picker.FindText = "secret-marker";
        _picker.ReplaceText = "other-marker";
        _picker.UseRegex = true;
        _picker.IgnoreCase = true;

        _picker.Close();

        _picker.IsOpen.ShouldBeFalse();
        _picker.IsAskingFindReplace.ShouldBeFalse();
        _picker.FindText.ShouldBeEmpty();
        _picker.ReplaceText.ShouldBeEmpty();
        _picker.UseRegex.ShouldBeFalse();
        _picker.IgnoreCase.ShouldBeFalse();
        _picker.Preview.ShouldBeEmpty();
        _picker.Items.ShouldBeEmpty();
        _picker.ChainText.ShouldBeEmpty();
    }

    [Fact]
    public void The_key_hints_follow_what_is_shown()
    {
        _picker.Open("x", isSensitive: false);
        _picker.StatusText.ShouldBe("Enter apply \u00B7 Ctrl+Space add to chain \u00B7 Esc back");

        Highlight("Trim");
        _picker.AddToChainCommand.Execute(null);
        _picker.StatusText.ShouldBe("Enter apply chain \u00B7 Ctrl+Space add \u00B7 Backspace remove last \u00B7 Esc back");

        Highlight("Find and replace\u2026");
        _picker.AddToChainCommand.Execute(null);
        _picker.StatusText.ShouldBe("Enter add to chain \u00B7 Tab next field \u00B7 Esc back");

        _picker.Open("x", isSensitive: false);
        Highlight("Find and replace\u2026");
        _picker.TryConfirm(out _).ShouldBeFalse();
        _picker.StatusText.ShouldBe("Enter apply \u00B7 Tab next field \u00B7 Esc back");
    }

    [Fact]
    public void The_list_and_the_find_and_replace_fields_take_turns_on_screen()
    {
        var changes = new List<string?>();
        _picker.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        _picker.Open("x", isSensitive: false);
        _picker.IsChoosing.ShouldBeTrue();

        Highlight("Find and replace\u2026");
        _picker.TryConfirm(out _).ShouldBeFalse();
        _picker.IsChoosing.ShouldBeFalse();
        changes.ShouldContain(nameof(TransformPickerViewModel.IsChoosing));

        _picker.Cancel();
        _picker.IsChoosing.ShouldBeTrue();
    }

    [Fact]
    public void The_preview_says_whether_it_is_a_result_or_a_reason()
    {
        var changes = new List<string?>();
        _picker.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        _picker.Open("this is not json", isSensitive: false);
        _picker.PreviewIsResult.ShouldBeTrue();

        Highlight("JSON format");
        _picker.PreviewIsResult.ShouldBeFalse();
        changes.ShouldContain(nameof(TransformPickerViewModel.PreviewIsResult));

        Highlight("Trim");
        _picker.PreviewIsResult.ShouldBeTrue();
    }

    [Fact]
    public void Missing_arguments_are_bugs()
    {
        Should.Throw<ArgumentNullException>(() => new TransformPickerViewModel(null!, new TransformPipeline(), _log));
        Should.Throw<ArgumentNullException>(() => new TransformPickerViewModel(TransformRegistry.Default, null!, _log));
        Should.Throw<ArgumentNullException>(() => new TransformPickerViewModel(TransformRegistry.Default, new TransformPipeline(), null!));
        Should.Throw<ArgumentNullException>(() => _picker.Open(null!, isSensitive: false));
    }
}
