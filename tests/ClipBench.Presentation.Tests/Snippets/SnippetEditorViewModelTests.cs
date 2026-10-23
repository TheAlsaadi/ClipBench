using ClipBench.Core.Results;
using ClipBench.Core.Snippets;
using ClipBench.Presentation.Snippets;
using ClipBench.Presentation.Tests.Support;

namespace ClipBench.Presentation.Tests.Snippets;

public sealed class SnippetEditorViewModelTests : IDisposable
{
    private readonly FakeSnippetStore _store = new();
    private readonly SnippetCatalog _catalog;
    private SnippetEditorViewModel _editor;

    public SnippetEditorViewModelTests()
    {
        _catalog = new SnippetCatalog(_store);
        _editor = Create();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _editor.Dispose();
        _catalog.Dispose();
    }

    private SnippetEditorViewModel Create() => new(_catalog, new SnippetEngine(new SnippetTestClock()), new ImmediateDispatcher());

    // Makes snippets first and then a fresh editor over them, as the window does each time it is opened.
    private async Task<Snippet[]> WithSnippetsAsync(params (string Name, string Body)[] snippets)
    {
        var made = new List<Snippet>();
        foreach (var (name, body) in snippets)
        {
            var created = await _catalog.CreateAsync(name, body, Ct);
            created.IsSuccess.ShouldBeTrue(created.IsFailure ? created.Error.Message : null);
            made.Add(created.Value);
        }

        _editor.Dispose();
        _editor = Create();
        return [.. made];
    }

    private string[] Names() => [.. _editor.Items.Select(item => item.Name)];

    // --- starting ---

    [Fact]
    public void With_no_snippets_a_new_one_is_ready_to_write()
    {
        _editor.Items.ShouldBeEmpty();
        _editor.SelectedIndex.ShouldBe(-1);
        _editor.IsNew.ShouldBeTrue();
        _editor.CanEdit.ShouldBeTrue();
        _editor.HasSelection.ShouldBeFalse();
        _editor.Name.ShouldBeEmpty();
        _editor.Body.ShouldBeEmpty();
        _editor.IsDirty.ShouldBeFalse();
    }

    [Fact]
    public async Task With_snippets_the_first_is_shown()
    {
        await WithSnippetsAsync(("Signature", "Regards"), ("Date", "{date}"));

        Names().ShouldBe(["Signature", "Date"]);
        _editor.SelectedIndex.ShouldBe(0);
        _editor.Name.ShouldBe("Signature");
        _editor.Body.ShouldBe("Regards");
        _editor.IsNew.ShouldBeFalse();
        _editor.HasSelection.ShouldBeTrue();
        _editor.IsDirty.ShouldBeFalse();
    }

    // --- editing ---

    [Fact]
    public async Task Typing_makes_the_snippet_dirty_and_putting_it_back_makes_it_clean_again()
    {
        await WithSnippetsAsync(("Signature", "Regards"));

        _editor.Body = "Regards,\r\nSam";
        _editor.IsDirty.ShouldBeTrue();

        _editor.Body = "Regards";
        _editor.IsDirty.ShouldBeFalse();

        _editor.Name = "Sig";
        _editor.IsDirty.ShouldBeTrue();
    }

    [Fact]
    public void A_new_snippet_is_dirty_once_something_is_typed()
    {
        _editor.Name = "Hello";

        _editor.IsDirty.ShouldBeTrue();

        _editor.Name = string.Empty;
        _editor.IsDirty.ShouldBeFalse();
    }

    [Fact]
    public async Task Choosing_another_snippet_shows_it()
    {
        await WithSnippetsAsync(("One", "first"), ("Two", "second"));

        _editor.SelectedIndex = 1;

        _editor.Name.ShouldBe("Two");
        _editor.Body.ShouldBe("second");
        _editor.IsDirty.ShouldBeFalse();
    }

    [Fact]
    public async Task Choosing_another_snippet_with_unsaved_changes_is_refused_and_says_why()
    {
        await WithSnippetsAsync(("One", "first"), ("Two", "second"));
        _editor.Body = "changed";

        _editor.SelectedIndex = 1;

        _editor.SelectedIndex.ShouldBe(0);
        _editor.Body.ShouldBe("changed");
        _editor.ErrorMessage.ShouldBe("Save or discard your changes first.");
    }

