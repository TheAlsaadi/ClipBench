namespace ClipBench.Storage;

/// <summary>
/// Where the encrypted history lives: the database and the wrapped key, side by side in one folder. The
/// folder's permissions are inherited by both files, and for the default folder under
/// <c>%LOCALAPPDATA%</c> that means the current user only.
/// </summary>
public sealed class StoragePaths
{
    /// <summary>Uses a folder the caller chose, which must be an absolute path.</summary>
    /// <param name="root">The folder that holds the database and the key file.</param>
    public StoragePaths(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        if (!Path.IsPathRooted(root))
        {
            throw new ArgumentException("The storage folder must be an absolute path.", nameof(root));
        }

        Root = Path.GetFullPath(root);
    }

    /// <summary>The folder that holds both files.</summary>
    public string Root { get; }

    /// <summary>The SQLite database file.</summary>
    public string DatabasePath => Path.Combine(Root, "clipbench.db");

    /// <summary>The file that holds the wrapped master key.</summary>
    public string KeyPath => Path.Combine(Root, "key.bin");

    /// <summary>The default location: a <c>ClipBench</c> folder in the current user's local application data.</summary>
    /// <returns>The paths for the current user.</returns>
    public static StoragePaths ForCurrentUser()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return string.IsNullOrEmpty(local)
            ? throw new InvalidOperationException("This account has no local application data folder.")
            : new StoragePaths(Path.Combine(local, "ClipBench"));
    }
}
