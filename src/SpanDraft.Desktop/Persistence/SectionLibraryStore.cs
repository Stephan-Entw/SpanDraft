using SpanDraft.Desktop.Libraries;

namespace SpanDraft.Desktop.Persistence;

public sealed class SectionLibraryStore(IProjectFileStore files, string path, Action? ensureDirectory = null)
{
    public string FilePath { get; } = path;

    public static SectionLibraryStore Local(IProjectFileStore files)
    {
        string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SpanDraft");
        return new(files, Path.Combine(directory, "section-library.json"), () => Directory.CreateDirectory(directory));
    }

    public async Task<UserSectionLibrary> LoadAsync()
    {
        var bytes = await files.ReadAsync(FilePath);
        return bytes is null ? new() : SectionLibraryCodec.Deserialize(bytes);
    }

    public async Task SaveAsync(UserSectionLibrary library, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        byte[] bytes = SectionLibraryCodec.Serialize(library);
        var existing = await files.ReadAsync(FilePath);
        if (existing is not null) SectionLibraryCodec.Deserialize(existing);
        cancellationToken.ThrowIfCancellationRequested();
        ensureDirectory?.Invoke();
        await files.WriteAtomicAsync(FilePath, bytes, cancellationToken);
    }
}
