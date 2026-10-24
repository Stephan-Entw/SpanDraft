using SpanDraft.Desktop.Libraries;

namespace SpanDraft.Desktop.Persistence;

public sealed class MaterialLibraryStore(IProjectFileStore files, string path, Action? ensureDirectory = null)
{
    public string FilePath { get; } = path;

    public static MaterialLibraryStore Local(IProjectFileStore files)
    {
        string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SpanDraft");
        return new(files, Path.Combine(directory, "material-library.json"), () => Directory.CreateDirectory(directory));
    }

    public async Task<UserMaterialLibrary> LoadAsync()
    {
        var bytes = await files.ReadAsync(FilePath);
        return bytes is null ? new() : MaterialLibraryCodec.Deserialize(bytes);
    }

    public async Task SaveAsync(UserMaterialLibrary library, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(library);
        cancellationToken.ThrowIfCancellationRequested();
        // A failed load must never turn damaged user data into an empty replacement.
        var existing = await files.ReadAsync(FilePath);
        var previous = existing is null ? new UserMaterialLibrary() : MaterialLibraryCodec.Deserialize(existing);
        foreach (var entry in library.All.Where(e => e.StrengthType is null))
            if (previous.Find(entry.Name) is not { } old || !MaterialSnapshotComparison.Equals(old, entry))
                throw new LibraryFormatException("A new or changed material requires a strength type.");
        byte[] bytes = MaterialLibraryCodec.Serialize(library);
        cancellationToken.ThrowIfCancellationRequested();
        ensureDirectory?.Invoke();
        await files.WriteAtomicAsync(FilePath, bytes, cancellationToken);
    }
}
