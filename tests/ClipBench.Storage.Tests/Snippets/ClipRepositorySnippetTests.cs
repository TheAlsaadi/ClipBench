using System.Text;
using ClipBench.Core.Boards;
using ClipBench.Core.Results;
using ClipBench.Core.Snippets;
using ClipBench.Storage.Snippets;
using ClipBench.Storage.Tests.Support;

namespace ClipBench.Storage.Tests.Snippets;

/// <summary>Snippet names and texts are sealed like clip content, and bound to the snippet and the column they were written in.</summary>
public sealed class ClipRepositorySnippetTests : RepositoryTestBase
{
    private static Snippet Make(string name, string body, int position) => new(SnippetId.New(), name, body, position);

    // --- storing ---

    [Fact]
    public async Task Snippets_are_saved_and_read_back_in_the_order_of_their_places_with_text_in_any_script()
    {
        await using var repository = await OpenAsync();
        var third = Make("\u0645\u0631\u062D\u0628\u0627", "\u0634\u0643\u0631\u0627 {input:\u0627\u0644\u0627\u0633\u0645}", 9);
        var first = Make("Signature", "Regards,\r\n{input:Name}\r\n{date}", 1);
        var second = Make("Plans \U0001F680", "caf\u00E9 e\u0301 \U0001F600", 5);
        foreach (var snippet in new[] { third, first, second })
        {
            (await repository.SaveSnippetAsync(snippet, Ct)).IsSuccess.ShouldBeTrue();
        }

        var loaded = (await repository.LoadSnippetsAsync(Ct)).Value;

        loaded.UnreadableCount.ShouldBe(0);
        loaded.Items.ShouldBe([first, second, third]);
    }

    [Fact]
    public async Task A_snippet_of_the_longest_size_is_stored()
    {
        await using var repository = await OpenAsync();
        var snippet = Make("Long", new string('x', SnippetEngine.MaxBodyLength), 0);

        (await repository.SaveSnippetAsync(snippet, Ct)).IsSuccess.ShouldBeTrue();

        (await repository.LoadSnippetsAsync(Ct)).Value.Items.ShouldBe([snippet]);
    }

    [Fact]
    public async Task An_empty_text_is_stored()
    {
        await using var repository = await OpenAsync();
        var snippet = Make("Empty", string.Empty, 0);
        await repository.SaveSnippetAsync(snippet, Ct);

        (await repository.LoadSnippetsAsync(Ct)).Value.Items.ShouldBe([snippet]);
    }

    [Fact]
    public async Task Snippets_come_back_after_the_database_is_reopened()
    {
        var snippet = Make("Signature", "Regards", 0);
        await using (var first = await OpenAsync())
        {
            await first.SaveSnippetAsync(snippet, Ct);
        }

        await using var second = await OpenAsync();

        (await second.LoadSnippetsAsync(Ct)).Value.Items.ShouldBe([snippet]);
    }

    [Fact]
    public async Task A_database_with_no_snippets_loads_none()
    {
        await using var repository = await OpenAsync();

        var loaded = (await repository.LoadSnippetsAsync(Ct)).Value;

        loaded.Items.ShouldBeEmpty();
        loaded.UnreadableCount.ShouldBe(0);
    }

    [Fact]
    public async Task Saving_a_snippet_again_replaces_it()
    {
        await using var repository = await OpenAsync();
        var snippet = Make("Signature", "old text", 3);
        await repository.SaveSnippetAsync(snippet, Ct);

        await repository.SaveSnippetAsync(snippet with { Name = "Renamed", Body = "new text" }, Ct);

        (await repository.LoadSnippetsAsync(Ct)).Value.Items.ShouldBe([snippet with { Name = "Renamed", Body = "new text" }]);
    }

    [Fact]
    public async Task Deleting_a_snippet_removes_only_that_one()
    {
        await using var repository = await OpenAsync();
        var a = Make("A", "a", 0);
        var b = Make("B", "b", 1);
        await repository.SaveSnippetAsync(a, Ct);
        await repository.SaveSnippetAsync(b, Ct);

        (await repository.DeleteSnippetAsync(a.Id, Ct)).IsSuccess.ShouldBeTrue();

        (await repository.LoadSnippetsAsync(Ct)).Value.Items.ShouldBe([b]);
    }

    [Fact]
    public async Task Deleting_a_snippet_that_is_not_stored_says_so()
    {
        await using var repository = await OpenAsync();

        (await repository.DeleteSnippetAsync(SnippetId.New(), Ct)).Error.Code.ShouldBe(ErrorCode.SnippetNotFound);
    }

    // --- sealed ---

    [Fact]
    public async Task A_name_and_a_text_are_not_stored_as_text()
    {
        const string Name = "SNIPPETNAME-71ab33";
        const string Body = "SNIPPETBODY-5c0de2";
        await using var repository = await OpenAsync();
        await repository.SaveSnippetAsync(Make(Name, Body, 0), Ct);

        var (name, body) = RawSnippetColumns();

        name.Length.ShouldBe(12 + Encoding.UTF8.GetByteCount(Name) + 16);
        body.Length.ShouldBe(12 + Encoding.UTF8.GetByteCount(Body) + 16);
        name.AsSpan().IndexOf(Encoding.UTF8.GetBytes(Name)).ShouldBe(-1);
        body.AsSpan().IndexOf(Encoding.UTF8.GetBytes(Body)).ShouldBe(-1);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("body")]
    public async Task Changing_a_byte_makes_that_snippet_unreadable_and_no_other(string column)
    {
        await using var repository = await OpenAsync();
        var a = Make("A", "text a", 0);
        var b = Make("B", "text b", 1);
        var c = Make("C", "text c", 2);
        foreach (var snippet in new[] { a, b, c })
        {
            await repository.SaveSnippetAsync(snippet, Ct);
        }

        var sealedValue = RawColumn(column, b.Id);
        sealedValue[^1] ^= 0x01;
        RawSetColumn(column, b.Id, sealedValue);

        var loaded = (await repository.LoadSnippetsAsync(Ct)).Value;

        loaded.UnreadableCount.ShouldBe(1);
        loaded.Items.ShouldBe([a, c]);
    }

