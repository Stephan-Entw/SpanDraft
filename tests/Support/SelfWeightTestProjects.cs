using SpanDraft.Core.Materials;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.State;
using static SpanDraft.Tests.ProjectTestSupport;

namespace SpanDraft.Tests;

internal static class SelfWeightTestProjects
{
    internal static Material Steel(double? density = 7850) => new("Steel", Pressure.FromPascals(210e9),
        Pressure.FromPascals(235e6), MaterialStrengthType.Re,
        density is { } rho ? MassDensity.FromKilogramsPerCubicMeter(rho) : null, null);
    internal static EditorDocument Document(bool enabled = false) => new(M(3), Steel(),
        new SpanDraft.Core.Sections.Parametric.RectangleSectionGeometry(M(.1), M(.2)), SectionAxisDesignation.Y, [new(Guid.NewGuid(), M(0), SupportType.Fixed, "A")],
        selfWeight: enabled ? new(Guid.NewGuid(), true) : null);
}
