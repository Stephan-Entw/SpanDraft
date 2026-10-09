namespace SpanDraft.Desktop.Persistence;

/// <summary>Small I/O seam shared by projects, recovery, settings and local user libraries.</summary>
public interface IProjectFileStore
{
    Task<byte[]?> ReadAsync(string path);
    Task WriteAtomicAsync(string path, byte[] bytes, CancellationToken cancellationToken = default);
    Task DeleteAsync(string path);
}

public sealed class ProjectFileStore : IProjectFileStore
{
    public async Task<byte[]?> ReadAsync(string path)
    {
        try { return await File.ReadAllBytesAsync(path); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    public Task WriteAtomicAsync(string path, byte[] bytes, CancellationToken cancellationToken = default) => Task.Run(async () =>
    {
        string destination = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(destination)!;
        string temporary = Path.Combine(directory, "." + Path.GetFileName(destination) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                4096, FileOptions.Asynchronous))
            {
                await stream.WriteAsync(bytes, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            // Same-directory rename publishes a complete file. Failure preserves the old destination.
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }, cancellationToken);

    public Task DeleteAsync(string path) => Task.Run(() => File.Delete(path));
}
