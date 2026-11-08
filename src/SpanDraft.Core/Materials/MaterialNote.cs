using System.Collections.ObjectModel;

namespace SpanDraft.Core.Materials;

/// <summary>Immutable reference-value guidance; never a mechanical input.</summary>
public sealed class MaterialNote : IEquatable<MaterialNote>
{
    public MaterialNote(string defaultText, IEnumerable<KeyValuePair<string, string>>? translations = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultText);
        DefaultText = defaultText.Trim();
        var copy = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (language, text) in translations ?? [])
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(language);
            ArgumentException.ThrowIfNullOrWhiteSpace(text);
            copy.Add(language.Trim().ToLowerInvariant(), text.Trim());
        }
        Translations = new ReadOnlyDictionary<string, string>(copy);
    }

    public string DefaultText { get; }
    public IReadOnlyDictionary<string, string> Translations { get; }

    public bool Equals(MaterialNote? other) => other is not null && DefaultText == other.DefaultText
        && Translations.SequenceEqual(other.Translations);
    public override bool Equals(object? obj) => obj is MaterialNote other && Equals(other);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(DefaultText, StringComparer.Ordinal);
        foreach (var (language, text) in Translations) { hash.Add(language); hash.Add(text); }
        return hash.ToHashCode();
    }
}
