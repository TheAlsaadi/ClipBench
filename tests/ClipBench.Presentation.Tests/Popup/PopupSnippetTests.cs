using ClipBench.Core.Boards;
using ClipBench.Core.Clips;
using ClipBench.Core.History;
using ClipBench.Core.Settings;
using ClipBench.Core.Snippets;
using ClipBench.Presentation.Boards;
using ClipBench.Presentation.Collect;
using ClipBench.Presentation.Popup;
using ClipBench.Presentation.Snippets;
using ClipBench.Presentation.Tests.Support;

namespace ClipBench.Presentation.Tests.Popup;

/// <summary>The Snippets view of the popup: a list of snippets, and Enter pastes one with its variables filled in.</summary>
public sealed class PopupSnippetTests : IDisposable
{
    private readonly ManualTimeProvider _time = new();
    private readonly FakePasteFlow _paste = new();
    private readonly ClipLibrary _library;
    private readonly BoardCatalog _boards;
    private readonly SnippetCatalog _catalog;
    private readonly SidebarViewModel _sidebar;
    private readonly SnippetsViewModel _snippets;
    private readonly SettingsService _settings = new(new FakeSettingsStore());
    private readonly PopupViewModel _vm;

    public PopupSnippetTests()
    {
        _library = new ClipLibrary(new ClipHistory(new HistoryOptions(), _time), new FakeClipStore());
        _boards = new BoardCatalog(_library, new FakeBoardStore());
        _catalog = new SnippetCatalog(new FakeSnippetStore());
        _sidebar = TestSidebars.New(_library, _boards, _catalog);
        _snippets = TestSnippets.New(_catalog);
        _vm = new PopupViewModel(_library, _paste, new FakeQueueControl(), new CollectToggle(new FakeCollectControl(), new FakeNotifier()), TestPickers.New(), _sidebar, _snippets, _settings, new ImmediateDispatcher(), _time);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _vm.Dispose();
        _snippets.Dispose();
        _sidebar.Dispose();
        _catalog.Dispose();
        _boards.Dispose();
        _settings.Dispose();
        _library.Dispose();
    }

    private async Task<Snippet> AddSnippetAsync(string name, string body)
    {
        var created = await _catalog.CreateAsync(name, body, Ct);
        created.IsSuccess.ShouldBeTrue(created.IsFailure ? created.Error.Message : null);
        return created.Value;
    }

    private async Task AddClipAsync(Clip clip) => (await _library.AddAsync(clip, Ct)).IsSuccess.ShouldBeTrue();

    // The snippets entry is the last one: History, Pinned, then Snippets when there are no boards.
    private void ChooseSnippets() => _sidebar.SelectEntryCommand.Execute(_sidebar.Entries.Count - 1);

    private string[] Shown() => [.. _snippets.Rows.Select(row => row.Name)];

    private string PastedText() => ((TextContent)_paste.PastedContent.Single()).Text;

    // --- the list ---

    [Fact]
    public async Task The_snippets_view_lists_the_snippets_and_none_of_the_clips()
    {
        await AddClipAsync(TestClips.Text("a clip"));
        await AddSnippetAsync("Signature", "Regards");
        await AddSnippetAsync("Date", "{date}");
        _vm.Open();

        ChooseSnippets();

        Shown().ShouldBe(["Signature", "Date"]);
        _vm.Rows.ShouldBeEmpty();
        _vm.ItemCount.ShouldBe(2);
        _vm.ShowEmptyState.ShouldBeFalse();
        _vm.SelectedIndex.ShouldBe(0);
        _vm.CurrentSnippet!.Name.ShouldBe("Signature");
        _vm.Current.ShouldBeNull();
    }

    [Fact]
    public async Task The_window_is_told_which_list_to_show()
    {
        await AddSnippetAsync("Signature", "Regards");
        _vm.Open();
        _vm.IsClipsView.ShouldBeTrue();
        _vm.IsSnippetsView.ShouldBeFalse();
        var changes = new List<string?>();
        _vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        ChooseSnippets();

        _vm.IsSnippetsView.ShouldBeTrue();
        _vm.IsClipsView.ShouldBeFalse();
        changes.ShouldContain(nameof(PopupViewModel.IsSnippetsView));
        changes.ShouldContain(nameof(PopupViewModel.IsClipsView));
    }

