using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Controls;
using SpanDraft.Desktop.Layout;
using SpanDraft.Solver;
using Xunit;
using static SpanDraft.Tests.SolverTestSupport;

namespace SpanDraft.Tests;

public sealed class DesktopResultDiagramProjectionTests
{
    private static ResultDiagramProjection Project(BeamSolution solution, ResultDiagramKind kind,
        StationLayoutResult? layout = null) => ResultDiagramProjection.Create(solution, kind,
            layout ?? StationLayout.Compute(L, 72, 872, []), 36, 132);

    [Theory]
    [InlineData(ResultDiagramKind.TransverseDisplacement)]
    [InlineData(ResultDiagramKind.ShearForce)]
    [InlineData(ResultDiagramKind.BendingMoment)]
    public void EverySampleAndJumpUsesTheEditorsExactDistortedMapping(ResultDiagramKind kind)
    {
        var solution = Solve(Beam(loads: [Point(.02, -1000), Couple(.03, 200), Uniform(.4, 1.5, -500)]));
        var layout = StationLayout.Compute(L, 72, 480,
            [new(.01, 16, 16), new(.02, 24, 24), new(.03, 24, 24), new(.4, 16, 16), new(1.5, 16, 16)]);
        Assert.True(layout.IsDistorted);
        var plot = Project(solution, kind, layout);
        Assert.Same(layout, plot.StationLayout);
        Assert.All(plot.Sections.SelectMany(s => s).Concat(plot.Jumps.SelectMany(j => new[] { j.Left, j.Right })),
            p => Assert.Equal(layout.Transform.PhysicalToScreen(p.Position.Meters), p.Screen.X));
        foreach (var station in layout.Stations)
            Assert.Contains(plot.Sections.SelectMany(s => s), p => p.Position.Meters == station.PhysicalX && p.Screen.X == station.ScreenX);
    }

    [Theory]
    [InlineData(ResultDiagramKind.TransverseDisplacement)]
    [InlineData(ResultDiagramKind.ShearForce)]
    [InlineData(ResultDiagramKind.BendingMoment)]
    public void CantileverTipForceDisplaysCubicDeflectionLinearMomentAndConstantShear(ResultDiagramKind kind)
    {
        var plot = Project(Solve(Cantilever(Point(L, -1000))), kind);
        Assert.All(plot.Sections.SelectMany(s => s), p =>
        {
            double x = p.Position.Meters;
            double expected = kind switch
            {
                ResultDiagramKind.TransverseDisplacement => -1000 * x * x * (3 * L - x) / (6 * EI) * 1000,
                ResultDiagramKind.ShearForce => 1000,
                _ => -1000 * (L - x)
            };
            Close(expected, p.Value, 1e-8);
        });
        Assert.Empty(plot.Jumps); // No invented outside-zero limits at either beam end.
    }

    [Fact]
    public void UdlQuarticHasAnalyticalValuesAndSubpixelChordError()
    {
        var solution = Solve(Beam(loads: [Uniform(0, L, -1000)]));
        var plot = Project(solution, ResultDiagramKind.TransverseDisplacement);
        foreach (var section in plot.Sections)
            for (int i = 1; i < section.Count; i++)
            {
                var a = section[i - 1]; var b = section[i];
                Assert.InRange(b.Screen.X - a.Screen.X, 0, 8);
                Close(-1000 * a.Position.Meters * (Math.Pow(L, 3) - 2 * L * Math.Pow(a.Position.Meters, 2)
                    + Math.Pow(a.Position.Meters, 3)) / (24 * EI) * 1000, a.Value, 1e-10);
                // Probe independently at a non-dyadic point, not just the sampler's probes.
                double x = a.Position.Meters + (b.Position.Meters - a.Position.Meters) * .37;
                double value = solution.EvaluateAt(M(x), EvaluationSide.Right).TransverseDisplacement.Meters * 1000;
                double chord = a.Screen.Y + (b.Screen.Y - a.Screen.Y) * .37;
                Assert.InRange(Math.Abs(plot.Scale.ToScreen(value) - chord), 0, .5);
            }
    }

