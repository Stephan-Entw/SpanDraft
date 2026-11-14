using SpanDraft.Core.Materials;
using SpanDraft.Core.Sections;

namespace SpanDraft.Desktop.State;

/// <summary>
/// The properties of one beam segment. Segments have no identity of their own: they are addressed by their
/// order, and their extent follows from the document's boundaries and length. The display name is desktop
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

    public Material Material { get; }
    public ISectionDefinition Section { get; }
    public SectionAxisDesignation BendingAxis { get; }

    /// <summary>Null selects a generated name; explicit names are desktop metadata.</summary>
    public string? SectionDisplayName { get; }
}
