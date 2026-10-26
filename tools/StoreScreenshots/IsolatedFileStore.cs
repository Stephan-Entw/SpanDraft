using SpanDraft.Desktop.Persistence;

namespace SpanDraft.StoreScreenshots;

/// <summary>No fallback to real user state, including best-effort store reads.</summary>
public sealed class IsolatedFileStore : IProjectFileStore
{
    private readonly IProjectFileStore inner;
    private readonly string stateRoot;
    private readonly HashSet<string> projects;
    private readonly List<string> accesses = [];
    public string[] AccessedPaths { get { lock (accesses) return accesses.ToArray(); } }

    public IsolatedFileStore(string stateRoot, IEnumerable<string> projectFiles, IProjectFileStore? inner = null)
    {
        this.stateRoot = Path.GetFullPath(stateRoot);
        projects = new(projectFiles.Select(Path.GetFullPath), StringComparer.OrdinalIgnoreCase);
        this.inner = inner ?? new ProjectFileStore();
    }

    public static bool IsWithin(string root, string path)
    {
        string relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(path));
        return relative != "." && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !Path.IsPathRooted(relative);
    }

    private string Check(string path, bool write)
    {
        path = Path.GetFullPath(path);
        if ((!IsWithin(stateRoot, path) && !(projects.Contains(path) && !write))
            || write && (projects.Contains(path) || Path.GetExtension(path).Equals(".spandraft", StringComparison.OrdinalIgnoreCase)))
            throw new System.Security.SecurityException("Access outside the isolated application state, or project modification, was refused.");
        EnsureNoReparsePoints(path);
        lock (accesses) accesses.Add(path);
        return path;
    }

    public static void EnsureNoReparsePoints(string path)
    {
        // A reparse point must not redirect an allowed path into the real profile.
        for (string? current = path; current is not null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new System.Security.SecurityException("Reparse points are not allowed in screenshot state or fixture paths.");
    }

    public Task<byte[]?> ReadAsync(string path) => inner.ReadAsync(Check(path, false));
    public Task WriteAtomicAsync(string path, byte[] bytes, CancellationToken cancellationToken = default) =>
        inner.WriteAtomicAsync(Check(path, true), bytes, cancellationToken);
    public Task DeleteAsync(string path) => inner.DeleteAsync(Check(path, true));
}