    [Theory]
    [InlineData(ResultDiagramKind.ShearForce, false)]
    [InlineData(ResultDiagramKind.BendingMoment, true)]
    public void ConcentratedLoadsHaveExactOneSidedVerticalJumps(ResultDiagramKind kind, bool moment)
    {
        var solution = Solve(Beam(loads: moment ? [Couple(.75, 400)] : [Point(.75, -1000)]));
        var plot = Project(solution, kind);
        var jump = Assert.Single(plot.Jumps);
        Assert.Equal(M(.75), jump.Left.Position); Assert.Equal(jump.Left.Position, jump.Right.Position);
        Assert.Equal(EvaluationSide.Left, jump.Left.Side); Assert.Equal(EvaluationSide.Right, jump.Right.Side);
        Assert.Equal(jump.Left.Screen.X, jump.Right.Screen.X);
        Close(moment ? -400 : -1000, jump.Right.Value - jump.Left.Value, 1e-7);
        Assert.All(plot.Sections, section => Assert.False(section[0].Position.Meters < .75 && section[^1].Position.Meters > .75));
        Assert.Contains(plot.Sections, s => s[^1] == jump.Left);
        Assert.Contains(plot.Sections, s => s[0] == jump.Right);
    }

    [Theory]
    [InlineData(ResultDiagramKind.ShearForce)]
    [InlineData(ResultDiagramKind.BendingMoment)]
    public void InternalFixedSupportProducesReactionJumps(ResultDiagramKind kind)
    {
        var solution = Solve(Beam([SupportAt(1, SupportType.Fixed)], [Point(0, -1000), Point(L, -500)]));
        var jump = Assert.Single(Project(solution, kind).Jumps);
        Assert.Equal(M(1), jump.Left.Position);
        var node = At(solution, 1);
        Close(kind == ResultDiagramKind.ShearForce ? node.ReactionY!.Value.Newtons : -node.ReactionMoment!.Value.NewtonMeters,
            jump.Right.Value - jump.Left.Value, 1e-7);
    }

    [Theory]
    [InlineData(ResultDiagramKind.ShearForce)]
    [InlineData(ResultDiagramKind.BendingMoment)]
    public void OverlappingUdlBoundariesRemainContinuousWithoutJumpLines(ResultDiagramKind kind)
    {
        var plot = Project(Solve(Beam(loads: [Uniform(.2, 1.2, -1000), Uniform(.5, 1.8, -500)])), kind);
        Assert.Empty(plot.Jumps);
        for (int i = 1; i < plot.Sections.Count; i++)
            Close(plot.Sections[i - 1][^1].Value, plot.Sections[i][0].Value, 1e-7);
    }

    [Fact]
    public void OffNodeAnalyticalExtremumControlsScaleAndIsInsertedIntoCurve()
    {
        var solution = Solve(Beam(loads: [Point(.6, -1000)]));
        var extrema = solution.Extrema;
        var extremum = solution.Extrema.MinimumTransverseDisplacement;
        var plot = Project(solution, ResultDiagramKind.TransverseDisplacement);
        Assert.DoesNotContain(solution.Nodes, n => n.Position == extremum.Position);
        Assert.Equal(extremum.Value.Meters * 1000, plot.Scale.Minimum);
        Assert.Contains(plot.Sections.SelectMany(s => s), p => p.Position == extremum.Position);
        foreach (var p in plot.Sections.SelectMany(s => s).Where(p => p.Position == extremum.Position))
            Close(extremum.Value.Meters * 1000, p.Value, 1e-10);
        Assert.Same(extrema, solution.Extrema);
    }

    [Theory]
    [InlineData(-1000)]
    [InlineData(1000)]
    [InlineData(-1e-12)]
    [InlineData(1e-12)]
    [InlineData(0)]
    public void SignedAndTrueZeroFieldsHaveFiniteCorrectOrdinate(double force)
    {
        var solution = Solve(Cantilever(Point(L, force)));
        foreach (var kind in Enum.GetValues<ResultDiagramKind>())
        {
            var plot = Project(solution, kind);
            Assert.Equal(force == 0, plot.Scale.IsZero);
            Assert.All(plot.Sections.SelectMany(s => s), p =>
            {
                Assert.True(double.IsFinite(p.Screen.Y));
                Assert.InRange(p.Screen.Y, 36, 168);
                if (p.Value > 0) Assert.True(p.Screen.Y < plot.Scale.ZeroY);
                if (p.Value < 0) Assert.True(p.Screen.Y > plot.Scale.ZeroY);
                if (p.Value == 0) Assert.Equal(plot.Scale.ZeroY, p.Screen.Y);
            });
            if (force == 0)
            {
                Assert.Equal(102, plot.Scale.ZeroY);
                Assert.Equal(0, Assert.Single(plot.Ticks).Index);
            }
        }
    }

    [Fact]
    public void NormalizedScaleSupportsLargeMixedSignsAndSubnormalNonzeroValues()
    {
        var mixed = new ResultDiagramScale(-double.MaxValue, double.MaxValue, 36, 132);
        Assert.True(double.IsFinite(mixed.ToScreen(-double.MaxValue)));
        Assert.True(double.IsFinite(mixed.ToScreen(double.MaxValue)));
        Assert.Equal(102, mixed.ZeroY);
        var tiny = new ResultDiagramScale(0, double.Epsilon, 36, 132);
        Assert.False(tiny.IsZero);
        Assert.True(tiny.ToScreen(double.Epsilon) < tiny.ZeroY);
    }

