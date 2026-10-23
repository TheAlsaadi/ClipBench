using ClipBench.Core.Results;
using ClipBench.Core.Snippets;
using ClipBench.Presentation.Snippets;
using ClipBench.Presentation.Tests.Support;

namespace ClipBench.Presentation.Tests.Snippets;

public sealed class SnippetsViewModelTests : IDisposable
{
    private readonly FakeSnippetStore _store = new();
    private readonly SnippetCatalog _catalog;
    private readonly SnippetsViewModel _vm;

    public SnippetsViewModelTests()
    {
        _catalog = new SnippetCatalog(_store);
        _vm = new SnippetsViewModel(_catalog, new SnippetEngine(new SnippetTestClock()));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _vm.Dispose();
        _catalog.Dispose();
    }

    private async Task<Snippet> AddAsync(string name, string body)
    {
        var created = await _catalog.CreateAsync(name, body, Ct);
        created.IsSuccess.ShouldBeTrue(created.IsFailure ? created.Error.Message : null);
        return created.Value;
    }

    private string[] Names() => [.. _vm.Rows.Select(row => row.Name)];

    // --- the list ---

    [Fact]
    public async Task All_snippets_are_listed_in_the_order_they_were_made_when_nothing_is_typed()
    {
        await AddAsync("Signature", "Regards");
        await AddAsync("Date", "{date}");

        _vm.Refresh(string.Empty);

        Names().ShouldBe(["Signature", "Date"]);
        _vm.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_row_shows_the_first_lines_of_the_text_and_how_many_values_it_asks_for()
    {
        await AddAsync("Reply", "Dear {input:Name},\r\n\r\nThanks for {input:Topic}.\r\nBest {input:Name}");

        _vm.Refresh(string.Empty);

        var row = _vm.Rows.Single();
        // Two lines, then a line that says there is more, as in the list of clips.
        row.Preview.ShouldBe("Dear {input:Name},\nThanks for {input:Topic}.\n\u2026");
        row.InputCount.ShouldBe(2);
        row.Details.ShouldBe("Asks for 2 values");
        row.HasDetails.ShouldBeTrue();
        row.AutomationName.ShouldBe("Snippet Reply, Dear {input:Name}, Thanks for {input:Topic}. \u2026, Asks for 2 values");
    }

    [Fact]
    public async Task A_row_that_asks_for_nothing_says_nothing_about_it()
    {
        await AddAsync("Plain", "Hello");
        await AddAsync("One", "{input:X}");

        _vm.Refresh(string.Empty);

        _vm.Rows[0].Details.ShouldBeEmpty();
        _vm.Rows[0].HasDetails.ShouldBeFalse();
        _vm.Rows[1].Details.ShouldBe("Asks for 1 value");
    }

    [Fact]
    public async Task A_long_first_line_is_cut()
    {
        await AddAsync("Long", new string('a', 500));

        _vm.Refresh(string.Empty);

        _vm.Rows.Single().Preview.Length.ShouldBeLessThanOrEqualTo(SnippetsViewModel.PreviewLength + 1);
    }

    [Fact]
    public async Task The_search_matches_names_and_texts_ignoring_capital_letters_with_names_first()
    {
        await AddAsync("Body only", "Please sign here");
        await AddAsync("Sign off", "Regards");
        await AddAsync("Other", "nothing");

        _vm.Refresh("SIGN");

        Names().ShouldBe(["Sign off", "Body only"]);
    }

    [Fact]
    public async Task A_search_with_no_match_lists_nothing()
    {
        await AddAsync("Signature", "Regards");

        _vm.Refresh("zzz");

        _vm.Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task Refreshing_again_replaces_the_rows()
    {
        await AddAsync("A", "a");
        _vm.Refresh(string.Empty);
        await AddAsync("B", "b");

        _vm.Refresh(string.Empty);

        Names().ShouldBe(["A", "B"]);
    }

    [Fact]
    public async Task Changes_to_the_catalog_are_passed_on()
    {
        var raised = 0;
        _vm.Changed += (_, _) => raised++;

        await AddAsync("A", "a");

        raised.ShouldBe(1);
    }

    // --- pasting without a prompt ---

    [Fact]
    public async Task A_snippet_that_asks_for_nothing_is_ready_at_once_with_the_date_the_time_and_the_clipboard()
    {
        var snippet = await AddAsync("Note", "{date} {time}: {clipboard}");

        var start = _vm.Begin(snippet.Id, "copied");

        start.Kind.ShouldBe(SnippetStartKind.Ready);
        start.Text.ShouldBe("2026-10-24 00:30: copied");
        _vm.IsPrompting.ShouldBeFalse();
    }

    [Fact]
    public async Task With_no_clipboard_text_the_variable_is_empty()
    {
        var snippet = await AddAsync("Note", "[{clipboard}]");

        _vm.Begin(snippet.Id, null).Text.ShouldBe("[]");
    }

    [Fact]
    public async Task A_snippet_with_warnings_is_still_pasted()
    {
        var snippet = await AddAsync("Odd", "{nope} {date");

        var start = _vm.Begin(snippet.Id, null);

        start.Kind.ShouldBe(SnippetStartKind.Ready);
        start.Text.ShouldBe("{nope} {date");
    }

    [Fact]
    public void A_snippet_that_is_gone_cannot_be_pasted()
    {
        var start = _vm.Begin(SnippetId.New(), null);

        start.Kind.ShouldBe(SnippetStartKind.Failed);
        start.Error.ShouldBe("That snippet no longer exists.");
    }

    // --- the prompt ---

    [Fact]
    public async Task A_snippet_that_asks_for_values_opens_the_prompt_with_one_field_for_each()
    {
        var snippet = await AddAsync("Reply", "Dear {input:Name}, about {input:Topic}. {input:Name}");

        var start = _vm.Begin(snippet.Id, null);

        start.Kind.ShouldBe(SnippetStartKind.NeedsInput);
        _vm.IsPrompting.ShouldBeTrue();
        _vm.PromptTitle.ShouldBe("Reply");
        _vm.Fields.Select(field => field.Name).ShouldBe(["Name", "Topic"]);
        _vm.Fields.ShouldAllBe(field => field.Value.Length == 0);
    }

    [Fact]
    public async Task The_typed_values_make_the_text_and_close_the_prompt()
    {
        var snippet = await AddAsync("Reply", "Dear {input:Name}, about {input:Topic}. {input:Name} on {date}");
        _vm.Begin(snippet.Id, null);
        _vm.Fields[0].Value = "Sam";
        _vm.Fields[1].Value = "lunch";

        _vm.TrySubmit(out var text).ShouldBeTrue();

        text.ShouldBe("Dear Sam, about lunch. Sam on 2026-10-24");
        _vm.IsPrompting.ShouldBeFalse();
        _vm.Fields.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_value_may_be_left_empty()
    {
        var snippet = await AddAsync("Reply", "[{input:Name}]");
        _vm.Begin(snippet.Id, null);

        _vm.TrySubmit(out var text).ShouldBeTrue();

        text.ShouldBe("[]");
    }

    [Fact]
    public async Task What_is_typed_is_not_read_as_a_variable()
    {
        var snippet = await AddAsync("Reply", "{input:X}");
        _vm.Begin(snippet.Id, "clip");
        _vm.Fields[0].Value = "{date} {clipboard}";

        _vm.TrySubmit(out var text).ShouldBeTrue();

        text.ShouldBe("{date} {clipboard}");
    }

    [Fact]
    public async Task The_clipboard_is_the_one_given_when_the_paste_began()
    {
        var snippet = await AddAsync("Reply", "{input:X} {clipboard}");
        _vm.Begin(snippet.Id, "from the start");
        _vm.Fields[0].Value = "x";

        _vm.TrySubmit(out var text).ShouldBeTrue();

        text.ShouldBe("x from the start");
    }

    [Fact]
    public async Task A_value_that_is_too_long_keeps_the_prompt_open_with_the_reason_and_without_repeating_it()
    {
        var snippet = await AddAsync("Reply", "{input:X}");
        _vm.Begin(snippet.Id, null);
        _vm.Fields[0].Value = new string('a', SnippetEngine.MaxInputLength + 1);

        _vm.TrySubmit(out var text).ShouldBeFalse();

        text.ShouldBeEmpty();
        _vm.IsPrompting.ShouldBeTrue();
        _vm.PromptError.ShouldNotBeNullOrWhiteSpace();
        _vm.PromptError.ShouldNotContain("aaaa");
    }

    [Fact]
    public async Task Fixing_the_value_after_a_refusal_works()
    {
        var snippet = await AddAsync("Reply", "{input:X}");
        _vm.Begin(snippet.Id, null);
        _vm.Fields[0].Value = new string('a', SnippetEngine.MaxInputLength + 1);
        _vm.TrySubmit(out _).ShouldBeFalse();

        _vm.Fields[0].Value = "short";

        _vm.TrySubmit(out var text).ShouldBeTrue();
        text.ShouldBe("short");
        _vm.PromptError.ShouldBeNull();
    }

    [Fact]
    public async Task Cancelling_closes_the_prompt_and_forgets_what_was_typed()
    {
        var snippet = await AddAsync("Reply", "{input:X}");
        _vm.Begin(snippet.Id, null);
        _vm.Fields[0].Value = "secret-marker";

        _vm.CancelPrompt();

        _vm.IsPrompting.ShouldBeFalse();
        _vm.Fields.ShouldBeEmpty();
        _vm.PromptTitle.ShouldBeEmpty();
        _vm.PromptError.ShouldBeNull();
    }

    [Fact]
    public void Submitting_when_no_prompt_is_open_does_nothing()
    {
        _vm.TrySubmit(out var text).ShouldBeFalse();

        text.ShouldBeEmpty();
    }

    [Fact]
    public async Task Starting_another_snippet_replaces_a_prompt_that_was_open()
    {
        var first = await AddAsync("First", "{input:A}");
        var second = await AddAsync("Second", "{input:B}");
        _vm.Begin(first.Id, null);

        _vm.Begin(second.Id, null);

        _vm.PromptTitle.ShouldBe("Second");
        _vm.Fields.Select(field => field.Name).ShouldBe(["B"]);
    }

    // --- printing and bugs ---

    [Fact]
    public async Task Printing_never_shows_a_name_or_a_text_or_what_was_typed()
    {
        var snippet = await AddAsync("secret-name", "secret-body {input:X}");
        _vm.Refresh(string.Empty);
        var start = _vm.Begin(snippet.Id, null);
        _vm.Fields[0].Value = "secret-typed";

        var printed = string.Join("\n", _vm.Rows[0].ToString(), _vm.Fields[0].ToString(), start.ToString());

        printed.ShouldNotContain("secret");
    }

    [Fact]
    public void Missing_arguments_are_bugs()
    {
        Should.Throw<ArgumentNullException>(() => new SnippetsViewModel(null!, new SnippetEngine(new SnippetTestClock())));
        Should.Throw<ArgumentNullException>(() => new SnippetsViewModel(_catalog, null!));
        Should.Throw<ArgumentNullException>(() => _vm.Refresh(null!));
        Should.Throw<ArgumentNullException>(() => new SnippetRowViewModel(SnippetId.New(), null!, "x", 0));
        Should.Throw<ArgumentNullException>(() => new SnippetFieldViewModel(null!));
    }

    [Fact]
    public async Task After_disposing_changes_are_no_longer_passed_on()
    {
        var raised = 0;
        _vm.Changed += (_, _) => raised++;
        _vm.Dispose();

        await AddAsync("A", "a");

        raised.ShouldBe(0);
    }
}
