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
        cancellationToken.ThrowIfCancellationRequested();
        byte[] bytes = MaterialLibraryCodec.Serialize(library);
        // A failed load must never turn damaged user data into an empty replacement.
        var existing = await files.ReadAsync(FilePath);
        if (existing is not null) MaterialLibraryCodec.Deserialize(existing);
        cancellationToken.ThrowIfCancellationRequested();
        ensureDirectory?.Invoke();
        await files.WriteAtomicAsync(FilePath, bytes, cancellationToken);
    }
}
