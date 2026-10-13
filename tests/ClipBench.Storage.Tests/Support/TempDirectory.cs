namespace ClipBench.Storage.Tests.Support;

/// <summary>A unique folder under the system temp folder that is removed when the test is done.</summary>
internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ClipBench.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // A test that fails while it still holds a file open must report its own failure, not this one.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
