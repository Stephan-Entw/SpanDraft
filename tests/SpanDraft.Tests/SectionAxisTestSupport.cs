using SpanDraft.Core.Beams;
using SpanDraft.Core.Loads;
using SpanDraft.Core.Materials;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Sections.Parametric;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using static SpanDraft.Tests.SolverTestSupport;

namespace SpanDraft.Tests;

internal static class SectionAxisTestSupport
{
    internal static BeamModel AxisBeam(ISectionDefinition section, SectionAxisDesignation axis,
        IEnumerable<BeamLoad>? loads = null, bool cantilever = true) =>
        new(M(L), new Material("Axis test steel", Pressure.FromPascals(E), Pressure.FromMegapascals(235)),
            section, axis, cantilever ? [SupportAt(0, SupportType.Fixed)] :
                [SupportAt(0, SupportType.Pinned), SupportAt(L, SupportType.Roller)], loads ?? []);

    internal static ISectionDefinition Profile(string shape) => shape switch
    {
        "rectangle" => new RectangleSectionGeometry(M(.04), M(.08)),
        "I" => new ISectionGeometry(M(.12), M(.06), M(.004), M(.006), M(.002)),
        "angle" => new AngleSectionGeometry(M(.06), M(.09), M(.006), M(.003)),
        "T" => new TSectionGeometry(M(.12), M(.08), M(.006), M(.01), M(.002)),
        "circle" => new CircleSectionGeometry(M(.06)),
        _ => throw new ArgumentOutOfRangeException(nameof(shape))
    };

    internal static ManualSectionAxis ManualAxis(SectionAxisDesignation axis, double i = 8e-6, double w = .0002) =>
        new(axis, SecondMomentOfArea.FromMetersToTheFourth(i), SectionModulus.FromCubicMeters(w));

    internal static ManualSectionDefinition Manual(SectionAxisDesignation first, SectionAxisDesignation? second = null) =>
        new(Area.FromSquareMeters(.003), ManualAxis(first),
            second.HasValue ? ManualAxis(second.Value, 2e-6, .0001) : null);

    // An ordinary interface implementation verifies that consumers do not classify concrete section types.
    internal sealed class TabulatedSection : ISectionDefinition
    {
        internal TabulatedSection(double a = .003, double i = 8e-6, double positiveW = .0002,
            double negativeW = .0001, double otherI = 2e-6)
        {
            Area = Area.FromSquareMeters(a);
            Axes = Array.AsReadOnly(new[] {
                new SectionAxisProperties(SectionAxisDesignation.Y, SecondMomentOfArea.FromMetersToTheFourth(i),
                    SectionModulus.FromCubicMeters(positiveW), SectionModulus.FromCubicMeters(negativeW)),
                new SectionAxisProperties(SectionAxisDesignation.Z, SecondMomentOfArea.FromMetersToTheFourth(otherI),
                    SectionModulus.FromCubicMeters(.0003), SectionModulus.FromCubicMeters(.0004)) });
        }

        public Area Area { get; }
        public IReadOnlyList<SectionAxisProperties> Axes { get; }
        internal int ResolutionCount { get; private set; }

        public SectionAxisProperties GetAxis(SectionAxisDesignation axisDesignation)
        {
            ResolutionCount++;
            if (!Enum.IsDefined(axisDesignation)) throw new ArgumentOutOfRangeException(nameof(axisDesignation));
            return Axes.SingleOrDefault(axis => axis.AxisDesignation == axisDesignation)
                ?? throw new KeyNotFoundException();
        }
    }
}
