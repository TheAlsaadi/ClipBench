using ClipBench.Core.Results;
using Microsoft.Data.Sqlite;

namespace ClipBench.Storage.Sqlite;

/// <summary>
/// Turns a database exception into a result error. The message names the problem and the SQLite result
/// code. It never repeats the exception text, which can contain statements or data.
/// </summary>
internal static class SqliteErrors
{
    // SQLite primary result codes (https://www.sqlite.org/rescode.html).
    private const int Busy = 5;
    private const int Locked = 6;
    private const int ReadOnly = 8;
    private const int Corrupt = 11;
    private const int Full = 13;
    private const int NotADatabase = 26;

    public static ResultError From(SqliteException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception.SqliteErrorCode switch
        {
            Corrupt or NotADatabase => new ResultError(
                ErrorCode.HistoryUnreadable,
                "The saved history database is damaged. You can start with an empty history."),
            Busy or Locked => new ResultError(ErrorCode.StorageFailure, "The history database is busy. Try again."),
            Full => new ResultError(ErrorCode.StorageFailure, "The disk is full, so the history could not be saved."),
            ReadOnly => new ResultError(ErrorCode.StorageFailure, "The history database is read-only."),
            var code => new ResultError(ErrorCode.StorageFailure, $"The history database reported an error (SQLite code {code})."),
        };
    }
}
