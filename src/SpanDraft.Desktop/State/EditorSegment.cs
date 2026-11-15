using SpanDraft.Core.Materials;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Units;

namespace SpanDraft.Desktop.State;

/// <summary>
/// One beam segment: its length, section, material and bending axis. The beam is the ordered chain of its
/// segments, so a segment's extent follows from the lengths in front of it. A segment keeps its identity across
/// edits; <see cref="Id"/> is what positions refer to, never the segment's order. The display name is desktop
/// metadata of the section and never carries over to another segment.
/// </summary>
public sealed record EditorSegment
{
    public EditorSegment(Material material, ISectionDefinition section, SectionAxisDesignation bendingAxis,
        string? sectionDisplayName = null)
    {
        ArgumentNullException.ThrowIfNull(material);
        ArgumentNullException.ThrowIfNull(section);
        if (!Enum.IsDefined(bendingAxis))
            throw new ArgumentOutOfRangeException(nameof(bendingAxis), bendingAxis, "Unknown bending axis.");
        SectionAxisProperties axis;
        try { axis = section.GetAxis(bendingAxis); }
        catch (KeyNotFoundException e)
        { throw new ArgumentException("The section does not provide the selected bending axis.", nameof(bendingAxis), e); }
        if (axis is null || axis.AxisDesignation != bendingAxis)
            throw new ArgumentException("The section did not resolve the selected bending axis.", nameof(bendingAxis));
        if (sectionDisplayName is not null && (string.IsNullOrWhiteSpace(sectionDisplayName) || sectionDisplayName != sectionDisplayName.Trim()))
            throw new ArgumentException("Section display names must be nonempty and trimmed.", nameof(sectionDisplayName));
        Material = material;
        Section = section;
        BendingAxis = bendingAxis;
        SectionDisplayName = sectionDisplayName;
    }

    /// <summary>
    /// The stable identity. <see cref="Guid.Empty"/> means "not assigned yet"; a document assigns it when it takes
    /// the segment, and operations that create a segment give it a new one.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>The extent along the beam axis; zero until a document or a caller assigns it.</summary>
    public Length Length { get; init; }

    public Material Material { get; }
    public ISectionDefinition Section { get; }
    public SectionAxisDesignation BendingAxis { get; }

    /// <summary>Null selects a generated name; explicit names are desktop metadata.</summary>
    public string? SectionDisplayName { get; }

    /// <summary>The same segment (identity and length) with another section; the material is kept.</summary>
    public EditorSegment WithSection(ISectionDefinition section, SectionAxisDesignation bendingAxis, string? sectionDisplayName) =>
        new EditorSegment(Material, section, bendingAxis, sectionDisplayName) { Id = Id, Length = Length };

    /// <summary>The same segment (identity and length) with another material.</summary>
    public EditorSegment WithMaterial(Material material) =>
        new EditorSegment(material, Section, BendingAxis, SectionDisplayName) { Id = Id, Length = Length };
}