    [Theory]
    [InlineData(ResultDiagramKind.TransverseDisplacement)]
    [InlineData(ResultDiagramKind.ShearForce)]
    [InlineData(ResultDiagramKind.BendingMoment)]
    public void MarkersRetainBothAnalyticalExtremaAndUseOnlyTheSharedTransformAndScale(ResultDiagramKind kind)
    {
        var solution = Solve(Beam(loads: [Point(.02, -1000), Couple(.03, 200), Uniform(.4, 1.5, -500)]));
        var layout = StationLayout.Compute(L, 72, 480,
            [new(.02, 24, 24), new(.03, 24, 24), new(.4, 16, 16), new(1.5, 16, 16)]);
        var plot = Project(solution, kind, layout);
        Assert.Equal(2, plot.Markers.Count);
        foreach (var marker in plot.Markers)
        {
            var expected = AnalyticalExtremum(solution, kind, marker.Kind == ResultDiagramExtremumKind.Minimum);
            Assert.Equal(expected.Value, marker.Value);
            Assert.Equal(expected.Position, marker.Position);
            Assert.Equal(expected.Side, marker.Side);
            Assert.Equal(layout.Transform.PhysicalToScreen(expected.Position.Meters), marker.Screen.X);
            Assert.Equal(plot.Scale.ToScreen(expected.Value), marker.Screen.Y);
        }
    }

    [Theory]
    [InlineData(ResultDiagramKind.ShearForce, false)]
    [InlineData(ResultDiagramKind.BendingMoment, true)]
    public void MarkersAtLoadJumpsUseTheSelectedLimitAndNeverTheJumpMidpoint(ResultDiagramKind kind, bool moment)
    {
        var solution = Solve(Beam(loads: moment ? [Couple(.75, 400)] : [Point(.75, -1000)]));
        var plot = Project(solution, kind);
        var jump = Assert.Single(plot.Jumps);
        var markersAtJump = plot.Markers.Where(m => m.Position == jump.Left.Position).ToArray();
        Assert.Equal(moment ? 2 : 1, markersAtJump.Length);
        foreach (var marker in markersAtJump)
        {
            var limit = marker.Side == EvaluationSide.Left ? jump.Left : jump.Right;
            Assert.Equal(limit.Value, marker.Value);
            Assert.Equal(limit.Screen, marker.Screen);
            Assert.NotEqual((jump.Left.Screen.Y + jump.Right.Screen.Y) / 2, marker.Screen.Y);
        }
    }

    [Theory]
    [InlineData(ResultDiagramKind.ShearForce)]
    [InlineData(ResultDiagramKind.BendingMoment)]
    public void MarkersAtInternalSupportJumpsAlsoPreserveAnalyticalSides(ResultDiagramKind kind)
    {
        var solution = Solve(Beam([SupportAt(1, SupportType.Fixed)], [Point(0, -1000), Point(L, -500)]));
        var plot = Project(solution, kind);
        var jump = Assert.Single(plot.Jumps);
        var marker = Assert.Single(plot.Markers, m => m.Position == jump.Left.Position);
        var expected = marker.Side == EvaluationSide.Left ? jump.Left : jump.Right;
        Assert.Equal(expected.Value, marker.Value);
        Assert.Equal(expected.Screen, marker.Screen);
    }

    [Fact]
    public void RepeatedEqualInteriorExtremaKeepTheSolversFirstRepresentative()
    {
        // Same manufactured quartic as SolverExtremaTests: equal minima at .1L/.9L.
        var beam = Cantilever(Uniform(0, L, 24 * EI / Math.Pow(L, 4)));
        var solution = new BeamSolution(beam,
            [new(0, M(0), default, default, -.18 / L, null, null, null),
             new(1, M(L), default, default, .18 / L, null, null, null)], SolverModel.Create(beam));
        var minimum = Assert.Single(Project(solution, ResultDiagramKind.TransverseDisplacement).Markers,
            m => m.Kind == ResultDiagramExtremumKind.Minimum);
        Assert.Equal(solution.Extrema.MinimumTransverseDisplacement.Position, minimum.Position);
        Close(.1 * L, minimum.Position.Meters, 1e-12);
        Assert.Null(minimum.Side);
    }

