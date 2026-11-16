using SpanDraft.Core.Materials;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Sections.Parametric;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Diagnostics;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;

namespace SpanDraft.Performance;

internal static class ReferenceScenarios
{
    public static readonly string[] Ids = ["small", "typical", "large"];
    public static string[] Select(string id) => id == "all" ? Ids : Ids.Contains(id) ? [id]
        : throw new ArgumentException("Scenario must be small, typical, large or all.");
    private static Guid Id(int value) => new(value, 0, 0, new byte[8]);
    private static Length M(double value) => Length.FromMeters(value);

    public static ProjectState Create(string id)
    {
        if (!Ids.Contains(id)) throw new ArgumentException("Unknown reference scenario.");
        bool large = id == "large";
        int segments = id == "small" ? 1 : large ? 14 : 4;
        double span = id == "small" ? 6 : large ? 1 : 3;
        var material = new Material("S235JR", Pressure.FromPascals(210e9), Pressure.FromMegapascals(235), MaterialStrengthType.Re);
        var chain = Enumerable.Range(0, segments).Select(i => new EditorSegment(material,
            new RectangularHollowSectionGeometry(M(large ? .06 : .15), M((large ? .10 : .30) + (i % 2) * .02), M(.004), M(.006)),
            SectionAxisDesignation.Y) { Id = Id(1000 + i), Length = M(span) }).ToArray();
        var supports = Enumerable.Range(0, segments + 1).Select(i => new EditorSupport(Id(2000 + i), M(i * span),
            i == 0 ? SupportType.Pinned : SupportType.Roller, "S" + (i + 1))).ToArray();
        var loads = new List<EditorPointLoad>(); var distributed = new List<EditorDistributedLoad>();
        void ForceAt(double x, double value)
        {
            int n = loads.Count(l => l.Kind == PointLoadKind.Force);
            loads.Add(new EditorPointForce(Id(3000 + n), M(x), Force.FromNewtons(n == 0 ? -10000 : value), "F" + (n + 1)));
        }
        void MomentAt(double x)
        {
            int n = loads.Count(l => l.Kind == PointLoadKind.Moment);
            loads.Add(new EditorPointMoment(Id(4000 + n), M(x), Moment.FromNewtonMeters(2500), "M" + (n + 1)));
        }
        void Uniform(double start, double end, double intensity)
        {
            int n = distributed.Count;
            distributed.Add(new(Id(5000 + n), M(start), M(end), ForcePerLength.FromNewtonsPerMeter(n == 0 ? -2000 : intensity), "q" + (n + 1)));
        }
        if (large)
        {
            // Two connected copies of the existing report overflow reference's seven-span loading pattern.
            double[] positions = [.35, 1, 1.65, 2.3, 2.95, 3.6, 4.25, 4.9, 5.55, 6.2];
            for (int module = 0; module < 2; module++)
            {
                double origin = module * 7;
                for (int i = 0; i < positions.Length; i++) ForceAt(origin + positions[i], -4000 - i * 1000);
                MomentAt(origin + 3.2);
                Uniform(origin + .2, origin + 2.4, -3000);
                Uniform(origin + 3.6, origin + 6.8, -5000);
            }
        }
        else
        {
            for (int i = 0; i < segments; i++)
            {
                ForceAt(i * span + span / 2, -10000 - i * 1000);
                if (i % 2 == 1) MomentAt(i * span + span * .7);
                Uniform(i * span + .2, (i + 1) * span - .2, -2000 - i * 500);
            }
        }
        var naming = new NamingState(supports.Length + 1, loads.Count(l => l.Kind == PointLoadKind.Force) + 1,
            loads.Count(l => l.Kind == PointLoadKind.Moment) + 1, distributed.Count + 1);
        return new(new EditorDocument(chain, supports, loads, naming, distributed), new(),
            new("DEV-75 reference", "Synthetic reference", "DEV-75", "Reference"));
    }
}

internal static class ReferenceActions
{
    public static readonly string[] Ids = ["point-force-value", "first-segment-length", "distributed-load-intensity"];
    public static InteractionAction Kind(string action) => action switch
    {
        "point-force-value" => InteractionAction.PointForce, "first-segment-length" => InteractionAction.ProjectSetup,
        "distributed-load-intensity" => InteractionAction.DistributedLoad, _ => throw new ArgumentException("Unknown reference action.")
    };

    public static Func<Task<bool>> Prepare(MainWindowViewModel main, string action)
    {
        var editor = main.Editor ?? throw new InvalidOperationException("Missing reference editor.");
        string Input(double value, QuantityKind quantity) => InputQuantityFormatter.FormatExact(value, editor.ResultPresentation.Profile[quantity]);
        switch (action)
        {
            case "point-force-value":
                if (!editor.EditLoad(editor.Document.Loads.First(l => l.Kind == PointLoadKind.Force).Id))
                    throw new InvalidOperationException("Could not edit reference force.");
                editor.LoadDraft!.ValueText = Input(-11000, QuantityKind.TransverseForce);
                return () => Task.FromResult(editor.ConfirmLoad());
            case "first-segment-length":
                main.EditProject();
                var input = main.Setup.Cards[0].LengthInput;
                input.Begin(); input.Text = Input(editor.Document.Segments[0].Length.Meters + .1, QuantityKind.BeamLength);
                if (!input.Confirm()) throw new InvalidOperationException("Reference length draft was rejected.");
                return main.Setup.ConfirmAsync;
            case "distributed-load-intensity":
                if (!editor.EditDistributedLoad(editor.Document.DistributedLoads[0].Id))
                    throw new InvalidOperationException("Could not edit reference distributed load.");
                editor.DistributedLoadDraft!.StartIntensityText = Input(-2200, QuantityKind.DistributedLoad);
                editor.DistributedLoadDraft.EndIntensityText = Input(-2200, QuantityKind.DistributedLoad);
                return () => Task.FromResult(editor.ConfirmDistributedLoad());
            default: throw new ArgumentException("Unknown reference action.");
        }
    }

    public static void AssertReference(MainWindowViewModel main, ProjectState reference, bool primed)
    {
        var session = main.Session ?? throw new InvalidOperationException("Missing reference session.");
        if (!reference.ContentEquals(session.CurrentRevision.State) || session.UndoHistory.Count != 0
            || session.RedoHistory.Count != (primed ? 1 : 0) || main.Editor?.IsEditorNeutral != true)
            throw new InvalidOperationException("Reference state or history changed between iterations.");
    }
}
