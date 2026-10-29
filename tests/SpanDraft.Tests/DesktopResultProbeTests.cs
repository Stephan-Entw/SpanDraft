using SpanDraft.Analysis;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Controls;
using SpanDraft.Desktop.Layout;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;
using SpanDraft.Solver;
using Xunit;

namespace SpanDraft.Tests;

public sealed class DesktopResultProbeTests
{
    internal static EditorDocument Document() => DesktopLayoutFixture.Document().WithSupports([
        new(Guid.NewGuid(), Length.FromMeters(0), SupportType.Pinned, "A"),
        new(Guid.NewGuid(), Length.FromMeters(1), SupportType.Roller, "B")]).WithLoads([
        new EditorPointForce(Guid.NewGuid(), Length.FromMillimeters(333.37), Force.FromNewtons(-6000), "F1"),
        new EditorPointMoment(Guid.NewGuid(), Length.FromMillimeters(333.37), Moment.FromNewtonMeters(3000), "M1")]);

    [Theory]
    [InlineData(54.318, 54)]
    [InlineData(54.5, 55)]
    [InlineData(55.104, 55)]
    [InlineData(56.677, 57)]
    public void MillimeterGridEvaluatesTheSnappedWholePosition(double rawMm, double expectedMm)
    {
        var layout = StationLayout.Compute(1, 0, 1000, []);
        var position = ResultProbeSnap.Placement(layout, 100, rawMm, 100, UnitCatalog.Millimeter);
        Assert.Equal(Length.FromMillimeters(expectedMm), position);
        Assert.Equal(expectedMm.ToString(System.Globalization.CultureInfo.InvariantCulture),
            InputQuantityFormatter.FormatExact(position!.Value.Meters, UnitCatalog.Millimeter,
                System.Globalization.CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData(1000, 1)]
    [InlineData(1600, 2)]
    [InlineData(2500, 5)]
    [InlineData(10000, 10)]
    [InlineData(16000, 20)]
    [InlineData(25000, 50)]
    [InlineData(100, .1)]
    public void GridUsesTheNextOneTwoFiveStep(double lengthMm, double stepMm)
    {
        var layout = StationLayout.Compute(lengthMm / 1000, 0, 1000, []);
        double raw = 123.6 * stepMm / 1000;
        var position = ResultProbeSnap.Placement(layout, 100, layout.Transform.PhysicalToScreen(raw), 100, UnitCatalog.Millimeter);
        NumericAssert.Close(124 * stepMm / 1000, position!.Value.Meters);
    }

    [Fact]
    public void GridUsesTheCurrentLengthUnitWithoutBinaryDecimalTails()
    {
        var layout = StationLayout.Compute(3.048, 0, 1000, []);
        double raw = UnitCatalog.Foot.ToSi(1.234);
        var position = ResultProbeSnap.Placement(layout, 100, layout.Transform.PhysicalToScreen(raw), 100, UnitCatalog.Foot);
        Assert.Equal(UnitCatalog.Foot.ToSi(1.23), position!.Value.Meters);
        Assert.Equal("1.23", InputQuantityFormatter.FormatExact(position.Value.Meters, UnitCatalog.Foot,
            System.Globalization.CultureInfo.InvariantCulture));
        var tiny = StationLayout.Compute(.001, 0, 1000, []);
        var fine = ResultProbeSnap.Placement(tiny, 100, 333, 100, UnitCatalog.Meter);
        Assert.Equal("0.000333", InputQuantityFormatter.FormatExact(fine!.Value.Meters, UnitCatalog.Meter,
            System.Globalization.CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData(1, 1000)]
    [InlineData(10, 500)]
    public void StationPriorityUsesScreenDistanceAndRetainsItsExactPhysicalValue(double length, double width)
    {
        const double station = .33337;
        var layout = StationLayout.Compute(length, 0, width, [new(station, 30, 30)]);
        double screenX = layout.Transform.PhysicalToScreen(station);
        Assert.Equal(Length.FromMeters(station), ResultProbeSnap.Placement(layout, 100, screenX + 9, 100, UnitCatalog.Millimeter));
        Assert.NotEqual(Length.FromMeters(station), ResultProbeSnap.Placement(layout, 100, screenX + 11, 100, UnitCatalog.Millimeter));
    }

    [Fact]
    public void NearestStationWinsAndScreenDistanceTiesChooseTheLeftStation()
    {
        var layout = StationLayout.Compute(1, 0, 1000, [new(.5, 0, 0), new(.516, 0, 0)]);
        Assert.Equal(Length.FromMeters(.5), ResultProbeSnap.Placement(layout, 100, 508, 100, UnitCatalog.Millimeter));
        Assert.Equal(Length.FromMeters(.516), ResultProbeSnap.Placement(layout, 100, 509, 100, UnitCatalog.Millimeter));
    }

    [Theory]
    [InlineData(-.01, 100)]
    [InlineData(1000.01, 100)]
    [InlineData(500, 118.01)]
    [InlineData(500, 81.99)]
    [InlineData(double.NaN, 100)]
    [InlineData(500, double.PositiveInfinity)]
    public void InvalidOrOutsidePointerHasNoProbe(double x, double y) =>
        Assert.Null(ResultProbeSnap.Placement(StationLayout.Compute(1, 0, 1000, []), 100, x, y, UnitCatalog.Millimeter));

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void EndpointsRetainTheExactModelPosition(double x)
    {
        const double length = 1.00037;
        var layout = StationLayout.Compute(length, 0, 1000, []);
        Assert.Equal(Length.FromMeters(x * length), ResultProbeSnap.Placement(layout, 100, x * 1000, 118, UnitCatalog.Millimeter));
    }

    [Fact]
    public void RegularPositionUsesAnalyticalCantileverFields()
    {
        var beam = SolverTestSupport.Cantilever(SolverTestSupport.Point(SolverTestSupport.L, -1000));
        var document = new EditorDocument(beam.Length, beam.Material, beam.Section, beam.BendingAxis,
            [new(Guid.NewGuid(), Length.FromMeters(0), SupportType.Fixed, "A")],
            [new EditorPointForce(Guid.NewGuid(), beam.Length, Force.FromNewtons(-1000), "F1")]);
        var editor = new EditorViewModel(document, () => { });
        editor.ToggleResultProbeTool();
        const double x = .725, force = -1000, length = SolverTestSupport.L;
        editor.HoverResultProbe(Length.FromMeters(x));
        Assert.Equal(5, editor.ResultProbeRows.Count);
        Check("w", force * x * x * (3 * length - x) / (6 * SolverTestSupport.EI), QuantityKind.TransverseDisplacement);
        Check("θ", force * x * (2 * length - x) / (2 * SolverTestSupport.EI), QuantityKind.Rotation);
        Check("V", -force, QuantityKind.TransverseForce);
        Check("M", force * (length - x), QuantityKind.Moment);
        Assert.DoesNotContain(editor.ResultProbeRows, row => row.Label.StartsWith('N'));
        void Check(string label, double value, QuantityKind kind) => Assert.Equal(
            QuantityFormatter.Format(value, kind, editor.ResultPresentation), editor.ResultProbeRows.Single(r => r.Label == label).Value);
    }

    [Theory]
    [InlineData("de-DE", 3, 0)]
    [InlineData("en-US", 6, 1)]
    [InlineData("de-DE", 3, 2)]
    public void StationUsesBothSolverLimitsAndExactXWithCurrentResultFormatting(string culture, int digits, int profile)
    {
        using var scope = new ResultCultureScope(culture, culture);
        var options = new ResultPresentationOptions(DesktopResultPresentationTests.Profile(profile), digits);
        var editor = new EditorViewModel(Document(), () => { }, resultPresentation: options);
        var position = editor.Document.Loads[0].Position;
        editor.ToggleResultProbeTool();
        editor.HoverResultProbe(position, atStation: true);
        var solution = editor.Presentation.Result!.Solution;
        var left = solution.EvaluateAt(position, EvaluationSide.Left);
        var right = solution.EvaluateAt(position, EvaluationSide.Right);
        Assert.Equal(7, editor.ResultProbeRows.Count);
        Assert.Equal(new[] { "x", "w", "θ", "V−", "V+", "M−", "M+" }, editor.ResultProbeRows.Select(r => r.Label));
        var unit = options.Profile[QuantityKind.BeamLength];
        Assert.Equal(InputQuantityFormatter.FormatExact(position.Meters, unit) + " " + unit.Symbol, editor.ResultProbeRows[0].Value);
        if (profile == 0) Assert.Equal("333,37 mm", editor.ResultProbeRows[0].Value);
        Check("V−", left.ShearForce.Newtons, QuantityKind.TransverseForce);
        Check("V+", right.ShearForce.Newtons, QuantityKind.TransverseForce);
        Check("M−", left.BendingMoment.NewtonMeters, QuantityKind.Moment);
        Check("M+", right.BendingMoment.NewtonMeters, QuantityKind.Moment);
        void Check(string label, double value, QuantityKind kind) => Assert.Equal(
            QuantityFormatter.Format(value, kind, options), editor.ResultProbeRows.Single(r => r.Label == label).Value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void EndpointsShowOnlyInwardResults(double x)
    {
        var editor = new EditorViewModel(Document(), () => { });
        editor.ToggleResultProbeTool();
        editor.HoverResultProbe(Length.FromMeters(x), atStation: true);
        Assert.Equal(new[] { "x", "w", "θ", "V", "M" }, editor.ResultProbeRows.Select(r => r.Label));
        var section = editor.Presentation.Result!.Solution.EvaluateAt(Length.FromMeters(x), x == 0 ? EvaluationSide.Right : EvaluationSide.Left);
        Assert.Equal(QuantityFormatter.Format(section.ShearForce.Newtons, QuantityKind.TransverseForce, editor.ResultPresentation), editor.ResultProbeRows[3].Value);
        Assert.Equal(QuantityFormatter.Format(section.BendingMoment.NewtonMeters, QuantityKind.Moment, editor.ResultPresentation), editor.ResultProbeRows[4].Value);
    }

    [Theory]
    [InlineData(3, false)]
    [InlineData(10, true)]
    public void LimitRowsSplitOnlyWhenDifferentAtTheCurrentPresentationPrecision(int digits, bool split)
    {
        var document = Document();
        var moment = (EditorPointMoment)document.Loads[1];
        document = document.WithLoads([document.Loads[0], moment with { Moment = Moment.FromNewtonMeters(.0001) }]);
        var editor = new EditorViewModel(document, () => { }, resultPresentation: new(significantDigits: digits));
        editor.ToggleResultProbeTool();
        editor.HoverResultProbe(moment.Position, atStation: true);
        Assert.Equal(split ? new[] { "M−", "M+" } : new[] { "M" },
            editor.ResultProbeRows.Where(r => r.Label.StartsWith('M')).Select(r => r.Label));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LiveFixedUnfixedAndCancelledProbeNeverTouchesProjectState(bool dirty)
    {
        int analyses = 0;
        var state = ProjectTestSupport.State() with { Document = Document() };
        var session = ProjectSession.Open(state, ProjectTestSupport.TestPath("probe.spandraft"));
        if (dirty) session.Commit(state with { Document = state.Document with { Length = Length.FromMeters(1.1) } });
        var editor = EditorViewModel.ForSession(session, () => { }, b => { analyses++; return BeamAnalysis.Analyze(b); });
        var revision = session.CurrentRevision;
        var undo = session.UndoHistory.ToArray();
        var redo = session.RedoHistory.ToArray();
        var bytes = ProjectFileCodec.Serialize(revision.State);
        int changes = 0;
        session.Changed += () => changes++;
        editor.ToggleResultProbeTool();
        editor.HoverResultProbe(Length.FromMillimeters(725));
        Assert.Equal(Length.FromMillimeters(725), editor.ResultProbePosition);
        editor.HoverResultProbe(null);
        Assert.Null(editor.ResultProbePosition);
        editor.ToggleResultProbeFixed(Length.FromMillimeters(725));
        editor.HoverResultProbe(Length.FromMillimeters(500));
        editor.HoverResultProbe(null);
        Assert.Equal(Length.FromMillimeters(725), editor.ResultProbePosition);
        editor.ToggleResultProbeFixed(Length.FromMillimeters(500));
        Assert.False(editor.IsResultProbeFixed);
        Assert.Equal(Length.FromMillimeters(500), editor.ResultProbePosition);
        editor.HoverResultProbe(Length.FromMillimeters(600));
        Assert.Equal(Length.FromMillimeters(600), editor.ResultProbePosition);
        editor.ToggleResultProbeFixed(Length.FromMillimeters(500));
        Assert.True(editor.IsResultProbeFixed);
        Assert.Equal(Length.FromMillimeters(500), editor.ResultProbePosition);
        editor.ToggleResultProbeTool();
        Assert.False(editor.IsResultProbeTool);
        Assert.False(editor.IsResultProbeFixed);
        Assert.Empty(editor.ResultProbeRows);
        Assert.Same(revision, session.CurrentRevision);
        Assert.Equal(dirty, session.IsDirty);
        Assert.Equal(undo, session.UndoHistory);
        Assert.Equal(redo, session.RedoHistory);
        Assert.Equal(bytes, ProjectFileCodec.Serialize(session.CurrentRevision.State));
        Assert.Equal(0, changes);
        Assert.Equal(1, analyses);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void ToolSwitchesCancelEachOtherWithoutCommittingDrafts(int tool)
    {
        var editor = new EditorViewModel(Document(), () => { });
        var revision = editor.Session.CurrentRevision;
        void OtherTool()
        {
            if (tool == 0) editor.ToggleSupportTool(SupportType.Fixed);
            else if (tool == 1) editor.ToggleLoadTool(PointLoadKind.Force);
            else if (tool == 2) editor.ToggleLoadTool(PointLoadKind.Moment);
            else editor.ToggleDistributedLoadTool();
        }
        OtherTool();
        editor.ToggleResultProbeTool();
        Assert.False(editor.IsEditorNeutral);
        Assert.Null(editor.PlacementTool);
        Assert.Null(editor.LoadTool);
        Assert.False(editor.IsDistributedLoadTool);
        editor.ToggleResultProbeFixed(Length.FromMillimeters(700));
        OtherTool();
        Assert.False(editor.IsResultProbeTool);
        Assert.Null(editor.ResultProbePosition);
        Assert.Same(revision, editor.Session.CurrentRevision);
    }

    [Fact]
    public void UnavailableBusyRecalculatedAndChangedProjectStatesCannotRetainAStaleProbe()
    {
        var editor = new EditorViewModel(DesktopLayoutFixture.Document(), () => { });
        Assert.False(editor.CanResultProbe);
        editor.ToggleResultProbeTool();
        Assert.False(editor.IsResultProbeTool);
        editor.Session.Commit(editor.Session.CurrentRevision.State with { Document = Document() });
        Assert.True(editor.CanResultProbe);
        editor.Session.SetBusy(true);
        Assert.False(editor.ResultProbeToolCommand.CanExecute(null));
        editor.ToggleResultProbeTool();
        Assert.False(editor.IsResultProbeTool);
        editor.Session.SetBusy(false);
        editor.ToggleResultProbeTool();
        editor.ToggleResultProbeFixed(Length.FromMillimeters(700));
        editor.Session.Commit(editor.Session.CurrentRevision.State with { Document = editor.Document.WithSupports([]) });
        Assert.False(editor.IsResultProbeTool);
        Assert.Null(editor.ResultProbePosition);
        editor.Session.Commit(editor.Session.CurrentRevision.State with { Document = Document() });
        editor.ToggleResultProbeTool();
        editor.ChangeProjectCommand.Execute(null);
        Assert.False(editor.IsResultProbeTool);
    }
}
