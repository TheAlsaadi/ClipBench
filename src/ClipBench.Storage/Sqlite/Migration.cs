namespace ClipBench.Storage.Sqlite;

/// <summary>One step of the database schema. The SQL is a constant owned by this assembly.</summary>
/// <param name="Version">The schema version this step produces. Versions start at 1 and have no gaps.</param>
/// <param name="Sql">The statements to run.</param>
internal sealed record Migration(int Version, string Sql);
