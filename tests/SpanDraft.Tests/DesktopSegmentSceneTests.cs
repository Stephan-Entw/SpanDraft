using SpanDraft.Core.Beams;
using SpanDraft.Core.Materials;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Sections.Parametric;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Controls;
using SpanDraft.Desktop.Layout;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.State;
using SpanDraft.Solver;
using Xunit;
using static SpanDraft.Tests.SolverTestSupport;

namespace SpanDraft.Tests;

/// <summary>Segment boundaries in the beam scene and the result diagrams.</summary>
public sealed class DesktopSegmentSceneTests
{
    private static Material Steel => new("Steel", Pressure.FromPascals(210e9), Pressure.FromPascals(235e6), MaterialStrengthType.Re);

    private static EditorDocument Document(params double[] boundaries) => new(M(2), boundaries.Select(M),
        Enumerable.Range(0, boundaries.Length + 1).Select(i => new EditorSegment(Steel,
            new RectangleSectionGeometry(M(0.1), M(0.1 + 0.05 * i)), SectionAxisDesignation.Y)),
        [new(Guid.NewGuid(), M(0), SupportType.Fixed, "A")]);

    [Fact]
    public void TheSceneCarriesTheScreenPositionOfEveryBoundaryAndNoneForOneSegment()
    {
        var document = Document(0.7, 1.4);
        var frame = new BeamLayoutFrame(StationLayout.Compute(2, 30, 470,
            StationRequirementBuilder.FromDocument(document)), new(500, 120, 40));

        var scene = BeamRenderState.Create(document, frame, DesktopLayoutFixture.Measure);

        Assert.Equal([frame.Layout.Transform.PhysicalToScreen(0.7), frame.Layout.Transform.PhysicalToScreen(1.4)],
            scene.SegmentBoundaryXs);
        Assert.True(scene.SegmentBoundaryXs[0] < scene.SegmentBoundaryXs[1]);
        Assert.Empty(BeamRenderState.Create(Document(), frame, DesktopLayoutFixture.Measure).SegmentBoundaryXs);
    }

    [Fact]
    public void BoundariesAreStationsOfTheLayoutEvenWhenTheyAreClose()
    {
        var document = Document(0.7, 0.7000001);
        var layout = StationLayout.Compute(2, 30, 470, StationRequirementBuilder.FromDocument(document));
        Assert.Contains(layout.Stations, s => s.PhysicalX == 0.7);
        Assert.Contains(layout.Stations, s => s.PhysicalX == 0.7000001);
    }

    [Theory]
    [InlineData(ResultDiagramKind.TransverseDisplacement)]
    [InlineData(ResultDiagramKind.ShearForce)]
    [InlineData(ResultDiagramKind.BendingMoment)]
    public void DiagramsAreContinuousAtABoundaryWithoutJumpAndHaveSamplesOnBothSidesOfIt(ResultDiagramKind kind)
    {
        var segments = new[]
        {
            new BeamSegment(M(0), M(0.8), Steel, Manual(8e-6), SectionAxisDesignation.Y),
            new BeamSegment(M(0.8), M(L), Steel, Manual(1e-6), SectionAxisDesignation.Y)
        };
        var solution = Solve(new BeamModel(M(L), segments, [SupportAt(0, SupportType.Fixed), SupportAt(L, SupportType.Roller)],
            [Uniform(0, L, -900), Point(1.5, -400)]));
        var layout = StationLayout.Compute(L, 72, 480, [new(0.8, 0, 0), new(1.5, 24, 24)]);

        var plot = ResultDiagramProjection.Create(solution, kind, layout, 36, 132, UnitProfile.Default);

        Assert.DoesNotContain(plot.Jumps, j => j.Left.Position.Meters == 0.8);
        var left = plot.Sections.SelectMany(s => s).Single(p => p.Position.Meters == 0.8 && p.Side == EvaluationSide.Left);
        var right = plot.Sections.SelectMany(s => s).Single(p => p.Position.Meters == 0.8 && p.Side == EvaluationSide.Right);
        Assert.Equal(left.Screen.X, right.Screen.X);
        Assert.Equal(left.SiValue, right.SiValue, Math.Max(1e-9, Math.Abs(left.SiValue) * 1e-9));
        Assert.Equal(left.Screen.Y, right.Screen.Y, 1e-6);
    }

    private static ManualSectionDefinition Manual(double i) => new(Area.FromSquareMeters(0.01),
        new ManualSectionAxis(SectionAxisDesignation.Y, SecondMomentOfArea.FromMetersToTheFourth(i), SectionModulus.FromCubicMeters(i / 0.05)));
}
