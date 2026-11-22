namespace SpanDraft.Desktop.Persistence;

/// <summary>
/// The file-system recovery storage. Active slots live in <c>Recovery/</c> below the data directory, the two legacy
/// slots directly in the data directory (the same volume, so a move is a rename), tombstones in <c>Recovery/Discarded/</c>.
/// </summary>
public sealed class FileRecoveryStorage(string dataDirectory) : IRecoveryStorage
{
    private readonly string _root = Path.GetFullPath(dataDirectory);
    private string RecoveryDirectory => Path.Combine(_root, "Recovery");
    private string DiscardedDirectory => Path.Combine(RecoveryDirectory, "Discarded");

    public static FileRecoveryStorage Local() => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SpanDraft"));

    public string PathOf(RecoverySlot slot) => Path.Combine(slot.IsLegacy ? _root : RecoveryDirectory, slot.FileName);

    public Task<IReadOnlyList<RecoverySlot>> EnumerateAsync(CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        var slots = new List<RecoverySlot>();
        // Listing (not File.Exists, which answers false for any error) so that an area that cannot be read fails as a whole.
        if (Directory.Exists(_root))
            foreach (string file in Directory.EnumerateFiles(_root))
                if (RecoverySlot.TryParse(Path.GetFileName(file), out var legacy) && legacy.IsLegacy) slots.Add(legacy);
        if (Directory.Exists(RecoveryDirectory))
            foreach (string file in Directory.EnumerateFiles(RecoveryDirectory))
                if (RecoverySlot.TryParse(Path.GetFileName(file), out var slot) && !slot.IsLegacy) slots.Add(slot);
        return (IReadOnlyList<RecoverySlot>)slots;
    }, cancellationToken);

    public async Task<byte[]?> ReadAsync(RecoverySlot slot)
    {
        try { return await File.ReadAllBytesAsync(PathOf(slot)); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    public Task WriteAtomicAsync(RecoverySlot slot, byte[] bytes, CancellationToken cancellationToken = default) => Task.Run(async () =>
    {
        string destination = PathOf(slot);
        string directory = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, "." + slot.FileName + "." + Guid.NewGuid().ToString("N") + ".tmp");
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
            catch (Exception e) when (RecoveryErrors.IsStorage(e)) { }
        }
    }, cancellationToken);

    public Task DeleteAsync(RecoverySlot slot) => Task.Run(() =>
    {
        try { File.Delete(PathOf(slot)); }
        catch (DirectoryNotFoundException) { }
    });

    public Task MoveAsync(RecoverySlot source, RecoverySlot target) => Task.Run(() =>
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PathOf(target))!);
        // overwrite: false makes the rename itself the existence check; the source stays untouched on failure.
        File.Move(PathOf(source), PathOf(target), overwrite: false);
    });

    public Task DiscardAsync(RecoverySlot slot) => Task.Run(() =>
    {
        Directory.CreateDirectory(DiscardedDirectory);
        string name = slot.IsLegacy ? "legacy-" + Path.GetFileNameWithoutExtension(slot.FileName) + "-" + Guid.NewGuid().ToString("N") + ".json"
            : slot.FileName;
        for (int attempt = 0; ; attempt++)
        {
            try { File.Move(PathOf(slot), Path.Combine(DiscardedDirectory, name), overwrite: false); return; }
            catch (IOException) when (attempt < 3 && File.Exists(Path.Combine(DiscardedDirectory, name)) && File.Exists(PathOf(slot)))
            {
                name = Path.GetFileNameWithoutExtension(name) + "-" + Guid.NewGuid().ToString("N") + ".json";
            }
        }
    });
}
