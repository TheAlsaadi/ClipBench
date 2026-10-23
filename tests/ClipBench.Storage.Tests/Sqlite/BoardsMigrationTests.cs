using ClipBench.Storage.Sqlite;
using ClipBench.Storage.Tests.Support;
using Microsoft.Data.Sqlite;

namespace ClipBench.Storage.Tests.Sqlite;

/// <summary>Step 2 of the schema adds boards and snippets. A history saved before it must come through whole.</summary>
public sealed class BoardsMigrationTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly FakeTimeProvider _clock = new();

    public BoardsMigrationTests() => _connection.Open();

    public void Dispose() => _connection.Dispose();

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

    private void InsertOldClip(byte idByte, bool pinned)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            INSERT INTO clips (id, format, captured_at, expires_at, is_pinned, size_bytes, content_hash, content, preview, source_app)
            VALUES ($id, 0, 100, NULL, $pinned, 5, $hash, x'0102', x'0304', NULL);
            """;
        command.Parameters.AddWithValue("$id", Enumerable.Repeat(idByte, 16).ToArray());
        command.Parameters.AddWithValue("$pinned", pinned ? 1 : 0);
        command.Parameters.AddWithValue("$hash", Enumerable.Repeat(idByte, 32).ToArray());
        command.ExecuteNonQuery();
    }

    [Fact]
    public void The_schema_has_a_second_step()
    {
        Migrations.Latest.ShouldBeGreaterThanOrEqualTo(2);
        Migrations.All[1].Version.ShouldBe(2);
    }

    [Fact]
    public void A_history_saved_before_boards_keeps_every_clip_and_gets_the_new_columns_empty()
    {
        SchemaMigrator.Apply(_connection, [Migrations.All[0]], _clock).IsSuccess.ShouldBeTrue();
        InsertOldClip(1, pinned: true);
        InsertOldClip(2, pinned: false);

        SchemaMigrator.Apply(_connection, Migrations.All, _clock).IsSuccess.ShouldBeTrue();

        Scalar("SELECT COUNT(*) FROM clips").ShouldBe(2);
        Scalar("SELECT COUNT(*) FROM clips WHERE board_id IS NULL AND board_position = 0").ShouldBe(2);
        Scalar("SELECT COUNT(*) FROM clips WHERE is_pinned = 1").ShouldBe(1);
        Scalar("SELECT length(content) FROM clips WHERE id = x'01010101010101010101010101010101'").ShouldBe(2);
    }

    [Fact]
    public void A_fresh_database_gets_both_steps_and_the_new_tables_are_empty()
    {
        SchemaMigrator.Apply(_connection, Migrations.All, _clock).IsSuccess.ShouldBeTrue();

        Scalar("SELECT COUNT(*) FROM schema_version").ShouldBe(Migrations.All.Count);
        Scalar("SELECT COUNT(*) FROM boards").ShouldBe(0);
        Scalar("SELECT COUNT(*) FROM snippets").ShouldBe(0);
        Scalar("SELECT COUNT(*) FROM pragma_table_info('boards') WHERE name IN ('id', 'position', 'name')").ShouldBe(3);
        Scalar("SELECT COUNT(*) FROM pragma_table_info('snippets') WHERE name IN ('id', 'position', 'name', 'body')").ShouldBe(4);
        Scalar("SELECT COUNT(*) FROM pragma_table_info('clips') WHERE name IN ('board_id', 'board_position')").ShouldBe(2);
        Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = 'clips_board'").ShouldBe(1);
    }

    [Fact]
    public void The_id_columns_only_take_sixteen_bytes()
    {
        SchemaMigrator.Apply(_connection, Migrations.All, _clock).IsSuccess.ShouldBeTrue();

        Should.Throw<SqliteException>(() => Execute("INSERT INTO boards (id, position, name) VALUES (x'0102', 0, x'00');"));
        Should.Throw<SqliteException>(() => Execute("INSERT INTO snippets (id, position, name, body) VALUES (x'0102', 0, x'00', x'00');"));
        InsertOldClip(1, pinned: true);
        Should.Throw<SqliteException>(() => Execute("UPDATE clips SET board_id = x'0102';"));
    }

    [Fact]
    public void Running_the_steps_again_changes_nothing()
    {
        SchemaMigrator.Apply(_connection, Migrations.All, _clock).IsSuccess.ShouldBeTrue();

        SchemaMigrator.Apply(_connection, Migrations.All, _clock).IsSuccess.ShouldBeTrue();

        Scalar("SELECT COUNT(*) FROM schema_version").ShouldBe(Migrations.All.Count);
    }
}
