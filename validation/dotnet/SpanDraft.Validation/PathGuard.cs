namespace SpanDraft.Validation;

/// <summary>Keeps generated files away from the golden references.</summary>
public static class PathGuard
{
    /// <summary>
    /// Whether <paramref name="path"/> lies inside <paramref name="directory"/>. The comparison ignores case, because the
    /// default macOS and Windows file systems do, and treats a sibling such as "references-x" as outside.
    /// </summary>
    public static bool IsInside(string path, string directory)
    {
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }
}
