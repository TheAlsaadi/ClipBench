using System.Text.Json;
using ClipBench.Core.Results;
using ClipBench.Core.Settings;

namespace ClipBench.Storage.Settings;

/// <summary>
/// Keeps the settings in a small JSON file. Reading is defensive: a file that is missing, too large, nested too
/// deeply or not a JSON object gives the defaults, a damaged file is set aside beside it for inspection, and a key
/// with a value that cannot be used keeps its default. Writing replaces the file in one step, through a temporary
/// file, so a crash never leaves half a file behind. The file holds choices only, never clip content.
/// </summary>
public sealed class JsonSettingsStore : ISettingsStore
{
    /// <summary>The largest settings file that is read.</summary>
    public const int MaxFileBytes = 64 * 1024;

    private const int Version = 1;
    private const int MaxDepth = 16;
    private const string VersionKey = "version";
    private const string SeparatorKey = "separator";
    private const string CustomSeparatorKey = "customSeparator";
    private const string RestoreKey = "restorePreviousClipboard";
    private const string KeepCopiesKey = "keepCopiesWhileCollecting";

    private static readonly byte[] _byteOrderMark = [0xEF, 0xBB, 0xBF];
    private static readonly string[] _separatorNames = Enum.GetNames<SeparatorChoice>();

    private readonly string _path;

    /// <summary>Creates the store. Nothing is read until <see cref="LoadAsync"/>.</summary>
    /// <param name="path">The settings file, an absolute path.</param>
    public JsonSettingsStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Path.IsPathRooted(path))
        {
            throw new ArgumentException("The settings file must be an absolute path.", nameof(path));
        }

        _path = Path.GetFullPath(path);
    }

    /// <inheritdoc/>
    public async Task<SettingsLoad> LoadAsync(CancellationToken cancellationToken)
    {
        byte[]? bytes;
        try
        {
            bytes = await ReadCappedAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return new SettingsLoad(AppSettings.Default, SettingsLoadStatus.Missing);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new SettingsLoad(AppSettings.Default, SettingsLoadStatus.Unreadable);
        }

        if (bytes is not null && Parse(bytes) is { } settings)
        {
            return new SettingsLoad(settings, SettingsLoadStatus.Loaded);
        }

        SetAside();
        return new SettingsLoad(AppSettings.Default, SettingsLoadStatus.Damaged);
    }

    /// <inheritdoc/>
    public async Task<Result> SaveAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        cancellationToken.ThrowIfCancellationRequested();

        var bytes = Serialize(settings);
        var temporary = _path + ".tmp";
        try
        {
            _ = Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None);
            await using (file.ConfigureAwait(false))
            {
                await file.WriteAsync(bytes, CancellationToken.None).ConfigureAwait(false);
                file.Flush(flushToDisk: true);
            }

            File.Move(temporary, _path, overwrite: true);
            return Result.Success();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Result.Failure(new ResultError(ErrorCode.StorageFailure, "Your settings could not be saved."));
        }
        finally
        {
            TryDelete(temporary);
        }
    }

    // Returns null when the file is larger than the limit.
    private async Task<byte[]?> ReadCappedAsync(CancellationToken cancellationToken)
    {
        var file = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using (file.ConfigureAwait(false))
        {
            if (file.Length > MaxFileBytes)
            {
                return null;
            }

            // The file can grow after its length was read, so the read itself is capped too.
            var buffer = new byte[MaxFileBytes + 1];
            var total = 0;
            int read;
            while (total < buffer.Length && (read = await file.ReadAsync(buffer.AsMemory(total), cancellationToken).ConfigureAwait(false)) > 0)
            {
                total += read;
            }

            return total > MaxFileBytes ? null : buffer[..total];
        }
    }

    // Returns null when the file is not a JSON object.
    private static AppSettings? Parse(byte[] bytes)
    {
        ReadOnlyMemory<byte> json = bytes;
        if (json.Span.StartsWith(_byteOrderMark))
        {
            json = json[_byteOrderMark.Length..];
        }

        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = MaxDepth });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var defaults = AppSettings.Default;
            return new AppSettings
            {
                Separator = ReadSeparator(root) ?? defaults.Separator,
                CustomSeparator = ReadString(root, CustomSeparatorKey) ?? defaults.CustomSeparator,
                RestorePreviousClipboard = ReadBoolean(root, RestoreKey) ?? defaults.RestorePreviousClipboard,
                KeepCopiesWhileCollecting = ReadBoolean(root, KeepCopiesKey) ?? defaults.KeepCopiesWhileCollecting,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // Only the exact names count: Enum.TryParse would also take numbers and other casings.
    private static SeparatorChoice? ReadSeparator(JsonElement root) =>
        ReadString(root, SeparatorKey) is { } name && _separatorNames.Contains(name, StringComparer.Ordinal)
            ? Enum.Parse<SeparatorChoice>(name)
            : null;

    private static string? ReadString(JsonElement root, string key) =>
        root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool? ReadBoolean(JsonElement root, string key) =>
        root.TryGetProperty(key, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : null;

    private static byte[] Serialize(AppSettings settings)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber(VersionKey, Version);
            writer.WriteString(SeparatorKey, settings.Separator.ToString());
            writer.WriteString(CustomSeparatorKey, settings.CustomSeparator);
            writer.WriteBoolean(RestoreKey, settings.RestorePreviousClipboard);
            writer.WriteBoolean(KeepCopiesKey, settings.KeepCopiesWhileCollecting);
            writer.WriteEndObject();
        }

        return buffer.ToArray();
    }

    // A damaged file is kept for inspection, replacing an older one, so the next save starts clean.
    private void SetAside()
    {
        try
        {
            File.Move(_path, _path + ".bad", overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The defaults are used either way; the next save replaces the file.
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A leftover temporary file is replaced by the next save.
        }
    }
}
