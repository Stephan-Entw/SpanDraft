using System.Globalization;
using SpanDraft.Analysis;
using SpanDraft.Core.Materials;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Sections.Parametric;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.State;
using SpanDraft.Reporting.Prototype;

namespace SpanDraft.Tests;

// Two fixed review cases, not a project adapter or a production reporting contract.
internal static class ReportPrototypeFixtures
{
    internal sealed record Fixture(PrototypeReportData Data, EditorDocument Document,
        AnalysisPresentationState Presentation, EditorPresentationState EditorPresentation)
    {
        internal BeamAnalysisResult Analysis => Presentation.Result!;
    }

    internal static Fixture Create(bool overflow, UnitProfile? profile = null)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
        try { return CreateGerman(overflow, profile ?? UnitProfile.MechanicalEngineering); }
        finally { CultureInfo.CurrentCulture = previousCulture; }
    }

    private static Fixture CreateGerman(bool overflow, UnitProfile profile)
    {
        var section = new RectangularHollowSectionGeometry(Mm(60), Mm(100), Mm(4), Mm(6));
        var material = new Material("S235JR", Pressure.FromPascals(210e9), Pressure.FromMegapascals(235));
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
        var outcome = BeamAnalysis.Analyze(document.ToBeamModel());
        var analysis = outcome.Result ?? throw new InvalidOperationException("The report fixture must solve.");
        var presentation = AnalysisPresentationState.FromOutcome(outcome, new(profile));
        var references = presentation.References;
        string Number(double si, QuantityKind kind) => QuantityFormatter.FormatNumber(si, kind, profile,
            PresentationMode.Standard, references);
        string Quantity(double si, QuantityKind kind) => QuantityFormatter.Format(si, kind, profile,
            PresentationMode.Standard, references);
        string Position(double meters) => InputQuantityFormatter.Display(meters, profile[QuantityKind.BeamLength]);
        PrototypeTable[] tables =
        [
            new("Lager", ["Name", "Typ", "x [" + profile[QuantityKind.BeamLength].Symbol + "]"], [.17f, .48f, .35f],
                supports.Select(s => new[] { s.Name, s.Type == SupportType.Pinned ? "Festlager" : "Loslager", Position(s.Position.Meters) }).ToArray()),
            new("Lasten", ["Name", "Wert", "x [" + profile[QuantityKind.BeamLength].Symbol + "]"], [.16f, .42f, .42f],
                loads.Select(l => new[] { l.Name, InputQuantityFormatter.WithUnit(l.Value,
                    profile[l.Kind == PointLoadKind.Force ? QuantityKind.TransverseForce : QuantityKind.Moment]), Position(l.Position.Meters) })
                .Concat(uniform.Select(l => new[] { l.Name, InputQuantityFormatter.WithUnit(l.Intensity.NewtonsPerMeter,
                    profile[QuantityKind.DistributedLoad]), Position(l.StartPosition.Meters) + "…" + Position(l.EndPosition.Meters) })).ToArray()),
            new("Reaktionen", ["Lager", "Rx [N]", "Ry [N]", "M [N·m]"], [.16f, .24f, .30f, .30f],
                supports.Select(s =>
                {
                    var node = analysis.Solution.Nodes.Single(n => n.Position == s.Position);
                    return new[] { s.Name, node.ReactionX is { } rx ? Number(rx.Newtons, QuantityKind.AxialForce) : "-",
                        node.ReactionY is { } ry ? Number(ry.Newtons, QuantityKind.TransverseForce) : "-",
                        node.ReactionMoment is { } m ? Number(m.NewtonMeters, QuantityKind.Moment) : "-" };
                }).ToArray())
        ];
        var engineering = analysis.Engineering;
        var axis = analysis.Beam.BendingAxisProperties;
        var data = new PrototypeReportData(overflow ? "Mehrfach gelagerter Balken" : "Einfach gelagerter Balken",
            "Neutrales Beispiel", overflow ? "BEISPIEL-02" : "BEISPIEL-01", "Technik",
            typeof(PrototypeReport).Assembly.GetName().Version!.ToString(3), "22.10.2026",
            section,
            Quantity(axis.SecondMomentOfArea.MetersToTheFourth, QuantityKind.SecondMomentOfArea),
            Quantity(axis.PositiveSectionModulus.CubicMeters, QuantityKind.SectionModulus), tables,
            [
                ("Maximale Durchbiegung |w|max", Quantity(engineering.TransverseDisplacementMagnitude.Meters, QuantityKind.TransverseDisplacement),
                    "bei x = " + Position(engineering.CriticalTransverseDisplacement.Position.Meters) + " " + profile[QuantityKind.BeamLength].Symbol),
                ("Maßgebendes Biegemoment |M|max", Quantity(engineering.BendingMomentMagnitude.NewtonMeters, QuantityKind.Moment),
                    "bei x = " + Position(engineering.CriticalBendingMoment.Position.Meters) + " " + profile[QuantityKind.BeamLength].Symbol),
                ("Max. elastische Biegespannung σb,max", Quantity(engineering.MaximumBendingStress.Pascals, QuantityKind.Stress), null),
                ("Sicherheitsfaktor S = Re / σb,max", Quantity(engineering.SafetyFactor, QuantityKind.SafetyFactor), null)
            ]);
        return new(data, document, presentation, new());
    }

    private static Guid Id(int value) => new(value, 0, 0, new byte[8]);
    private static Length M(double value) => Length.FromMeters(value);
    private static Length Mm(double value) => Length.FromMillimeters(value);
    private static EditorPointForce Force(int number, double x, double value) => new(Id(100 + number), M(x),
        SpanDraft.Core.Units.Force.FromNewtons(value), "F" + number);
    private static EditorUniformDistributedLoad Uniform(int number, double x, double end, double value) => new(Id(200 + number),
        M(x), M(end), ForcePerLength.FromNewtonsPerMeter(value), "q" + number);
}
