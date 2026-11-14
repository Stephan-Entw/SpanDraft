using SpanDraft.Core.Materials;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Units;

namespace SpanDraft.Core.Beams;

/// <summary>
/// A beam stretch between two exact positions with constant material, section and bending axis. The bending
/// axis is the section axis acting in the common bending plane; it is resolved once at construction.
/// Whether consecutive segments tile the beam is checked by BeamModelValidator.
/// </summary>
public sealed class BeamSegment
{
    public BeamSegment(Length start, Length end, Material material, ISectionDefinition section,
        SectionAxisDesignation bendingAxis)
    {
        if (start.Meters >= end.Meters)
            throw new ArgumentOutOfRangeException(nameof(end), "End position must be greater than start position.");
        ArgumentNullException.ThrowIfNull(material);
        ArgumentNullException.ThrowIfNull(section);
        if (!Enum.IsDefined(bendingAxis))
            throw new ArgumentOutOfRangeException(nameof(bendingAxis), bendingAxis, "Unknown bending axis.");
        SectionAxisProperties axisProperties;
        try
        {
            axisProperties = section.GetAxis(bendingAxis);
        }
        catch (KeyNotFoundException exception)
        {
            throw new ArgumentException($"The section does not provide bending axis {bendingAxis}.",
                nameof(bendingAxis), exception);
        }
        if (axisProperties is null || axisProperties.AxisDesignation != bendingAxis)
            throw new ArgumentException("The section did not resolve the selected bending axis.", nameof(bendingAxis));

        Start = start;
        End = end;
        Material = material;
        Section = section;
        BendingAxis = bendingAxis;
        BendingAxisProperties = axisProperties;
    }

    public Length Start { get; }
    public Length End { get; }
    public Material Material { get; }
    public ISectionDefinition Section { get; }
    public SectionAxisDesignation BendingAxis { get; }
    public SectionAxisProperties BendingAxisProperties { get; }
}
