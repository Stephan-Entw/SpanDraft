using System.Text;

namespace SpanDraft.Desktop.Persistence;

/// <summary>A canonical project path, compared with the lookup semantics of its directory.</summary>
public sealed class ProjectIdentity : IEquatable<ProjectIdentity>
{
    private readonly string _directory;
    private readonly string _name;
    private readonly bool _caseSensitive;

    public ProjectIdentity(string canonicalPath, bool caseSensitive)
        : this(canonicalPath, caseSensitive, normalizeName: false) { }

    internal ProjectIdentity(string canonicalPath, bool caseSensitive, bool normalizeName)
    {
        CanonicalPath = Path.GetFullPath(canonicalPath);
        _directory = Path.GetDirectoryName(CanonicalPath) ?? throw new ArgumentException("Expected a file path.", nameof(canonicalPath));
        string name = Path.GetFileName(CanonicalPath);
        if (name.Length == 0) throw new ArgumentException("Expected a file name.", nameof(canonicalPath));
        _name = normalizeName ? name.Normalize(NormalizationForm.FormD) : name;
        _caseSensitive = caseSensitive;
    }

    public string CanonicalPath { get; }
    private StringComparer NameComparer => _caseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    public bool Equals(ProjectIdentity? other) => other is not null && _caseSensitive == other._caseSensitive
        && StringComparer.Ordinal.Equals(_directory, other._directory) && NameComparer.Equals(_name, other._name);

    public override bool Equals(object? obj) => obj is ProjectIdentity other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(_directory, _caseSensitive, NameComparer.GetHashCode(_name));
}

public interface IProjectIdentityResolver
{
    ProjectIdentity Resolve(string path);
}