    [Theory]
    [InlineData(ResultDiagramKind.TransverseDisplacement)]
    [InlineData(ResultDiagramKind.ShearForce)]
    [InlineData(ResultDiagramKind.BendingMoment)]
    public void TrueZeroHasOneCombinedMarkerAtTheFirstEndpoint(ResultDiagramKind kind)
    {
        var solution = Solve(Cantilever(Point(.5, 0), Couple(1.5, 0)));
        var plot = Project(solution, kind);
        var marker = Assert.Single(plot.Markers);
        Assert.Equal(ResultDiagramExtremumKind.MinimumAndMaximum, marker.Kind);
        Assert.Equal(0, marker.Value);
        Assert.Equal(M(0), marker.Position);
        Assert.Equal(EvaluationSide.Right, marker.Side);
        Assert.Equal(plot.Scale.ZeroY, marker.Screen.Y);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(-1e-100)]
    public void ExactlyConstantDisplacementCombinesExtremaWithoutTreatingSmallValuesAsZero(double value)
    {
        var beam = Cantilever();
        var displacement = Displacement.FromMeters(value);
        var solution = new BeamSolution(beam,
            [new(0, M(0), default, displacement, 0, null, null, null),
             new(1, M(L), default, displacement, 0, null, null, null)], SolverModel.Create(beam));
        var marker = Assert.Single(Project(solution, ResultDiagramKind.TransverseDisplacement).Markers);
        Assert.Equal(ResultDiagramExtremumKind.MinimumAndMaximum, marker.Kind);
        Assert.Equal(value * 1000, marker.Value);
        Assert.Equal(M(0), marker.Position);
    }

    [Theory]
    [InlineData(ResultDiagramKind.TransverseDisplacement)]
    [InlineData(ResultDiagramKind.BendingMoment)]
    public void ZeroRemainsAMarkedExtremumInALoadedModel(ResultDiagramKind kind)
    {
        // Exact nodal representation of the tip-force cubic, with a true zero
        // moment at the free end rather than a solver roundoff residual.
        var beam = Cantilever(Point(L, -6 * EI));
        var solution = new BeamSolution(beam,
            [new(0, M(0), default, default, 0, null, null, null),
             new(1, M(L), default, Displacement.FromMeters(-16), -12, null, null, null)], SolverModel.Create(beam));
        var plot = Project(solution, kind);
        var marker = Assert.Single(plot.Markers, m => m.Value == 0);
        Assert.Equal(ResultDiagramExtremumKind.Maximum, marker.Kind);
        Assert.Equal(kind == ResultDiagramKind.TransverseDisplacement ? M(0) : M(L), marker.Position);
    }

    [Fact]
    public void OffRasterMarkerIsExactAcrossDifferentSamplingDensities()
    {
        var solution = Solve(Beam(loads: [Point(.6, -1000)]));
        var analytical = solution.Extrema.MinimumTransverseDisplacement;
        Assert.DoesNotContain(solution.Nodes, n => n.Position == analytical.Position);
        int previousCount = 0;
        foreach (double width in new[] { 400d, 1200d })
        {
            var layout = StationLayout.Compute(L, 72, width, [new(.6, 24, 24)]);
            var plot = Project(solution, ResultDiagramKind.TransverseDisplacement, layout);
            var marker = Assert.Single(plot.Markers, m => m.Kind == ResultDiagramExtremumKind.Minimum);
            Assert.Equal(analytical.Position, marker.Position);
            Assert.Equal(analytical.Side, marker.Side);
            Assert.Equal(analytical.Value.Meters * 1000, marker.Value);
            Assert.Equal(layout.Transform.PhysicalToScreen(analytical.Position.Meters), marker.Screen.X);
            Assert.Equal(plot.Scale.ToScreen(analytical.Value.Meters * 1000), marker.Screen.Y);
            int count = plot.Sections.Sum(s => s.Count);
            Assert.True(count > previousCount);
            previousCount = count;
        }
    }

    private static (double Value, Length Position, EvaluationSide? Side) AnalyticalExtremum(
        BeamSolution solution, ResultDiagramKind kind, bool minimum)
    {
        var extrema = solution.Extrema;
        if (kind == ResultDiagramKind.TransverseDisplacement)
        {
            var e = minimum ? extrema.MinimumTransverseDisplacement : extrema.MaximumTransverseDisplacement;
            return (e.Value.Meters * 1000, e.Position, e.Side);
        }
        if (kind == ResultDiagramKind.ShearForce)
        {
            var e = minimum ? extrema.MinimumShearForce : extrema.MaximumShearForce;
            return (e.Value.Newtons, e.Position, e.Side);
        }
        var moment = minimum ? extrema.MinimumBendingMoment : extrema.MaximumBendingMoment;
        return (moment.Value.NewtonMeters, moment.Position, moment.Side);
    }
}
