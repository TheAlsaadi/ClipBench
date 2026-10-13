using ClipBench.Core.Results;
using Microsoft.Data.Sqlite;

namespace ClipBench.Storage.Sqlite;

/// <summary>
/// Brings a database up to the newest schema. The whole pass is one transaction, so a step that fails
/// leaves the database exactly as it was. A database from a newer version of the app is refused and
/// left alone.
/// </summary>
internal static class SchemaMigrator
{
    /// <summary>Applies the steps the database does not have yet.</summary>
    /// <param name="connection">An open connection.</param>
    /// <param name="migrations">Every step, oldest first, numbered from 1 without gaps.</param>
    /// <param name="timeProvider">The clock that stamps each applied step.</param>
    /// <returns>Success, <see cref="ErrorCode.HistoryTooNew"/>, or a storage failure.</returns>
    public static Result Apply(SqliteConnection connection, IReadOnlyList<Migration> migrations, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(migrations);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ValidateSteps(migrations);

        try
        {
            // BEGIN IMMEDIATE: take the write lock first, so two processes starting together cannot both migrate.
            using var transaction = connection.BeginTransaction(deferred: false);

            using (var create = connection.CreateCommand())
            {
                create.Transaction = transaction;
                create.CommandText = "CREATE TABLE IF NOT EXISTS schema_version (version INTEGER NOT NULL PRIMARY KEY, applied_at INTEGER NOT NULL) STRICT;";
                create.ExecuteNonQuery();
            }

            var current = ReadMax(connection, transaction);
            if (current > migrations[^1].Version)
            {
                transaction.Rollback();
                return Result.Failure(new ResultError(
                    ErrorCode.HistoryTooNew,
                    "This history was saved by a newer version of ClipBench. Update ClipBench to open it."));
            }

            foreach (var migration in migrations.Where(step => step.Version > current))
            {
                using var run = connection.CreateCommand();
                run.Transaction = transaction;
#pragma warning disable CA2100 // The SQL is a constant that this assembly owns (see Migrations); nothing user-supplied reaches it.
                run.CommandText = migration.Sql;
#pragma warning restore CA2100
                run.ExecuteNonQuery();

                using var record = connection.CreateCommand();
                record.Transaction = transaction;
                record.CommandText = "INSERT INTO schema_version (version, applied_at) VALUES ($version, $appliedAt);";
                record.Parameters.AddWithValue("$version", migration.Version);
                record.Parameters.AddWithValue("$appliedAt", timeProvider.GetUtcNow().UtcTicks);
                record.ExecuteNonQuery();
            }

            transaction.Commit();
            return Result.Success();
        }
        catch (SqliteException exception)
        {
            return Result.Failure(SqliteErrors.From(exception));
        }
    }

    /// <summary>Reads the schema version of a database.</summary>
    /// <param name="connection">An open connection.</param>
    /// <returns>The newest applied step, or 0 for a database that has none.</returns>
    public static int ReadVersion(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        using var exists = connection.CreateCommand();
        exists.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'schema_version';";
        return Convert.ToInt64(exists.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) == 0
            ? 0
            : ReadMax(connection, transaction: null);
    }

    private static int ReadMax(SqliteConnection connection, SqliteTransaction? transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_version;";
        return Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void ValidateSteps(IReadOnlyList<Migration> migrations)
    {
        if (migrations.Count == 0)
        {
            throw new ArgumentException("There must be at least one migration step.", nameof(migrations));
        }

        for (var i = 0; i < migrations.Count; i++)
        {
            if (migrations[i].Version != i + 1)
            {
                throw new ArgumentException("Migration steps must be numbered 1, 2, 3 and so on, without gaps.", nameof(migrations));
            }
        }
    }
}
