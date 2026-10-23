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

    // Boards and snippets. Names and snippet text are sealed blobs, like clip content. A clip's board and its place
    // on the board are plain columns, like the pin flag, because ordering needs them. board_id has no foreign key on
    // purpose: a board that is missing is repaired when the boards are loaded, and a tampered row cannot fail a write.
    private const string Version2 = """
        CREATE TABLE boards (
            id       BLOB    NOT NULL PRIMARY KEY CHECK (length(id) = 16),
            position INTEGER NOT NULL,
            name     BLOB    NOT NULL
        ) STRICT;

        ALTER TABLE clips ADD COLUMN board_id BLOB CHECK (board_id IS NULL OR length(board_id) = 16);
        ALTER TABLE clips ADD COLUMN board_position INTEGER NOT NULL DEFAULT 0;
        CREATE INDEX clips_board ON clips (board_id, board_position) WHERE board_id IS NOT NULL;

        CREATE TABLE snippets (
            id       BLOB    NOT NULL PRIMARY KEY CHECK (length(id) = 16),
            position INTEGER NOT NULL,
            name     BLOB    NOT NULL,
            body     BLOB    NOT NULL
        ) STRICT;
        """;

    /// <summary>Every step, oldest first.</summary>
    public static IReadOnlyList<Migration> All { get; } = [new Migration(1, Version1), new Migration(2, Version2)];

    /// <summary>The schema version this build of the app writes.</summary>
    public static int Latest => All[^1].Version;
}
