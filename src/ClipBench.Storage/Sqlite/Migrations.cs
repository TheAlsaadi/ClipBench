namespace ClipBench.Storage.Sqlite;

/// <summary>The schema, step by step. Never edit a released step: add a new one.</summary>
internal static class Migrations
{
    // Plain columns hold only what retention and ordering need. Everything that reveals what was copied
    // (content, preview, source app) is a sealed blob. The hash is a keyed HMAC, not a plain hash.
    private const string Version1 = """
        CREATE TABLE meta (
            name  TEXT NOT NULL PRIMARY KEY,
            value BLOB NOT NULL
        ) STRICT;

        CREATE TABLE clips (
            id           BLOB    NOT NULL PRIMARY KEY CHECK (length(id) = 16),
            format       INTEGER NOT NULL CHECK (format BETWEEN 0 AND 4),
            captured_at  INTEGER NOT NULL,
            expires_at   INTEGER,
            is_pinned    INTEGER NOT NULL CHECK (is_pinned IN (0, 1)),
            size_bytes   INTEGER NOT NULL CHECK (size_bytes >= 0),
            content_hash BLOB    NOT NULL UNIQUE CHECK (length(content_hash) = 32),
            content      BLOB    NOT NULL,
            preview      BLOB    NOT NULL,
            source_app   BLOB
        ) STRICT;

        CREATE INDEX clips_captured_at ON clips (captured_at DESC);
        CREATE INDEX clips_expires_at ON clips (expires_at) WHERE expires_at IS NOT NULL;
        """;

    /// <summary>Every step, oldest first.</summary>
    public static IReadOnlyList<Migration> All { get; } = [new Migration(1, Version1)];

    /// <summary>The schema version this build of the app writes.</summary>
    public static int Latest => All[^1].Version;
}