    [Fact]
    public async Task Starting_a_new_snippet_shows_empty_boxes()
    {
        await WithSnippetsAsync(("One", "first"));

        _editor.NewCommand.Execute(null);

        _editor.IsNew.ShouldBeTrue();
        _editor.SelectedIndex.ShouldBe(-1);
        _editor.HasSelection.ShouldBeFalse();
        _editor.Name.ShouldBeEmpty();
        _editor.Body.ShouldBeEmpty();
        _editor.IsDirty.ShouldBeFalse();
    }

    [Fact]
    public async Task Starting_a_new_snippet_with_unsaved_changes_is_refused()
    {
        await WithSnippetsAsync(("One", "first"));
        _editor.Name = "changed";

        _editor.NewCommand.Execute(null);

        _editor.IsNew.ShouldBeFalse();
        _editor.Name.ShouldBe("changed");
        _editor.ErrorMessage.ShouldBe("Save or discard your changes first.");
    }

    // --- saving ---

    [Fact]
    public async Task A_new_snippet_is_saved_chosen_and_clean()
    {
        _editor.Name = "  Signature ";
        _editor.Body = "Regards,\r\n{input:Name}";

        await _editor.SaveCommand.ExecuteAsync(null);

        _catalog.Snippets.Single().Name.ShouldBe("Signature");
        _catalog.Snippets.Single().Body.ShouldBe("Regards,\r\n{input:Name}");
        Names().ShouldBe(["Signature"]);
        _editor.SelectedIndex.ShouldBe(0);
        _editor.IsNew.ShouldBeFalse();
        _editor.IsDirty.ShouldBeFalse();
        _editor.ErrorMessage.ShouldBeNull();
        _editor.Name.ShouldBe("Signature");
    }

    [Fact]
    public async Task A_second_new_snippet_goes_after_the_first_and_is_the_one_chosen()
    {
        await WithSnippetsAsync(("One", "first"));
        _editor.NewCommand.Execute(null);
        _editor.Name = "Two";
        _editor.Body = "second";

        await _editor.SaveCommand.ExecuteAsync(null);

        Names().ShouldBe(["One", "Two"]);
        _editor.SelectedIndex.ShouldBe(1);
    }

    [Fact]
    public async Task A_changed_snippet_is_saved_where_it_was()
    {
        await WithSnippetsAsync(("One", "first"), ("Two", "second"));
        _editor.SelectedIndex = 1;
        _editor.Name = "Second";
        _editor.Body = "changed";

        await _editor.SaveCommand.ExecuteAsync(null);

        _catalog.Snippets[1].Name.ShouldBe("Second");
        _catalog.Snippets[1].Body.ShouldBe("changed");
        Names().ShouldBe(["One", "Second"]);
        _editor.SelectedIndex.ShouldBe(1);
        _editor.IsDirty.ShouldBeFalse();
    }

    [Fact]
    public async Task A_name_that_is_not_allowed_stays_in_the_box_with_the_reason()
    {
        _editor.Name = "   ";
        _editor.Body = "text";

        await _editor.SaveCommand.ExecuteAsync(null);

        _catalog.Snippets.ShouldBeEmpty();
        _editor.ErrorMessage.ShouldBe("Give the snippet a name.");
        _editor.IsNew.ShouldBeTrue();
        _editor.Body.ShouldBe("text");
    }

    [Fact]
    public async Task A_name_that_is_taken_is_refused()
    {
        await WithSnippetsAsync(("One", "first"));
        _editor.NewCommand.Execute(null);
        _editor.Name = "ONE";

        await _editor.SaveCommand.ExecuteAsync(null);

        _editor.ErrorMessage.ShouldBe("Another snippet already has that name.");
        _catalog.Snippets.Length.ShouldBe(1);
    }

    [Fact]
    public async Task A_text_that_is_too_long_is_refused()
    {
        _editor.Name = "Long";
        _editor.Body = new string('a', SnippetEngine.MaxBodyLength + 1);

        await _editor.SaveCommand.ExecuteAsync(null);

        _editor.ErrorMessage.ShouldNotBeNull();
        _catalog.Snippets.ShouldBeEmpty();
    }

    [Fact]
    public async Task When_the_store_fails_the_message_says_so_and_the_snippet_is_still_listed()
    {
        _store.Failure = new ResultError(ErrorCode.StorageFailure, "the disk said no");
        _editor.Name = "One";

        await _editor.SaveCommand.ExecuteAsync(null);

        _editor.ErrorMessage.ShouldNotBeNull();
        Names().ShouldBe(["One"]);
    }