    [Fact]
    public async Task Choosing_history_again_brings_the_clips_back()
    {
        await AddClipAsync(TestClips.Text("a clip"));
        await AddSnippetAsync("Signature", "Regards");
        _vm.Open();
        ChooseSnippets();

        _sidebar.SelectEntryCommand.Execute(0);

        _vm.Rows.Count.ShouldBe(1);
        _vm.ItemCount.ShouldBe(1);
        _vm.CurrentSnippet.ShouldBeNull();
    }

    [Fact]
    public async Task The_search_filters_the_snippets()
    {
        await AddSnippetAsync("Signature", "Regards");
        await AddSnippetAsync("Date", "today");
        _vm.Open();
        ChooseSnippets();

        _vm.SearchText = "sig";
        _time.FireNext().ShouldBeTrue();

        Shown().ShouldBe(["Signature"]);
    }

    [Fact]
    public async Task The_arrow_keys_walk_the_snippets_and_wrap_around()
    {
        await AddSnippetAsync("One", "1");
        await AddSnippetAsync("Two", "2");
        await AddSnippetAsync("Three", "3");
        _vm.Open();
        ChooseSnippets();

        _vm.MoveDownCommand.Execute(null);
        _vm.CurrentSnippet!.Name.ShouldBe("Two");

        _vm.MoveUpCommand.Execute(null);
        _vm.MoveUpCommand.Execute(null);
        _vm.CurrentSnippet!.Name.ShouldBe("Three");

        _vm.MoveDownCommand.Execute(null);
        _vm.CurrentSnippet!.Name.ShouldBe("One");
    }

    [Fact]
    public async Task A_snippet_made_while_the_view_is_shown_appears_in_it()
    {
        _vm.Open();
        ChooseSnippets();
        _vm.ShowEmptyState.ShouldBeTrue();

        await AddSnippetAsync("Late", "x");

        Shown().ShouldBe(["Late"]);
        _vm.ShowEmptyState.ShouldBeFalse();
    }

    [Fact]
    public async Task A_snippet_deleted_while_the_view_is_shown_leaves_it()
    {
        var gone = await AddSnippetAsync("Gone", "x");
        await AddSnippetAsync("Kept", "y");
        _vm.Open();
        ChooseSnippets();

        await _catalog.DeleteAsync(gone.Id, Ct);

        Shown().ShouldBe(["Kept"]);
    }

    [Fact]
    public async Task Opening_the_popup_starts_on_history_even_after_snippets_were_shown()
    {
        await AddSnippetAsync("Signature", "Regards");
        _vm.Open();
        ChooseSnippets();
        _vm.Close();

        _vm.Open();

        _sidebar.Scope.ShouldBe(PopupScope.History);
    }

    [Fact]
    public async Task Ctrl_E_closes_the_popup_and_asks_for_the_snippet_editor()
    {
        await AddSnippetAsync("Signature", "Regards");
        _vm.Open();
        var asked = 0;
        _vm.EditSnippetsRequested += (_, _) => asked++;

        _vm.EditSnippetsCommand.Execute(null);

        _vm.IsOpen.ShouldBeFalse();
        asked.ShouldBe(1);
    }

    [Fact]
    public void The_editor_can_be_asked_for_while_the_popup_is_closed_too()
    {
        var asked = 0;
        _vm.EditSnippetsRequested += (_, _) => asked++;

        _vm.EditSnippetsCommand.Execute(null);

        asked.ShouldBe(1);
    }

    [Fact]
    public async Task Each_empty_snippet_view_says_what_to_do()
    {
        _vm.Open();
        ChooseSnippets();
        _vm.EmptyText.ShouldBe("No snippets yet. Press Ctrl+E to make one.");

        await AddSnippetAsync("Signature", "Regards");
        _vm.SearchText = "zzz";
        _time.FireNext().ShouldBeTrue();
        _vm.EmptyText.ShouldBe("No snippets match your search.");
    }

    // --- pasting ---

    [Fact]
    public async Task Enter_hides_the_popup_and_pastes_the_snippet_with_its_variables_filled_in()
    {
        await AddSnippetAsync("Note", "{date} {time}");
        _vm.Open();
        ChooseSnippets();
        var closedBeforePaste = false;
        _vm.CloseRequested += (_, _) => closedBeforePaste = _paste.PastedContent.Count == 0;

        await _vm.PasteCommand.ExecuteAsync(null);

        closedBeforePaste.ShouldBeTrue();
        PastedText().ShouldBe("2026-10-24 00:30");
        _paste.Pasted.ShouldBeEmpty();
        _vm.IsOpen.ShouldBeFalse();
    }

