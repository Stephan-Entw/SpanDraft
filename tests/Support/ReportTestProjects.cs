using SpanDraft.Core.Materials;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Sections.Parametric;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.State;

namespace SpanDraft.Tests;

internal static class ReportTestProjects
{
    internal static ProjectState Create(bool overflow = false)
    {
        var section = new RectangularHollowSectionGeometry(Mm(60), Mm(100), Mm(4), Mm(6));
        var material = new Material("S235JR", Pressure.FromPascals(210e9), Pressure.FromMegapascals(235), MaterialStrengthType.Re);
        double length = overflow ? 7 : 2;
        var supports = Enumerable.Range(0, overflow ? 8 : 2).Select(i => new EditorSupport(Id(i + 1),
            M(overflow ? i : i * 2), i == 0 ? SupportType.Pinned : SupportType.Roller, ((char)('A' + i)).ToString())).ToArray();
        EditorPointLoad[] loads = overflow
            ? [Force(1, .35, -4000), Force(2, 1, -5000), Force(3, 1.65, -6000), Force(4, 2.3, -7000),
                Force(5, 2.95, -8000), Force(6, 3.6, -9000), Force(7, 4.25, -10000), Force(8, 4.9, -11000),
                Force(9, 5.55, -12000), Force(10, 6.2, -13000),
                new EditorPointMoment(Id(111), M(3.2), Moment.FromNewtonMeters(2500), "M1")]
            : [Force(1, .8, -1000)];
        EditorUniformDistributedLoad[] uniform = overflow
            ? [Uniform(1, .2, 2.4, -3000), Uniform(2, 3.6, 6.8, -5000)]
            : [Uniform(1, .2, 1.8, -500)];
        var document = new EditorDocument(M(length), material, section, SectionAxisDesignation.Y,
            supports, loads, distributedLoads: uniform);
        return new(document, new EditorPresentationState().WithOffset(loads[0].Id, new(18, 12)),
            new(overflow ? "Mehrfach gelagerter Balken" : "Einfach gelagerter Balken",
                "Neutrales Beispiel", "BEISPIEL-01", "Technik"));
    }

    private static Guid Id(int value) => new(value, 0, 0, new byte[8]);
    private static Length M(double value) => Length.FromMeters(value);
    private static Length Mm(double value) => Length.FromMillimeters(value);
    private static EditorPointForce Force(int number, double x, double value) => new(Id(100 + number), M(x),
        SpanDraft.Core.Units.Force.FromNewtons(value), "F" + number);
    private static EditorUniformDistributedLoad Uniform(int number, double x, double end, double value) => new(Id(200 + number),
        M(x), M(end), ForcePerLength.FromNewtonsPerMeter(value), "q" + number);
}
