using SpanDraft.Core.Loads;
using SpanDraft.Core.Materials;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;

namespace SpanDraft.Core.Beams;

/// <summary>
/// A single straight beam with constant material and section. Local x runs rightwards
/// from zero at the left end; transverse positive is upwards in the selected planar
/// bending plane; positive rotation and moment are counterclockwise. The bending axis
/// is resolved at construction. Further consistency is checked by BeamModelValidator.
/// </summary>
public sealed class BeamModel
{
    public BeamModel(Length length, Material material, Section section,
        IEnumerable<Support> supports, IEnumerable<BeamLoad> loads)
        : this(length, material, section, SectionAxisDesignation.Y, supports, loads)
    {
    }

    /// <summary>Creates a beam using exactly the explicitly selected section axis.</summary>
    public BeamModel(Length length, Material material, ISectionDefinition section,
        SectionAxisDesignation bendingAxis, IEnumerable<Support> supports, IEnumerable<BeamLoad> loads)
    {
        DomainGuard.Positive(length.Meters, nameof(length));
        ArgumentNullException.ThrowIfNull(material);
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(supports);
        ArgumentNullException.ThrowIfNull(loads);

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

        Support[] supportArray = supports.ToArray();
        BeamLoad[] loadArray = loads.ToArray();
        if (supportArray.Any(support => support is null))
            throw new ArgumentException("Supports must not contain null entries.", nameof(supports));
        if (loadArray.Any(load => load is null))
            throw new ArgumentException("Loads must not contain null entries.", nameof(loads));

        Length = length;
        Material = material;
        Section = section;
        BendingAxis = bendingAxis;
        BendingAxisProperties = axisProperties;
        Supports = Array.AsReadOnly(supportArray);
        Loads = Array.AsReadOnly(loadArray);
    }

    public Length Length { get; }
    public Material Material { get; }
    public ISectionDefinition Section { get; }
    public SectionAxisDesignation BendingAxis { get; }
    public SectionAxisProperties BendingAxisProperties { get; }
    public IReadOnlyList<Support> Supports { get; }
    public IReadOnlyList<BeamLoad> Loads { get; }
}