    [Fact]
    public async Task Saving_with_nothing_to_save_does_nothing()
    {
        await WithSnippetsAsync(("One", "first"));
        var saved = _store.Saved.Count;

        await _editor.SaveCommand.ExecuteAsync(null);

        _store.Saved.Count.ShouldBe(saved);
        _editor.ErrorMessage.ShouldBeNull();
    }

    // --- discarding ---

    [Fact]
    public async Task Discarding_puts_back_what_is_saved()
    {
        await WithSnippetsAsync(("One", "first"));
        _editor.Name = "changed";
        _editor.Body = "changed";
        _editor.ErrorMessage = "an old message";

        _editor.DiscardCommand.Execute(null);

        _editor.Name.ShouldBe("One");
        _editor.Body.ShouldBe("first");
        _editor.IsDirty.ShouldBeFalse();
        _editor.ErrorMessage.ShouldBeNull();
    }

    [Fact]
    public void Discarding_a_new_snippet_empties_the_boxes()
    {
        _editor.Name = "typed";
        _editor.Body = "typed";

        _editor.DiscardCommand.Execute(null);

        _editor.Name.ShouldBeEmpty();
        _editor.Body.ShouldBeEmpty();
        _editor.IsDirty.ShouldBeFalse();
    }

    // --- deleting ---

    [Fact]
    public async Task A_snippet_is_deleted_after_a_question_and_the_next_one_is_shown()
    {
        await WithSnippetsAsync(("One", "first"), ("Two", "second"), ("Three", "third"));
        _editor.SelectedIndex = 1;

        await _editor.DeleteCommand.ExecuteAsync(null);
        _editor.IsConfirmingDelete.ShouldBeTrue();
        _editor.DeleteQuestion.ShouldContain("Two");
        Names().ShouldBe(["One", "Two", "Three"]);

        await _editor.DeleteCommand.ExecuteAsync(null);

        _editor.IsConfirmingDelete.ShouldBeFalse();
        Names().ShouldBe(["One", "Three"]);
        _editor.SelectedIndex.ShouldBe(1);
        _editor.Name.ShouldBe("Three");
    }

    [Fact]
    public async Task Deleting_the_last_snippet_leaves_a_new_one_ready()
    {
        await WithSnippetsAsync(("Only", "text"));

        await _editor.DeleteCommand.ExecuteAsync(null);
        await _editor.DeleteCommand.ExecuteAsync(null);

        _editor.Items.ShouldBeEmpty();
        _editor.IsNew.ShouldBeTrue();
        _editor.Name.ShouldBeEmpty();
    }

    [Fact]
    public async Task Backing_out_of_the_question_keeps_the_snippet()
    {
        await WithSnippetsAsync(("One", "first"));
        await _editor.DeleteCommand.ExecuteAsync(null);

        _editor.CancelDeleteCommand.Execute(null);

        _editor.IsConfirmingDelete.ShouldBeFalse();
        Names().ShouldBe(["One"]);
    }

    [Fact]
    public async Task Typing_withdraws_the_question()
    {
        await WithSnippetsAsync(("One", "first"));
        await _editor.DeleteCommand.ExecuteAsync(null);

        _editor.Body = "more";

        _editor.IsConfirmingDelete.ShouldBeFalse();
    }

    [Fact]
    public async Task Nothing_is_deleted_while_a_new_snippet_is_being_written()
    {
        await WithSnippetsAsync(("One", "first"));
        _editor.NewCommand.Execute(null);

        await _editor.DeleteCommand.ExecuteAsync(null);

        _editor.IsConfirmingDelete.ShouldBeFalse();
        Names().ShouldBe(["One"]);
    }

    // --- warnings and preview ---

