using ClipBench.Core.Clips;
using ClipBench.Storage.Clips;
using ClipBench.Storage.Crypto;
using Microsoft.Data.Sqlite;

namespace ClipBench.Storage.Tests.Support;

/// <summary>
/// Real SQLite in a temp folder with a real crypto service. Opened repositories are closed when the test
/// ends, and the folder is removed.
/// </summary>
public abstract class RepositoryTestBase : IAsyncDisposable
{
    private readonly List<ClipRepository> _opened = [];

    private readonly TempDirectory _directory = new();

    internal FakeTimeProvider Clock { get; } = new();

    internal AesGcmCryptoService Crypto { get; } = new(TestKeys.Master(1));

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    protected string FolderPath => _directory.Path;

    protected string DatabasePath => _directory.File("clipbench.db");

    internal async Task<ClipRepository> OpenAsync(ICryptoService? crypto = null, string? path = null)
    {
        var result = await ClipRepository.OpenAsync(path ?? DatabasePath, crypto ?? Crypto, Clock, Ct);
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? $"{result.Error.Code}: {result.Error.Message}" : string.Empty);
        _opened.Add(result.Value);
        return result.Value;
    }

    /// <summary>A separate connection for looking at, or damaging, what is on disk. The caller disposes it.</summary>
    protected SqliteConnection Raw(string? path = null)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path ?? DatabasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false,
        }.ToString());
        connection.Open();
        return connection;
    }

    protected long RawCount(string sql)
    {
        using var connection = Raw();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    protected byte[] RawBlob(string column, ClipId id)
    {
        using var connection = Raw();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {column} FROM clips WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id.Value.ToByteArray(bigEndian: true));
        return (byte[])command.ExecuteScalar()!;
    }

    protected void RawSet(string column, ClipId id, object value)
    {
        using var connection = Raw();
        using var command = connection.CreateCommand();
        command.CommandText = $"UPDATE clips SET {column} = $value WHERE id = $id;";
        command.Parameters.AddWithValue("$value", value);
        command.Parameters.AddWithValue("$id", id.Value.ToByteArray(bigEndian: true));
        command.ExecuteNonQuery().ShouldBe(1);
    }

    protected void RawSwap(string column, ClipId first, ClipId second)
    {
        var a = RawBlob(column, first);
        var b = RawBlob(column, second);
        RawSet(column, first, b);
        RawSet(column, second, a);
    }

    protected static async Task SaveAll(ClipRepository repository, params Clip[] clips)
    {
        foreach (var clip in clips)
        {
            (await repository.SaveAsync(clip, Ct)).IsSuccess.ShouldBeTrue();
        }
    }

    public virtual async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        foreach (var repository in _opened)
        {
            await repository.DisposeAsync();
        }

        Crypto.Dispose();
        _directory.Dispose();
    }
}
