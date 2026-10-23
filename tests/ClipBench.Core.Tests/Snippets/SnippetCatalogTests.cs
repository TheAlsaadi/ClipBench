using ClipBench.Core.Results;
using ClipBench.Core.Snippets;
using ClipBench.Core.Tests.Support;

namespace ClipBench.Core.Tests.Snippets;

public sealed class SnippetCatalogTests : IDisposable
{
    private static readonly ResultError _storeError = new(ErrorCode.StorageFailure, "the disk said no");

    private readonly FakeSnippetStore _store = new();
    private readonly SnippetCatalog _catalog;

    public SnippetCatalogTests()
    {
        _catalog = new SnippetCatalog(_store);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _catalog.Dispose();

    private async Task<Snippet> CreateAsync(string name, string body = "text")
    {
        var created = await _catalog.CreateAsync(name, body, Ct);
        created.IsSuccess.ShouldBeTrue(created.IsFailure ? created.Error.Message : null);
        return created.Value;
    }

    private string[] Names() => [.. _catalog.Snippets.Select(snippet => snippet.Name)];

    // --- names ---

    [Theory]
    [InlineData("Signature", "Signature")]
    [InlineData("  Email reply  ", "Email reply")]
    [InlineData("\u0645\u0631\u062D\u0628\u0627", "\u0645\u0631\u062D\u0628\u0627")]
    [InlineData("Plans \U0001F680", "Plans \U0001F680")]
    public void A_name_is_trimmed_and_kept_as_typed(string typed, string expected)
    {
        SnippetName.Validate(typed).Value.ShouldBe(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("two\nlines")]
    [InlineData("tab\tinside")]
    public void An_empty_name_or_one_with_control_characters_is_refused(string typed)
    {
        var result = SnippetName.Validate(typed);

        result.Error.Code.ShouldBe(ErrorCode.InvalidSnippetName);
        result.Error.Message.ShouldContain("snippet");
    }

    [Fact]
    public void A_name_can_be_as_long_as_the_limit_but_not_longer()
    {
        SnippetName.Validate(new string('a', SnippetName.MaxLength)).IsSuccess.ShouldBeTrue();
        SnippetName.Validate(new string('a', SnippetName.MaxLength + 1)).Error.Code.ShouldBe(ErrorCode.InvalidSnippetName);
    }

    [Fact]
    public void A_name_with_half_of_a_surrogate_pair_is_refused()
    {
        SnippetName.Validate("a" + (char)0xD800).Error.Code.ShouldBe(ErrorCode.InvalidSnippetName);
    }

    [Fact]
    public void Printing_a_snippet_never_shows_its_name_or_text()
    {
        var snippet = new Snippet(SnippetId.New(), "secret-name", "secret-body", 0);

        snippet.ToString().ShouldNotContain("secret");
        snippet.ToString().ShouldContain(snippet.Id.Value.ToString("N"));
    }

    // --- creating ---

    [Fact]
    public async Task A_new_catalog_has_no_snippets()
    {
        _catalog.Snippets.ShouldBeEmpty();
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Creating_a_snippet_trims_the_name_keeps_the_text_stores_it_and_puts_it_at_the_end()
    {
        var first = await CreateAsync("  Signature ", "Regards,\r\n{input:Name}");
        var second = await CreateAsync("Date");

        first.Name.ShouldBe("Signature");
        first.Body.ShouldBe("Regards,\r\n{input:Name}");
        first.Position.ShouldBe(0);
        second.Position.ShouldBe(1);
        Names().ShouldBe(["Signature", "Date"]);
        _store.Saved.ShouldBe([first, second]);
        _catalog.Find(first.Id).ShouldBe(first);
    }

    [Fact]
    public async Task Creating_a_snippet_raises_changed_once()
    {
        var raised = 0;
        _catalog.Changed += (_, _) => raised++;

        await CreateAsync("Signature");

        raised.ShouldBe(1);
    }

    [Theory]
    [InlineData("")]
    [InlineData("two\nlines")]
    public async Task A_name_that_is_not_allowed_is_refused_and_nothing_is_stored(string name)
    {
        var created = await _catalog.CreateAsync(name, "text", Ct);

        created.Error.Code.ShouldBe(ErrorCode.InvalidSnippetName);
        _catalog.Snippets.ShouldBeEmpty();
        _store.Saved.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("signature")]
    [InlineData("SIGNATURE")]
    [InlineData("  Signature ")]
    public async Task A_name_that_is_taken_is_refused_ignoring_capital_letters(string name)
    {
        await CreateAsync("Signature");

        (await _catalog.CreateAsync(name, "text", Ct)).Error.Code.ShouldBe(ErrorCode.SnippetNameTaken);
        _catalog.Snippets.Length.ShouldBe(1);
    }

    [Fact]
    public async Task A_text_that_is_too_long_or_asks_for_too_much_is_refused_and_nothing_is_stored()
    {
        var tooLong = await _catalog.CreateAsync("Long", new string('a', SnippetEngine.MaxBodyLength + 1), Ct);
        var tooMany = await _catalog.CreateAsync("Many", string.Concat(Enumerable.Range(0, SnippetEngine.MaxInputs + 1).Select(i => "{input:v" + i + "}")), Ct);

        tooLong.Error.Code.ShouldBe(ErrorCode.InvalidSnippet);
        tooMany.Error.Code.ShouldBe(ErrorCode.InvalidSnippet);
        _store.Saved.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_text_with_warnings_is_still_saved()
    {
        await CreateAsync("Odd", "{nope} {date");

        _catalog.Snippets.Single().Body.ShouldBe("{nope} {date");
    }

    [Fact]
    public async Task An_empty_text_is_allowed()
    {
        await CreateAsync("Empty", string.Empty);

        _catalog.Snippets.Single().Body.ShouldBeEmpty();
    }

    [Fact]
    public async Task There_can_be_only_so_many_snippets()
    {
        for (var i = 0; i < SnippetName.MaxSnippets; i++)
        {
            await CreateAsync("Snippet " + i);
        }

        (await _catalog.CreateAsync("One too many", "x", Ct)).Error.Code.ShouldBe(ErrorCode.TooManySnippets);
    }

    [Fact]
    public async Task When_the_store_fails_the_snippet_still_shows_and_the_failure_is_returned()
    {
        _store.Failure = _storeError;

        var created = await _catalog.CreateAsync("Signature", "x", Ct);

        created.Error.ShouldBe(_storeError);
        Names().ShouldBe(["Signature"]);
    }

    // --- changing ---

    [Fact]
    public async Task Updating_changes_the_name_and_the_text_and_keeps_the_place()
    {
        await CreateAsync("First");
        var second = await CreateAsync("Second");

        (await _catalog.UpdateAsync(second.Id, "  Renamed ", "new {date}", Ct)).IsSuccess.ShouldBeTrue();

        var found = _catalog.Find(second.Id)!;
        found.Name.ShouldBe("Renamed");
        found.Body.ShouldBe("new {date}");
        found.Position.ShouldBe(1);
        Names().ShouldBe(["First", "Renamed"]);
        _store.Saved[^1].ShouldBe(found);
    }

    [Fact]
    public async Task A_snippet_can_be_renamed_to_the_same_name_in_other_letters()
    {
        var snippet = await CreateAsync("signature");

        (await _catalog.UpdateAsync(snippet.Id, "Signature", "x", Ct)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task A_snippet_cannot_take_the_name_of_another()
    {
        await CreateAsync("First");
        var second = await CreateAsync("Second");

        (await _catalog.UpdateAsync(second.Id, "FIRST", "x", Ct)).Error.Code.ShouldBe(ErrorCode.SnippetNameTaken);
    }

    [Fact]
    public async Task Updating_with_a_name_or_text_that_is_not_allowed_or_an_unknown_snippet_fails_and_stores_nothing()
    {
        var snippet = await CreateAsync("First");
        var saved = _store.Saved.Count;

        (await _catalog.UpdateAsync(snippet.Id, " ", "x", Ct)).Error.Code.ShouldBe(ErrorCode.InvalidSnippetName);
        (await _catalog.UpdateAsync(snippet.Id, "First", new string('a', SnippetEngine.MaxBodyLength + 1), Ct)).Error.Code.ShouldBe(ErrorCode.InvalidSnippet);
        (await _catalog.UpdateAsync(SnippetId.New(), "Other", "x", Ct)).Error.Code.ShouldBe(ErrorCode.SnippetNotFound);
        _store.Saved.Count.ShouldBe(saved);
    }

    [Fact]
    public async Task Updating_raises_changed()
    {
        var snippet = await CreateAsync("First");
        var raised = 0;
        _catalog.Changed += (_, _) => raised++;

        await _catalog.UpdateAsync(snippet.Id, "First", "changed", Ct);

        raised.ShouldBe(1);
    }

    // --- deleting ---

    [Fact]
    public async Task Deleting_removes_the_snippet_and_stores_that()
    {
        var first = await CreateAsync("First");
        await CreateAsync("Second");

        (await _catalog.DeleteAsync(first.Id, Ct)).IsSuccess.ShouldBeTrue();

        Names().ShouldBe(["Second"]);
        _catalog.Find(first.Id).ShouldBeNull();
        _store.Deleted.ShouldBe([first.Id]);
    }

    [Fact]
    public async Task Deleting_an_unknown_snippet_fails()
    {
        (await _catalog.DeleteAsync(SnippetId.New(), Ct)).Error.Code.ShouldBe(ErrorCode.SnippetNotFound);
        _store.Deleted.ShouldBeEmpty();
    }

    [Fact]
    public async Task When_the_store_fails_to_delete_the_snippet_is_gone_from_the_list_and_the_failure_is_returned()
    {
        var snippet = await CreateAsync("First");
        _store.Failure = _storeError;

        (await _catalog.DeleteAsync(snippet.Id, Ct)).Error.ShouldBe(_storeError);

        _catalog.Snippets.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_snippet_made_after_one_was_deleted_still_goes_at_the_end()
    {
        var a = await CreateAsync("A");
        await CreateAsync("B");
        await _catalog.DeleteAsync(a.Id, Ct);

        (await CreateAsync("C")).Position.ShouldBe(2);
    }

    // --- loading ---

    [Fact]
    public async Task Loading_reads_the_snippets_in_the_order_of_their_places()
    {
        _store.Stored = [new Snippet(SnippetId.New(), "Late", "x", 5), new Snippet(SnippetId.New(), "Early", "y", 2)];

        (await _catalog.LoadAsync(Ct)).Value.ShouldBe(2);

        Names().ShouldBe(["Early", "Late"]);
    }

    [Fact]
    public async Task A_snippet_made_after_loading_goes_after_the_last_one_loaded()
    {
        _store.Stored = [new Snippet(SnippetId.New(), "Early", "y", 7)];
        await _catalog.LoadAsync(Ct);

        (await CreateAsync("New")).Position.ShouldBe(8);
    }

    [Fact]
    public async Task When_the_snippets_cannot_be_read_loading_fails_and_changes_nothing()
    {
        _store.Failure = _storeError;

        (await _catalog.LoadAsync(Ct)).Error.ShouldBe(_storeError);

        _catalog.Snippets.ShouldBeEmpty();
    }

    [Fact]
    public async Task Loading_raises_changed_once()
    {
        _store.Stored = [new Snippet(SnippetId.New(), "A", "x", 0)];
        var raised = 0;
        _catalog.Changed += (_, _) => raised++;

        await _catalog.LoadAsync(Ct);

        raised.ShouldBe(1);
    }

    // --- bugs ---

    [Fact]
    public void Missing_arguments_are_bugs()
    {
        Should.Throw<ArgumentNullException>(() => new SnippetCatalog(null!));
        Should.Throw<ArgumentNullException>(() => _catalog.CreateAsync(null!, "x", Ct));
        Should.Throw<ArgumentNullException>(() => _catalog.CreateAsync("x", null!, Ct));
        Should.Throw<ArgumentNullException>(() => _catalog.UpdateAsync(SnippetId.New(), null!, "x", Ct));
        Should.Throw<ArgumentNullException>(() => _catalog.UpdateAsync(SnippetId.New(), "x", null!, Ct));
        Should.Throw<ArgumentNullException>(() => SnippetName.Validate(null!));
        Should.Throw<ArgumentNullException>(() => new Snippet(SnippetId.New(), null!, "x", 0));
        Should.Throw<ArgumentNullException>(() => new Snippet(SnippetId.New(), "x", null!, 0));
    }
}
