using SpanDraft.Core.Sections;

namespace SpanDraft.Desktop.Libraries;

/// <summary>A section template. The beam's selected bending axis is not part of the preset.</summary>
public sealed class SectionLibraryEntry
{
    public SectionLibraryEntry(string name, ISectionDefinition section)
    {
        LibraryNames.Validate(name);
        ArgumentNullException.ThrowIfNull(section);
        Name = name;
        Section = section;
    }

    public string Name { get; }
    public ISectionDefinition Section { get; }
}
