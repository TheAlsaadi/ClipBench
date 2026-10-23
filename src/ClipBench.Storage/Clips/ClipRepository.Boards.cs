using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using ClipBench.Core.Boards;
using ClipBench.Core.History;
using ClipBench.Core.Results;
using Microsoft.Data.Sqlite;

namespace ClipBench.Storage.Clips;

/// <summary>Boards: their names, and which clip is on which board. See <see cref="ClipRepository"/>.</summary>
public sealed partial class ClipRepository
{
    private const string UpdatePlacement = "UPDATE clips SET board_id = $board, board_position = $position WHERE id = $id;";
    private const string SelectBoards = "SELECT id, position, name FROM boards ORDER BY position, id;";
    private const string InsertBoard = "INSERT OR REPLACE INTO boards (id, position, name) VALUES ($id, $position, $name);";
    private const string UpdateBoardName = "UPDATE boards SET name = $name WHERE id = $id;";
    private const string ReleaseBoardClips = "UPDATE clips SET board_id = NULL, board_position = 0 WHERE board_id = $id;";
    private const string DeleteBoardRow = "DELETE FROM boards WHERE id = $id;";

    /// <summary>
    /// Puts stored clips on boards, moves them within a board or takes them off, all in one transaction. Only the plain
    /// board columns change. A clip that is not stored is ignored.
    /// </summary>
    /// <param name="placements">Where each clip now is.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Success, or a storage failure (nothing is changed).</returns>
    public Task<Result> SetBoardsAsync(IEnumerable<ClipPlacement> placements, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(placements);
        var wanted = placements.ToArray();

        return RunAsync(
            connection =>
            {
                using var transaction = connection.BeginTransaction(deferred: false);
                foreach (var placement in wanted)
                {
                    using var command = connection.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText = UpdatePlacement;
                    Blob(command, "$board", placement.Board is { } board ? BoardIdBytes(board) : null);
                    Integer(command, "$position", placement.Board is null ? 0 : placement.Position);
                    Blob(command, "$id", IdBytes(placement.Id));
                    command.ExecuteNonQuery();
                }

                transaction.Commit();
                return Result.Success();
            },
            cancellationToken);
    }

    /// <summary>Reads every board, in the order they are shown. A board whose name fails its integrity check is skipped and counted.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The boards and how many records were skipped.</returns>
    public Task<Result<LoadedItems<Board>>> LoadBoardsAsync(CancellationToken cancellationToken) =>
        RunAsync(
            connection =>
            {
                using var command = connection.CreateCommand();
                command.CommandText = SelectBoards;
                using var reader = command.ExecuteReader();
                var boards = ImmutableArray.CreateBuilder<Board>();
                var unreadable = 0;
                while (reader.Read())
                {
                    var id = new BoardId(new Guid(reader.GetFieldValue<byte[]>(0), bigEndian: true));
                    var name = OpenBoardName(id, reader.GetFieldValue<byte[]>(2));
                    if (name.IsSuccess)
                    {
                        boards.Add(new Board(id, name.Value, (int)Math.Clamp(reader.GetInt64(1), int.MinValue, int.MaxValue)));
                    }
                    else
                    {
                        unreadable++;
                    }
                }

                return Result.Success(new LoadedItems<Board>(boards.ToImmutable(), unreadable));
            },
            cancellationToken);

    /// <summary>Saves a board, replacing the stored board with the same id. The name is sealed.</summary>
    /// <param name="board">The board.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Success, or a storage failure.</returns>
    public Task<Result> SaveBoardAsync(Board board, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(board);

        return RunAsync(
            connection =>
            {
                var name = SealBoardName(board.Id, board.Name);
                if (name.IsFailure)
                {
                    return Result.Failure(name.Error);
                }

                using var command = connection.CreateCommand();
                command.CommandText = InsertBoard;
                Blob(command, "$id", BoardIdBytes(board.Id));
                Integer(command, "$position", board.Position);
                Blob(command, "$name", name.Value);
                command.ExecuteNonQuery();
                return Result.Success();
            },
            cancellationToken);
    }

    /// <summary>Changes the name of a stored board. The old name is scrubbed from the write-ahead log as when a clip is deleted.</summary>
    /// <param name="id">The board id.</param>
    /// <param name="name">The new name.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Success, <see cref="ErrorCode.BoardNotFound"/>, or a storage failure.</returns>
    public Task<Result> RenameBoardAsync(BoardId id, string name, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(name);

        return RunAsync(
            connection =>
            {
                var sealedName = SealBoardName(id, name);
                if (sealedName.IsFailure)
                {
                    return Result.Failure(sealedName.Error);
                }

                using var command = connection.CreateCommand();
                command.CommandText = UpdateBoardName;
                Blob(command, "$name", sealedName.Value);
                Blob(command, "$id", BoardIdBytes(id));
                if (command.ExecuteNonQuery() == 0)
                {
                    return BoardMissing();
                }

                Execute(connection, CheckpointTruncate);
                return Result.Success();
            },
            cancellationToken);
    }

    /// <summary>
    /// Deletes a board and takes every clip off it, in one transaction. The clips stay. The old name is scrubbed from the
    /// write-ahead log as when a clip is deleted.
    /// </summary>
    /// <param name="id">The board id.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Success, <see cref="ErrorCode.BoardNotFound"/>, or a storage failure.</returns>
    public Task<Result> DeleteBoardAsync(BoardId id, CancellationToken cancellationToken) =>
        RunAsync(
            connection =>
            {
                using var transaction = connection.BeginTransaction(deferred: false);

                using var delete = connection.CreateCommand();
                delete.Transaction = transaction;
                delete.CommandText = DeleteBoardRow;
                Blob(delete, "$id", BoardIdBytes(id));
                if (delete.ExecuteNonQuery() == 0)
                {
                    transaction.Rollback();
                    return BoardMissing();
                }

                using var release = connection.CreateCommand();
                release.Transaction = transaction;
                release.CommandText = ReleaseBoardClips;
                Blob(release, "$id", BoardIdBytes(id));
                release.ExecuteNonQuery();

                transaction.Commit();
                Execute(connection, CheckpointTruncate);
                return Result.Success();
            },
            cancellationToken);

    private static byte[] BoardIdBytes(BoardId id) => id.Value.ToByteArray(bigEndian: true);

    private static Result BoardMissing() => Result.Failure(new ResultError(ErrorCode.BoardNotFound, "That board no longer exists."));

    // A name that has no UTF-8 form cannot be sealed. The board name rules keep such a name out, so this is a backstop.
    private Result<byte[]> SealBoardName(BoardId id, string name)
    {
        byte[] plain;
        try
        {
            plain = _strictUtf8.GetBytes(name);
        }
        catch (EncoderFallbackException)
        {
            return Result.Failure<byte[]>(new ResultError(ErrorCode.InvalidBoardName, "The board name has a character that cannot be saved."));
        }

        try
        {
            return Result.Success(_crypto.Encrypt(plain, RecordAad.ForBoard(id, "name")));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    private Result<string> OpenBoardName(BoardId id, byte[] sealedName)
    {
        var plain = _crypto.Decrypt(sealedName, RecordAad.ForBoard(id, "name"));
        if (plain.IsFailure)
        {
            return Result.Failure<string>(plain.Error);
        }

        try
        {
            return Result.Success(_strictUtf8.GetString(plain.Value));
        }
        catch (ArgumentException)
        {
            return Result.Failure<string>(DamagedRecord);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain.Value);
        }
    }
}
