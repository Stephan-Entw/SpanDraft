using SpanDraft.Core.Units;

namespace SpanDraft.Core.Sections;

/// <summary>A section's available bending axes; the beam's axis selection is not part of the definition.</summary>
public interface ISectionDefinition
{
    Area Area { get; }
    IReadOnlyList<SectionAxisProperties> Axes { get; }

    /// <exception cref="ArgumentOutOfRangeException">The designation is not a defined enum value.</exception>
    /// <exception cref="KeyNotFoundException">The section does not provide this axis.</exception>
    SectionAxisProperties GetAxis(SectionAxisDesignation axisDesignation);
}
