using ClipBench.Core.Results;
using ClipBench.Storage.Sqlite;
using ClipBench.Storage.Tests.Support;
using Microsoft.Data.Sqlite;

namespace ClipBench.Storage.Tests.Sqlite;

public sealed class SchemaMigratorTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly FakeTimeProvider _clock = new();

    public SchemaMigratorTests() => _connection.Open();

    public void Dispose() => _connection.Dispose();

    private static readonly Migration _createNotes = new(1, "CREATE TABLE notes (id INTEGER PRIMARY KEY);");
    private static readonly Migration _addColumn = new(2, "ALTER TABLE notes ADD COLUMN title TEXT;");

    private long Scalar(string sql)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private void Execute(string sql)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    // 0 for a database that has no version table yet.
    private long ReadVersion() =>
        TableCount("schema_version") == 0 ? 0 : Scalar("SELECT COALESCE(MAX(version), 0) FROM schema_version");

    private long TableCount(string name) =>
        Scalar($"SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = '{name}'");

    // --- applying steps ---

    [Fact]
    public void An_empty_database_gets_every_step_in_order()
    {
        var result = SchemaMigrator.Apply(_connection, [_createNotes, _addColumn], _clock);

        result.IsSuccess.ShouldBeTrue();
        ReadVersion().ShouldBe(2);
        Scalar("SELECT COUNT(*) FROM pragma_table_info('notes') WHERE name = 'title'").ShouldBe(1);
    }

    [Fact]
    public void Applying_again_changes_nothing()
    {
        SchemaMigrator.Apply(_connection, [_createNotes, _addColumn], _clock).IsSuccess.ShouldBeTrue();

        // Running CREATE TABLE a second time would fail, so success proves the steps were skipped.
        SchemaMigrator.Apply(_connection, [_createNotes, _addColumn], _clock).IsSuccess.ShouldBeTrue();

        Scalar("SELECT COUNT(*) FROM schema_version").ShouldBe(2);
    }

    [Fact]
    public void Only_the_missing_steps_run_on_an_older_database()
    {
        SchemaMigrator.Apply(_connection, [_createNotes], _clock).IsSuccess.ShouldBeTrue();
        Execute("INSERT INTO notes (id) VALUES (7);");

        var result = SchemaMigrator.Apply(_connection, [_createNotes, _addColumn], _clock);

        result.IsSuccess.ShouldBeTrue();
        ReadVersion().ShouldBe(2);
        Scalar("SELECT COUNT(*) FROM notes").ShouldBe(1);
    }

    [Fact]
    public void The_time_each_step_was_applied_comes_from_the_clock()
    {
        SchemaMigrator.Apply(_connection, [_createNotes], _clock).IsSuccess.ShouldBeTrue();

        Scalar("SELECT applied_at FROM schema_version WHERE version = 1").ShouldBe(FakeTimeProvider.Start.UtcTicks);
    }

    // --- failures change nothing ---

    [Fact]
    public void A_failing_step_leaves_the_database_exactly_as_it_was()
    {
        Migration broken = new(2, "ALTER TABLE does_not_exist ADD COLUMN x TEXT;");

        var result = SchemaMigrator.Apply(_connection, [_createNotes, broken], _clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ErrorCode.StorageFailure);
        ReadVersion().ShouldBe(0);
        TableCount("notes").ShouldBe(0, "the step that did succeed must be rolled back with the one that failed");
        TableCount("schema_version").ShouldBe(0);
    }

    [Fact]
    public void A_failure_message_does_not_repeat_the_statement()
    {
        Migration broken = new(1, "CREATE TABLE secret_marker_table (x");

        var result = SchemaMigrator.Apply(_connection, [broken], _clock);

        result.Error.Message.ShouldNotContain("secret_marker_table");
    }

    [Fact]
    public void A_database_from_a_newer_version_is_refused_and_not_touched()
    {
        SchemaMigrator.Apply(_connection, [_createNotes, _addColumn], _clock).IsSuccess.ShouldBeTrue();
        Execute("INSERT INTO schema_version (version, applied_at) VALUES (5, 0);");

        var result = SchemaMigrator.Apply(_connection, [_createNotes, _addColumn], _clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ErrorCode.HistoryTooNew);
        ReadVersion().ShouldBe(5);
    }

    // --- the list of steps itself ---

    [Fact]
    public void A_gap_in_the_steps_is_a_bug()
    {
        Should.Throw<ArgumentException>(() => SchemaMigrator.Apply(_connection, [_createNotes, new Migration(3, "SELECT 1;")], _clock));
    }

    [Fact]
    public void Steps_that_do_not_start_at_one_are_a_bug()
    {
        Should.Throw<ArgumentException>(() => SchemaMigrator.Apply(_connection, [_addColumn], _clock));
    }

    [Fact]
    public void An_empty_list_of_steps_is_a_bug()
    {
        Should.Throw<ArgumentException>(() => SchemaMigrator.Apply(_connection, [], _clock));
    }

    [Fact]
    public void The_real_steps_are_numbered_from_one_without_gaps()
    {
        Migrations.All.Select(migration => migration.Version).ShouldBe(Enumerable.Range(1, Migrations.All.Count));
        Migrations.Latest.ShouldBe(Migrations.All.Count);
    }

    // --- the real schema ---

    [Fact]
    public void The_real_schema_creates_the_tables_and_indexes()
    {
        SchemaMigrator.Apply(_connection, Migrations.All, _clock).IsSuccess.ShouldBeTrue();

        TableCount("clips").ShouldBe(1);
        TableCount("meta").ShouldBe(1);
        TableCount("schema_version").ShouldBe(1);
        Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = 'clips_captured_at'").ShouldBe(1);
        Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = 'clips_expires_at'").ShouldBe(1);
    }

    [Fact]
    public void The_real_schema_has_exactly_the_documented_columns_and_nothing_readable_in_plain()
    {
        SchemaMigrator.Apply(_connection, Migrations.All, _clock).IsSuccess.ShouldBeTrue();
        var columns = new List<string>();
        using (var command = _connection.CreateCommand())
        {
            command.CommandText = "SELECT name FROM pragma_table_info('clips') ORDER BY cid;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                columns.Add(reader.GetString(0));
            }
        }

        // Step 2 added the board and the place on it: plain columns, like the pin flag, because ordering needs them.
        columns.ShouldBe(["id", "format", "captured_at", "expires_at", "is_pinned", "size_bytes", "content_hash", "content", "preview", "source_app", "board_id", "board_position"]);
    }

    [Theory]
    [InlineData("randomblob(15), 0, 1, NULL, 0, 1, randomblob(32), x'01', x'01', NULL", "an id that is not 16 bytes")]
    [InlineData("randomblob(16), 9, 1, NULL, 0, 1, randomblob(32), x'01', x'01', NULL", "a format that does not exist")]
    [InlineData("randomblob(16), 0, 1, NULL, 2, 1, randomblob(32), x'01', x'01', NULL", "a pin flag that is not 0 or 1")]
    [InlineData("randomblob(16), 0, 1, NULL, 0, -1, randomblob(32), x'01', x'01', NULL", "a negative size")]
    [InlineData("randomblob(16), 0, 1, NULL, 0, 1, randomblob(31), x'01', x'01', NULL", "a hash that is not 32 bytes")]
    [InlineData("randomblob(16), 0, 1, NULL, 0, 1, randomblob(32), 'text', x'01', NULL", "content stored as text")]
    [InlineData("randomblob(16), 0, 1, NULL, 0, 1, randomblob(32), NULL, x'01', NULL", "no content")]
    public void The_real_schema_rejects_malformed_rows(string values, string reason)
    {
        SchemaMigrator.Apply(_connection, Migrations.All, _clock).IsSuccess.ShouldBeTrue();

        Should.Throw<SqliteException>(
            () => Execute("INSERT INTO clips (id, format, captured_at, expires_at, is_pinned, size_bytes, content_hash, content, preview, source_app) VALUES (" + values + ");"),
            reason);
    }

    [Fact]
    public void The_real_schema_refuses_two_clips_with_the_same_hash()
    {
        SchemaMigrator.Apply(_connection, Migrations.All, _clock).IsSuccess.ShouldBeTrue();
        const string row = "INSERT INTO clips (id, format, captured_at, is_pinned, size_bytes, content_hash, content, preview) VALUES (randomblob(16), 0, 1, 0, 1, x'0000000000000000000000000000000000000000000000000000000000000001', x'01', x'01');";
        Execute(row);

        Should.Throw<SqliteException>(() => Execute(row));
    }
}
