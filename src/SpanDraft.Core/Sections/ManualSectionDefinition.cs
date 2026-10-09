using SpanDraft.Core.Units;

namespace SpanDraft.Core.Sections;

/// <summary>Tabulated area and one or two explicit axes, without inferred geometry or an axis selection.</summary>
public sealed class ManualSectionDefinition : ISectionDefinition
{
    private readonly SectionAxes axes;

    public ManualSectionDefinition(Area area, ManualSectionAxis firstAxis, ManualSectionAxis? secondAxis = null)
    {
        DomainGuard.Positive(area.SquareMeters, nameof(area));
        ArgumentNullException.ThrowIfNull(firstAxis);
        if (secondAxis is not null)
        {
            var first = firstAxis.AxisDesignation;
            var second = secondAxis.AxisDesignation;
            var validPair = (first is SectionAxisDesignation.Y or SectionAxisDesignation.Z &&
                             second is SectionAxisDesignation.Y or SectionAxisDesignation.Z) ||
                            (first is SectionAxisDesignation.U or SectionAxisDesignation.V &&
                             second is SectionAxisDesignation.U or SectionAxisDesignation.V);
            if (first == second || !validPair)
                throw new ArgumentException("Two axes must be the distinct pair Y/Z or U/V.", nameof(secondAxis));
        }
        Area = area;
        axes = secondAxis is null ? new(firstAxis.ToProperties()) : new(firstAxis.ToProperties(), secondAxis.ToProperties());
    }

    public Area Area { get; }
    public IReadOnlyList<SectionAxisProperties> Axes => axes.Values;
    public SectionAxisProperties GetAxis(SectionAxisDesignation axisDesignation) => axes.GetAxis(axisDesignation);
}