    [Fact]
    public async Task The_clipboard_variable_is_the_newest_clip_that_has_text()
    {
        await AddClipAsync(TestClips.Text("older text", FakeCopyTime(0)));
        await AddClipAsync(TestClips.Text("newest text", FakeCopyTime(1)));
        await AddSnippetAsync("Quote", "> {clipboard}");
        _vm.Open();
        ChooseSnippets();

        await _vm.PasteCommand.ExecuteAsync(null);

        PastedText().ShouldBe("> newest text");
    }

    [Fact]
    public async Task A_newer_picture_or_list_of_files_is_skipped_for_the_clipboard_variable()
    {
        await AddClipAsync(TestClips.Text("the text", FakeCopyTime(0)));
        await AddClipAsync(TestClips.Image(1));
        await AddClipAsync(TestClips.Files(paths: [@"C:\a.txt"]));
        await AddSnippetAsync("Quote", "[{clipboard}]");
        _vm.Open();
        ChooseSnippets();

        await _vm.PasteCommand.ExecuteAsync(null);

        PastedText().ShouldBe("[the text]");
    }

    [Fact]
    public async Task With_no_text_in_the_history_the_clipboard_variable_is_empty()
    {
        await AddClipAsync(TestClips.Image(1));
        await AddSnippetAsync("Quote", "[{clipboard}]");
        _vm.Open();
        ChooseSnippets();

        await _vm.PasteCommand.ExecuteAsync(null);

        PastedText().ShouldBe("[]");
    }

    [Fact]
    public async Task Pasting_a_snippet_adds_nothing_to_the_history()
    {
        await AddSnippetAsync("Note", "{date}");
        _vm.Open();
        ChooseSnippets();

        await _vm.PasteCommand.ExecuteAsync(null);

        _library.Clips.ShouldBeEmpty();
    }

    [Fact]
    public async Task Enter_on_the_highlighted_snippet_pastes_that_one()
    {
        await AddSnippetAsync("One", "first");
        await AddSnippetAsync("Two", "second");
        _vm.Open();
        ChooseSnippets();
        _vm.MoveDownCommand.Execute(null);

        await _vm.PasteCommand.ExecuteAsync(null);

        PastedText().ShouldBe("second");
    }

    [Fact]
    public async Task With_no_snippets_Enter_does_nothing()
    {
        _vm.Open();
        ChooseSnippets();

        await _vm.PasteCommand.ExecuteAsync(null);

        _paste.PastedContent.ShouldBeEmpty();
        _vm.ErrorMessage.ShouldBeNull();
    }

    // --- values that are asked for ---

    [Fact]
    public async Task A_snippet_that_asks_for_values_opens_the_prompt_and_pastes_after_it_is_filled_in()
    {
        await AddSnippetAsync("Reply", "Dear {input:Name}, {input:Topic}!");
        _vm.Open();
        ChooseSnippets();

        await _vm.PasteCommand.ExecuteAsync(null);

        _paste.PastedContent.ShouldBeEmpty();
        _vm.IsPromptingSnippet.ShouldBeTrue();
        _vm.IsMainEnabled.ShouldBeFalse();
        _vm.IsOpen.ShouldBeTrue();
        _snippets.Fields.Select(field => field.Name).ShouldBe(["Name", "Topic"]);

        _snippets.Fields[0].Value = "Sam";
        _snippets.Fields[1].Value = "lunch";
        await _vm.SubmitSnippetPromptCommand.ExecuteAsync(null);

        PastedText().ShouldBe("Dear Sam, lunch!");
        _vm.IsOpen.ShouldBeFalse();
        _vm.IsPromptingSnippet.ShouldBeFalse();
        _vm.IsMainEnabled.ShouldBeTrue();
    }

    [Fact]
    public async Task The_clipboard_is_taken_when_the_paste_begins()
    {
        await AddClipAsync(TestClips.Text("before", FakeCopyTime(0)));
        await AddSnippetAsync("Reply", "{input:X} {clipboard}");
        _vm.Open();
        ChooseSnippets();
        await _vm.PasteCommand.ExecuteAsync(null);
        await AddClipAsync(TestClips.Text("after", FakeCopyTime(5)));
        _snippets.Fields[0].Value = "x";

        await _vm.SubmitSnippetPromptCommand.ExecuteAsync(null);

        PastedText().ShouldBe("x before");
    }

