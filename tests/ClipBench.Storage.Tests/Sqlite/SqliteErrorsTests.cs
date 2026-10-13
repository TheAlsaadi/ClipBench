using ClipBench.Core.Results;
using ClipBench.Storage.Sqlite;
using Microsoft.Data.Sqlite;

namespace ClipBench.Storage.Tests.Sqlite;

public class SqliteErrorsTests
{
    private const string Leaky = "SELECT secret-marker FROM clips WHERE text = 'secret-marker'";

    [Theory]
    [InlineData(11, ErrorCode.HistoryUnreadable)] // SQLITE_CORRUPT
    [InlineData(26, ErrorCode.HistoryUnreadable)] // SQLITE_NOTADB
    [InlineData(5, ErrorCode.StorageFailure)] // SQLITE_BUSY
    [InlineData(6, ErrorCode.StorageFailure)] // SQLITE_LOCKED
    [InlineData(13, ErrorCode.StorageFailure)] // SQLITE_FULL
    [InlineData(8, ErrorCode.StorageFailure)] // SQLITE_READONLY
    [InlineData(1, ErrorCode.StorageFailure)] // SQLITE_ERROR
    [InlineData(14, ErrorCode.StorageFailure)] // SQLITE_CANTOPEN
    public void A_database_error_maps_to_the_right_code(int sqliteCode, ErrorCode expected)
    {
        SqliteErrors.From(new SqliteException(Leaky, sqliteCode)).Code.ShouldBe(expected);
    }

    [Theory]
    [InlineData(5, "busy")]
    [InlineData(6, "busy")]
    [InlineData(13, "disk is full")]
    [InlineData(8, "read-only")]
    [InlineData(11, "damaged")]
    [InlineData(26, "damaged")]
    public void The_message_says_what_happened_in_plain_words(int sqliteCode, string words)
    {
        SqliteErrors.From(new SqliteException(Leaky, sqliteCode)).Message.ShouldContain(words);
    }

    [Fact]
    public void An_unlisted_error_names_only_the_result_code()
    {
        SqliteErrors.From(new SqliteException(Leaky, 21)).Message.ShouldContain("SQLite code 21");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(11)]
    [InlineData(13)]
    [InlineData(26)]
    public void The_message_never_repeats_the_exception_text(int sqliteCode)
    {
        SqliteErrors.From(new SqliteException(Leaky, sqliteCode)).Message.ShouldNotContain("secret-marker");
    }

    [Fact]
    public void A_missing_exception_is_a_bug()
    {
        Should.Throw<ArgumentNullException>(() => SqliteErrors.From(null!));
    }
}
