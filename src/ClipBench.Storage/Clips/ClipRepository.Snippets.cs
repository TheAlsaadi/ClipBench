using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using ClipBench.Core.History;
using ClipBench.Core.Results;
using ClipBench.Core.Snippets;

namespace ClipBench.Storage.Clips;

/// <summary>Snippets: their names and texts. See <see cref="ClipRepository"/>.</summary>
public sealed partial class ClipRepository
{
    private const string SelectSnippets = "SELECT id, position, name, body FROM snippets ORDER BY position, id;";
    private const string InsertSnippet = "INSERT OR REPLACE INTO snippets (id, position, name, body) VALUES ($id, $position, $name, $body);";
    private const string DeleteSnippetRow = "DELETE FROM snippets WHERE id = $id;";

    /// <summary>Reads every snippet, in the order they are shown. A snippet that fails its integrity check is skipped and counted.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The snippets and how many records were skipped.</returns>
    public Task<Result<LoadedItems<Snippet>>> LoadSnippetsAsync(CancellationToken cancellationToken) =>
        RunAsync(
            connection =>
            {
                using var command = connection.CreateCommand();
                command.CommandText = SelectSnippets;
                using var reader = command.ExecuteReader();
                var snippets = ImmutableArray.CreateBuilder<Snippet>();
                var unreadable = 0;
                while (reader.Read())
                {
                    var id = new SnippetId(new Guid(reader.GetFieldValue<byte[]>(0), bigEndian: true));
                    var name = OpenSnippetText(id, "name", reader.GetFieldValue<byte[]>(2));
                    var body = OpenSnippetText(id, "body", reader.GetFieldValue<byte[]>(3));
                    if (name.IsSuccess && body.IsSuccess)
                    {
                        snippets.Add(new Snippet(id, name.Value, body.Value, (int)Math.Clamp(reader.GetInt64(1), int.MinValue, int.MaxValue)));
                    }
                    else
                    {
                        unreadable++;
                    }
                }

                return Result.Success(new LoadedItems<Snippet>(snippets.ToImmutable(), unreadable));
            },
            cancellationToken);

    /// <summary>Saves a snippet, replacing the stored snippet with the same id. The name and the text are sealed.</summary>
    /// <param name="snippet">The snippet.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Success, or a storage failure.</returns>
    public Task<Result> SaveSnippetAsync(Snippet snippet, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snippet);

        return RunAsync(
            connection =>
            {
                var name = SealSnippetText(snippet.Id, "name", snippet.Name);
                var body = SealSnippetText(snippet.Id, "body", snippet.Body);
                if (name.IsFailure || body.IsFailure)
                {
                    return Result.Failure(name.IsFailure ? name.Error : body.Error);
                }

                using var command = connection.CreateCommand();
                command.CommandText = InsertSnippet;
                Blob(command, "$id", snippet.Id.Value.ToByteArray(bigEndian: true));
                Integer(command, "$position", snippet.Position);
                Blob(command, "$name", name.Value);
                Blob(command, "$body", body.Value);
                command.ExecuteNonQuery();

                // An edit replaces the old text, so the log is emptied to take the old text out of it.
                Execute(connection, CheckpointTruncate);
                return Result.Success();
            },
            cancellationToken);
    }

    /// <summary>Deletes a snippet. The old text is scrubbed from the write-ahead log as when a clip is deleted.</summary>
    /// <param name="id">The snippet id.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Success, <see cref="ErrorCode.SnippetNotFound"/>, or a storage failure.</returns>
    public Task<Result> DeleteSnippetAsync(SnippetId id, CancellationToken cancellationToken) =>
        RunAsync(
            connection =>
            {
                using var command = connection.CreateCommand();
                command.CommandText = DeleteSnippetRow;
                Blob(command, "$id", id.Value.ToByteArray(bigEndian: true));
                if (command.ExecuteNonQuery() == 0)
                {
                    return Result.Failure(new ResultError(ErrorCode.SnippetNotFound, "That snippet no longer exists."));
                }

                Execute(connection, CheckpointTruncate);
                return Result.Success();
            },
            cancellationToken);

    // Text with no UTF-8 form cannot be sealed. The name and text rules keep such text out, so this is a backstop.
    private Result<byte[]> SealSnippetText(SnippetId id, string column, string text)
    {
        byte[] plain;
        try
        {
            plain = _strictUtf8.GetBytes(text);
        }
        catch (EncoderFallbackException)
        {
            return Result.Failure<byte[]>(new ResultError(ErrorCode.InvalidSnippet, "The snippet has a character that cannot be saved."));
        }

        try
        {
            return Result.Success(_crypto.Encrypt(plain, RecordAad.ForSnippet(id, column)));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    private Result<string> OpenSnippetText(SnippetId id, string column, byte[] sealedText)
    {
        var plain = _crypto.Decrypt(sealedText, RecordAad.ForSnippet(id, column));
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