    [Fact]
    public void A_text_without_problems_has_no_warnings()
    {
        _editor.Body = "Dear {input:Name}, today is {date}.";

        _editor.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public void Each_problem_is_described_with_its_line()
    {
        _editor.Body = "{nope} fine\r\n{date:%}\r\n{input:}\n{date";

        _editor.Warnings.ShouldBe(
        [
            "Line 1: \"{nope}\" is not a variable, so it is pasted as typed.",
            "Line 2: \"{date:%}\" has a date or time format that cannot be used, so it is pasted as typed.",
            "Line 3: \"{input:}\" needs a name of up to 40 letters, digits, spaces, hyphens or underscores.",
            "Line 4: a { is never closed. Type {{ to write a brace.",
        ]);
    }

    [Fact]
    public void A_long_variable_is_cut_in_the_warning()
    {
        _editor.Body = "{" + new string('x', 100) + "}";

        _editor.Warnings.Single().Length.ShouldBeLessThan(150);
        _editor.Warnings.Single().ShouldContain("\u2026");
    }

    [Fact]
    public void The_warnings_follow_the_typing()
    {
        _editor.Body = "{nope}";
        _editor.Warnings.Count.ShouldBe(1);

        _editor.Body = "{date}";
        _editor.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public void A_text_that_cannot_be_used_says_why_in_the_warnings_and_has_no_preview()
    {
        _editor.Body = new string('a', SnippetEngine.MaxBodyLength + 1);

        _editor.Warnings.Single().ShouldBe("A snippet can have at most 10,000 characters.");
        _editor.Preview.ShouldBeEmpty();
    }

    [Fact]
    public void The_preview_shows_what_would_be_pasted_with_placeholders_for_the_values()
    {
        _editor.Body = "Hi {input:Name}, it is {date} {time}. You copied: {clipboard}";

        _editor.Preview.ShouldBe("Hi <Name>, it is 2026-10-24 00:30. You copied: <clipboard>");
    }

    [Fact]
    public void An_empty_text_has_an_empty_preview()
    {
        _editor.Body = "x";
        _editor.Body = string.Empty;

        _editor.Preview.ShouldBeEmpty();
    }

    [Fact]
    public void A_long_preview_is_cut()
    {
        _editor.Body = new string('a', 2000);

        _editor.Preview.Length.ShouldBeLessThanOrEqualTo(SnippetEditorViewModel.PreviewLimit + 1);
    }

    // --- changes made elsewhere ---

    [Fact]
    public async Task A_snippet_made_elsewhere_appears_and_the_choice_stays()
    {
        var made = await WithSnippetsAsync(("One", "first"));

        await _catalog.CreateAsync("Two", "second", Ct);

        Names().ShouldBe(["One", "Two"]);
        _editor.SelectedIndex.ShouldBe(0);
        _editor.Name.ShouldBe("One");
        made[0].Name.ShouldBe("One");
    }

    [Fact]
    public async Task When_the_chosen_snippet_is_deleted_elsewhere_the_boxes_stay_as_they_were_as_a_new_snippet()
    {
        var made = await WithSnippetsAsync(("One", "first"), ("Two", "second"));
        _editor.Body = "my edits";

        await _catalog.DeleteAsync(made[0].Id, Ct);

        Names().ShouldBe(["Two"]);
        _editor.IsNew.ShouldBeTrue();
        _editor.SelectedIndex.ShouldBe(-1);
        _editor.Name.ShouldBe("One");
        _editor.Body.ShouldBe("my edits");
        _editor.IsDirty.ShouldBeTrue();
    }

    [Fact]
    public async Task A_clean_snippet_changed_elsewhere_shows_the_new_text_but_unsaved_edits_are_kept()
    {
        var made = await WithSnippetsAsync(("One", "first"));

        await _catalog.UpdateAsync(made[0].Id, "One", "changed elsewhere", Ct);
        _editor.Body.ShouldBe("changed elsewhere");

        _editor.Body = "my edits";
        await _catalog.UpdateAsync(made[0].Id, "One", "changed again", Ct);
        _editor.Body.ShouldBe("my edits");
    }

    // --- bugs and clean-up ---

    [Fact]
    public async Task After_disposing_changes_are_no_longer_followed()
    {
        _editor.Dispose();

        await _catalog.CreateAsync("Late", "x", Ct);

        _editor.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Printing_never_shows_a_name()
    {
        var made = await WithSnippetsAsync(("secret-marker", "secret-body"));

        _editor.Items[0].ToString().ShouldNotContain("secret");
        made[0].ToString().ShouldNotContain("secret");
    }

    [Fact]
    public void Missing_arguments_are_bugs()
    {
        Should.Throw<ArgumentNullException>(() => new SnippetEditorViewModel(null!, new SnippetEngine(new SnippetTestClock()), new ImmediateDispatcher()));
        Should.Throw<ArgumentNullException>(() => new SnippetEditorViewModel(_catalog, null!, new ImmediateDispatcher()));
        Should.Throw<ArgumentNullException>(() => new SnippetEditorViewModel(_catalog, new SnippetEngine(new SnippetTestClock()), null!));
    }
}
