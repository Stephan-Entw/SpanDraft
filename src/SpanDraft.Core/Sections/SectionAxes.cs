namespace SpanDraft.Core.Sections;

internal sealed class SectionAxes
{
    internal SectionAxes(params SectionAxisProperties[] axes) =>
        Values = Array.AsReadOnly(axes.OrderBy(axis => axis.AxisDesignation).ToArray());

    internal IReadOnlyList<SectionAxisProperties> Values { get; }

    internal SectionAxisProperties GetAxis(SectionAxisDesignation axisDesignation)
    {
        ValidateDesignation(axisDesignation);
        return Values.FirstOrDefault(axis => axis.AxisDesignation == axisDesignation)
            ?? throw new KeyNotFoundException($"The section does not provide axis {axisDesignation}.");
    }

    internal static void ValidateDesignation(SectionAxisDesignation axisDesignation)
    {
        if (!Enum.IsDefined(axisDesignation))
            throw new ArgumentOutOfRangeException(nameof(axisDesignation), axisDesignation, "Unknown section axis.");
    }
}