    [Fact]
    public async Task A_value_that_is_too_long_keeps_the_prompt_open_with_the_reason()
    {
        await AddSnippetAsync("Reply", "{input:X}");
        _vm.Open();
        ChooseSnippets();
        await _vm.PasteCommand.ExecuteAsync(null);
        _snippets.Fields[0].Value = new string('a', SnippetEngine.MaxInputLength + 1);

        await _vm.SubmitSnippetPromptCommand.ExecuteAsync(null);

        _paste.PastedContent.ShouldBeEmpty();
        _vm.IsPromptingSnippet.ShouldBeTrue();
        _snippets.PromptError.ShouldNotBeNull();
    }

    [Fact]
    public async Task Esc_closes_the_prompt_first_and_the_popup_on_the_next_press()
    {
        await AddSnippetAsync("Reply", "{input:X}");
        _vm.Open();
        ChooseSnippets();
        await _vm.PasteCommand.ExecuteAsync(null);

        _vm.EscapeCommand.Execute(null);
        _vm.IsPromptingSnippet.ShouldBeFalse();
        _vm.IsOpen.ShouldBeTrue();
        _vm.IsMainEnabled.ShouldBeTrue();

        _vm.EscapeCommand.Execute(null);
        _vm.IsOpen.ShouldBeFalse();
    }

    [Fact]
    public async Task Closing_the_popup_closes_the_prompt_and_forgets_what_was_typed()
    {
        await AddSnippetAsync("Reply", "{input:X}");
        _vm.Open();
        ChooseSnippets();
        await _vm.PasteCommand.ExecuteAsync(null);
        _snippets.Fields[0].Value = "secret-marker";

        _vm.Close();

        _vm.IsPromptingSnippet.ShouldBeFalse();
        _snippets.Fields.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_status_line_follows_the_view_and_the_prompt()
    {
        await AddSnippetAsync("Reply", "{input:X}");
        _vm.Open();
        var changes = new List<string?>();
        _vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        ChooseSnippets();
        _vm.StatusText.ShouldBe("Enter paste \u00B7 Ctrl+E edit snippets \u00B7 Esc close");

        await _vm.PasteCommand.ExecuteAsync(null);
        _vm.StatusText.ShouldBe("Enter paste \u00B7 Tab next value \u00B7 Esc cancel");
        changes.ShouldContain(nameof(PopupViewModel.IsPromptingSnippet));
        changes.ShouldContain(nameof(PopupViewModel.IsMainEnabled));
    }

    // --- clip commands do not apply to snippets ---

    [Fact]
    public async Task The_commands_for_clips_do_nothing_in_the_snippets_view()
    {
        await AddClipAsync(TestClips.Text("a clip"));
        await AddSnippetAsync("Signature", "Regards");
        _vm.Open();
        ChooseSnippets();

        _vm.ToggleMarkCommand.Execute(null);
        await _vm.TogglePinCommand.ExecuteAsync(null);
        await _vm.DeleteCommand.ExecuteAsync(null);
        _vm.OpenTransformsCommand.Execute(null);
        await _vm.QueueCommand.ExecuteAsync(null);
        await _vm.PasteTableCommand.ExecuteAsync(ClipBench.Core.Pasting.TableLayout.Column);

        _vm.MarkedCount.ShouldBe(0);
        _vm.IsTransformsOpen.ShouldBeFalse();
        _vm.ErrorMessage.ShouldBeNull();
        _library.Clips.Single().IsPinned.ShouldBeFalse();
        _catalog.Snippets.Length.ShouldBe(1);
    }

    [Fact]
    public void A_missing_snippets_view_model_is_a_bug()
    {
        Should.Throw<ArgumentNullException>(() => new PopupViewModel(_library, _paste, new FakeQueueControl(), new CollectToggle(new FakeCollectControl(), new FakeNotifier()), TestPickers.New(), _sidebar, null!, _settings, new ImmediateDispatcher(), _time));
    }

    private static DateTimeOffset FakeCopyTime(int minutes) => TestClips.Start.AddMinutes(minutes);
}