    [Fact]
    public async Task A_text_copied_from_one_snippet_to_another_does_not_open()
    {
        await using var repository = await OpenAsync();
        var a = Make("A", "text a", 0);
        var b = Make("B", "text b", 1);
        await repository.SaveSnippetAsync(a, Ct);
        await repository.SaveSnippetAsync(b, Ct);

        RawSetColumn("body", b.Id, RawColumn("body", a.Id));

        var loaded = (await repository.LoadSnippetsAsync(Ct)).Value;
        loaded.Items.ShouldBe([a]);
        loaded.UnreadableCount.ShouldBe(1);
    }

    [Fact]
    public async Task A_name_and_a_text_swapped_inside_one_snippet_do_not_open()
    {
        await using var repository = await OpenAsync();
        var a = Make("A", "text a", 0);
        await repository.SaveSnippetAsync(a, Ct);
        var name = RawColumn("name", a.Id);
        var body = RawColumn("body", a.Id);
        RawSetColumn("name", a.Id, body);
        RawSetColumn("body", a.Id, name);

        var loaded = (await repository.LoadSnippetsAsync(Ct)).Value;

        loaded.Items.ShouldBeEmpty();
        loaded.UnreadableCount.ShouldBe(1);
    }

    [Fact]
    public async Task A_board_name_does_not_open_as_a_snippet_name_even_with_the_same_id()
    {
        await using var repository = await OpenAsync();
        var snippet = Make("Snippet", "text", 0);
        await repository.SaveSnippetAsync(snippet, Ct);
        var board = new Board(new BoardId(snippet.Id.Value), "Board", 0);
        await repository.SaveBoardAsync(board, Ct);
        using (var connection = Raw())
        {
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE snippets SET name = (SELECT name FROM boards);";
            command.ExecuteNonQuery();
        }

        var loaded = (await repository.LoadSnippetsAsync(Ct)).Value;

        loaded.Items.ShouldBeEmpty();
        loaded.UnreadableCount.ShouldBe(1);
    }

    // --- wiping ---

    [Fact]
    public async Task Wiping_removes_the_snippets_too()
    {
        await using var repository = await OpenAsync();
        await repository.SaveSnippetAsync(Make("A", "text", 0), Ct);
        RawCount("SELECT COUNT(*) FROM snippets").ShouldBe(1);

        (await repository.WipeAsync(Ct)).IsSuccess.ShouldBeTrue();

        RawCount("SELECT COUNT(*) FROM snippets").ShouldBe(0);
        (await repository.LoadSnippetsAsync(Ct)).Value.Items.ShouldBeEmpty();
    }

    // --- the store the app uses ---

    [Fact]
    public async Task A_snippet_goes_in_comes_out_and_is_deleted_through_the_store()
    {
        var repository = await OpenAsync();
        var store = new SnippetStore(repository);
        var snippet = Make("Signature", "Regards", 0);

        (await store.SaveAsync(snippet, Ct)).IsSuccess.ShouldBeTrue();
        (await store.LoadAsync(Ct)).Value.ShouldBe([snippet]);

        (await store.SaveAsync(snippet with { Body = "Best" }, Ct)).IsSuccess.ShouldBeTrue();
        (await store.LoadAsync(Ct)).Value.ShouldBe([snippet with { Body = "Best" }]);

        (await store.DeleteAsync(snippet.Id, Ct)).IsSuccess.ShouldBeTrue();
        (await store.LoadAsync(Ct)).Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task Snippets_that_cannot_be_read_are_left_out_of_the_store_list()
    {
        var repository = await OpenAsync();
        var store = new SnippetStore(repository);
        var a = Make("A", "a", 0);
        var b = Make("B", "b", 1);
        await store.SaveAsync(a, Ct);
        await store.SaveAsync(b, Ct);
        RawSetColumn("body", b.Id, new byte[] { 0 });

        (await store.LoadAsync(Ct)).Value.ShouldBe([a]);
    }

    [Fact]
    public void A_missing_repository_or_snippet_is_a_bug()
    {
        Should.Throw<ArgumentNullException>(() => new SnippetStore(null!));
    }

    private (byte[] Name, byte[] Body) RawSnippetColumns()
    {
        using var connection = Raw();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name, body FROM snippets;";
        using var reader = command.ExecuteReader();
        reader.Read().ShouldBeTrue();
        return (reader.GetFieldValue<byte[]>(0), reader.GetFieldValue<byte[]>(1));
    }

    private byte[] RawColumn(string column, SnippetId id)
    {
        using var connection = Raw();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {column} FROM snippets WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id.Value.ToByteArray(bigEndian: true));
        return (byte[])command.ExecuteScalar()!;
    }

    private void RawSetColumn(string column, SnippetId id, byte[] value)
    {
        using var connection = Raw();
        using var command = connection.CreateCommand();
        command.CommandText = $"UPDATE snippets SET {column} = $value WHERE id = $id;";
        command.Parameters.AddWithValue("$value", value);
        command.Parameters.AddWithValue("$id", id.Value.ToByteArray(bigEndian: true));
        command.ExecuteNonQuery().ShouldBe(1);
    }
}
