using System.Collections.Immutable;
using SpanDraft.Core.Beams;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Validation;
using SpanDraft.Solver;
using SpanDraft.Solver.Kernel;
using Xunit;
using static SpanDraft.Tests.SolverTestSupport;

namespace SpanDraft.Tests;

public sealed class SolverKernelIntegrationTests
{
    [Theory]
    [InlineData("cantilever")]
    [InlineData("simple")]
    [InlineData("udl")]
    [InlineData("cantilever-udl")]
    [InlineData("moment")]
    [InlineData("overlap")]
    [InlineData("indeterminate")]
    [InlineData("load-on-support")]
    [InlineData("coincident-fixed")]
    [InlineData("coincident-pinned")]
    [InlineData("near-supports")]
    [InlineData("adjacent-events")]
    [InlineData("unloaded")]
    [InlineData("signed-zero")]
    [InlineData("selected-axis")]
    public void EndToEndPreservesKernelNodesFieldsReactionsAndExtremaExactly(string name)
    {
        BeamModel beam = Model(name);
        BendingResult kernel = SolverKernel.Solve(BeamModelAdapter.Adapt(beam));
        BeamSolution solution = KernelBeamSolver.Solve(beam);
        Assert.Same(beam, solution.Beam);
        Assert.Equal(kernel.Stations.Length, solution.Nodes.Count);
        for (int i = 0; i < kernel.Stations.Length; i++)
        {
            BendingStation station = kernel.Stations[i];
            BeamNodeResult node = solution.Nodes[i];
            BendingField field = (station.Position == beam.Length.Meters ? station.Left : station.Right)!;
            Assert.Equal(i, node.NodeIndex);
            Exact(station.Position, node.Position.Meters);
            Exact(0, node.AxialDisplacement.Meters);
            Exact(field.Bending[0].Value, node.TransverseDisplacement.Meters);
            Exact(field.Bending[1].Value, node.RotationRadians);
            Reaction(RestrainedDirections.U, node.ReactionX?.Newtons);
            Reaction(RestrainedDirections.W, node.ReactionY?.Newtons);
            Reaction(RestrainedDirections.Theta, node.ReactionMoment?.NewtonMeters);

            void Reaction(RestrainedDirections direction, double? actual)
            {
                BendingReaction[] expected = kernel.Reactions.Where(r =>
                    r.Position == station.Position && r.Direction == direction).ToArray();
                if (expected.Length == 0) Assert.Null(actual);
                else
                {
                    Assert.True(actual.HasValue);
                    Exact(Assert.Single(expected).Output.Value, actual.Value);
                    if (direction == RestrainedDirections.U) Exact(0, actual.Value);
                }
            }
        }

        foreach (double x in QueryPositions(kernel))
            foreach (EvaluationSide side in Enum.GetValues<EvaluationSide>())
            {
                BendingField expected = kernel.EvaluateAt(x, side == EvaluationSide.Left ? FieldSide.Left : FieldSide.Right);
                BeamSectionResult actual = solution.EvaluateAt(M(x), side);
                Exact(x, actual.Position.Meters);
                Exact(0, actual.AxialDisplacement.Meters);
                Exact(0, actual.AxialForce.Newtons);
                foreach (var pair in expected.Bending.Zip(Values(actual)))
                    Exact(pair.First.Value, pair.Second);
            }

        BendingExtremum[] expectedExtrema = [kernel.Extrema.W.Minimum, kernel.Extrema.W.Maximum,
            kernel.Extrema.V.Minimum, kernel.Extrema.V.Maximum, kernel.Extrema.M.Minimum, kernel.Extrema.M.Maximum];
        foreach (var (expected, actual) in expectedExtrema.Zip(Extrema(solution)))
        {
            Exact(expected.Output.Value, actual.Value);
            Exact(expected.Position, actual.Position);
            Assert.Equal(PublicSide(expected.Side), actual.Side);
            foreach (EvaluationSide side in actual.Side is { } s ? [s] : Enum.GetValues<EvaluationSide>())
                Exact(actual.Value, Values(solution.EvaluateAt(M(actual.Position), side))[actual.Component]);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void MissingRequiredStationSideIsAnInternalInconsistency(int index)
    {
        BeamModel beam = Model("simple");
        BendingResult result = SolverKernel.Solve(BeamModelAdapter.Adapt(beam));
        BendingStation station = result.Stations[index];
        station = index == 2 ? station with { Left = null, Right = station.Left } :
            station with { Right = null, Left = station.Right };
        result = result with { Stations = result.Stations.SetItem(index, station) };
        Assert.Throws<InvalidOperationException>(() => new BeamSolution(beam, result));
    }

    [Fact]
    public void NodesUseRightExceptAtRightEndAndReactionsAreCopiedRatherThanReconstructed()
    {
        BeamModel beam = Cantilever(Point(1, -1000));
        BendingResult result = SolverKernel.Solve(BeamModelAdapter.Adapt(beam));
        BendingStation middle = result.Stations[1], end = result.Stations[^1];
        BendingField right = middle.Right! with
        {
            Bending = middle.Right!.Bending.SetItem(0, new(123, 0)).SetItem(1, new(456, 0))
        };
        BendingField left = end.Left! with
        {
            Bending = end.Left!.Bending.SetItem(0, new(789, 0)).SetItem(1, new(321, 0))
        };
        result = result with
        {
            Stations = result.Stations.SetItem(1, middle with { Right = right })
                .SetItem(result.Stations.Length - 1, end with { Left = left, Right = right }),
            Reactions = result.Reactions.Select(r => r.Direction == RestrainedDirections.U ? r :
                r with { Output = new(17, 0) }).ToImmutableArray()
        };
        var solution = new BeamSolution(beam, result);
        Exact(123, solution.Nodes[1].TransverseDisplacement.Meters);
        Exact(456, solution.Nodes[1].RotationRadians);
        Exact(789, solution.Nodes[^1].TransverseDisplacement.Meters);
        Exact(321, solution.Nodes[^1].RotationRadians);
        foreach (BendingReaction reaction in result.Reactions)
        {
            BeamNodeResult node = At(solution, reaction.Position);
            Exact(reaction.Direction == RestrainedDirections.U ? 0 : 17, reaction.Direction switch
            {
                RestrainedDirections.U => node.ReactionX!.Value.Newtons,
                RestrainedDirections.W => node.ReactionY!.Value.Newtons,
                _ => node.ReactionMoment!.Value.NewtonMeters
            });
        }
    }

    [Theory]
    [InlineData((int)RestrainedDirections.U)]
    [InlineData((int)RestrainedDirections.W)]
    [InlineData((int)RestrainedDirections.Theta)]
    public void DuplicateReactionKeyIsAnInternalInconsistency(int direction)
    {
        BeamModel beam = Model("cantilever");
        BendingResult result = SolverKernel.Solve(BeamModelAdapter.Adapt(beam));
        BendingReaction reaction = Assert.Single(result.Reactions, r => r.Direction == (RestrainedDirections)direction);
        result = result with { Reactions = result.Reactions.Add(reaction with { Output = new(123, 0) }) };
        Assert.Throws<InvalidOperationException>(() => new BeamSolution(beam, result));
    }

    [Fact]
    public void InternalEntryValidatesBeforeAdaptingAndPreservesAllModelErrors()
    {
        Assert.True(typeof(KernelBeamSolver).IsNotPublic);
        Assert.Throws<ArgumentNullException>(() => KernelBeamSolver.Solve(null!));
        foreach (BeamModel beam in new[]
        {
            Beam(supports: []),
            Beam([SupportAt(3, SupportType.Fixed), SupportAt(3, SupportType.Fixed)],
                [Point(3, -1), Couple(3, 1), Uniform(0, 3, -1)])
        })
        {
            BeamSolverException error = Assert.Throws<BeamSolverException>(() => KernelBeamSolver.Solve(beam));
            Assert.Equal(SolverErrorCode.InvalidModel, error.Code);
            Assert.Equal(BeamModelValidator.Validate(beam), error.ValidationErrors);
        }
    }

    [Fact]
    public void ExistingPublicQueryErrorsArePreserved()
    {
        BeamModel beam = Model("simple");
        BeamSolution old = Solve(beam), current = KernelBeamSolver.Solve(beam);
        foreach (BeamSolution solution in new[] { old, current })
        {
            Assert.Equal("side", Assert.Throws<ArgumentOutOfRangeException>(() =>
                solution.EvaluateAt(M(1), (EvaluationSide)99)).ParamName);
            Assert.Equal("position", Assert.Throws<ArgumentOutOfRangeException>(() =>
                solution.EvaluateAt(M(Math.BitIncrement(L)), EvaluationSide.Left)).ParamName);
        }
    }

    [Theory]
    [InlineData("cantilever")]
    [InlineData("udl")]
    [InlineData("moment")]
    [InlineData("overlap")]
    [InlineData("indeterminate")]
    public void RepresentativeModelsAgreeWithPublicProductionSolver(string name)
    {
        BeamModel beam = Model(name);
        BeamSolution old = Solve(beam), current = KernelBeamSolver.Solve(beam);
        SameResults(old, current);
        foreach (double x in old.Nodes.Select(n => n.Position.Meters).Concat([0.125, 0.625, 1.375, 1.875]))
            foreach (EvaluationSide side in Enum.GetValues<EvaluationSide>())
            {
                BeamSectionResult a = old.EvaluateAt(M(x), side), b = current.EvaluateAt(M(x), side);
                DisplacementClose(a.AxialDisplacement.Meters, b.AxialDisplacement);
                DisplacementClose(a.TransverseDisplacement.Meters, b.TransverseDisplacement);
                RotationClose(a.RotationRadians, b.RotationRadians);
                Close(a.AxialForce.Newtons, b.AxialForce.Newtons, 1e-7);
                Close(a.ShearForce.Newtons, b.ShearForce.Newtons, 1e-7);
                Close(a.BendingMoment.NewtonMeters, b.BendingMoment.NewtonMeters, 1e-7);
            }
        foreach (var (a, b) in Extrema(old).Zip(Extrema(current)))
        {
            Close(a.Value, b.Value, a.Component == 0 ? 1e-12 : 1e-7);
            Close(a.Position, b.Position, 1e-10);
            Assert.Equal(a.Side, b.Side);
        }
    }

    private static BeamModel Model(string name) => name switch
    {
        "cantilever" => Cantilever(Point(L, -1000)),
        "simple" => Beam(loads: [Point(1, -1000)]),
        "udl" => Beam(loads: [Uniform(0, L, -1000)]),
        "cantilever-udl" => Cantilever(Uniform(0, L, -1000)),
        "moment" => Beam(loads: [Couple(0.75, 400)]),
        "overlap" => Cantilever(Uniform(0.25, 1.25, -400), Uniform(0.75, 1.75, -600)),
        "indeterminate" => Beam([SupportAt(0, SupportType.Fixed), SupportAt(1, SupportType.Roller),
            SupportAt(L, SupportType.Roller)], [Point(0.75, -500), Uniform(0, L, -300)]),
        "load-on-support" => Beam([SupportAt(0, SupportType.Pinned), SupportAt(1, SupportType.Roller),
            SupportAt(L, SupportType.Roller)], [Point(1, -1000), Couple(1, 100)]),
        "coincident-fixed" => Beam([SupportAt(0, SupportType.Fixed), SupportAt(0, SupportType.Pinned),
            SupportAt(0, SupportType.Roller)], [Point(L, -1000)]),
        "coincident-pinned" => Beam([SupportAt(0, SupportType.Pinned), SupportAt(0, SupportType.Roller),
            SupportAt(L, SupportType.Roller)], [Point(0.75, -1000)]),
        "near-supports" => Beam([SupportAt(0.75, SupportType.Pinned), SupportAt(0.75 + 1e-6, SupportType.Roller)],
            [Point(0.75 + 0.5e-6, -1000)]),
        "adjacent-events" => Cantilever(Point(1, -1000), Couple(Math.BitIncrement(1), 200)),
        "unloaded" => Cantilever(),
        "signed-zero" => Cantilever(Point(-0d, -0d), Point(1, 0), Couple(Math.BitIncrement(1), -0d),
            Uniform(0.25, 1.75, -0d)),
        "selected-axis" => SectionAxisTestSupport.AxisBeam(
            SectionAxisTestSupport.Manual(SectionAxisDesignation.Y, SectionAxisDesignation.Z),
            SectionAxisDesignation.Z, [Point(1, -1000), Uniform(0.25, 1.75, -300)]),
        _ => throw new ArgumentOutOfRangeException(nameof(name))
    };

    private static IEnumerable<double> QueryPositions(BendingResult result)
    {
        foreach (BendingStation station in result.Stations)
        {
            yield return station.Position;
            double before = Math.BitDecrement(station.Position), after = Math.BitIncrement(station.Position);
            if (before >= 0) yield return before;
            if (after <= L) yield return after;
        }
        foreach (BendingPiece piece in result.Pieces)
        {
            double middle = piece.Start + (piece.End - piece.Start) / 2;
            if (middle > piece.Start && middle < piece.End) yield return middle;
        }
    }

    private static double[] Values(BeamSectionResult value) =>
        [value.TransverseDisplacement.Meters, value.RotationRadians, value.BendingMoment.NewtonMeters, value.ShearForce.Newtons];

    private static (double Value, double Position, EvaluationSide? Side, int Component)[] Extrema(BeamSolution solution) =>
    [
        (solution.Extrema.MinimumTransverseDisplacement.Value.Meters, solution.Extrema.MinimumTransverseDisplacement.Position.Meters, solution.Extrema.MinimumTransverseDisplacement.Side, 0),
        (solution.Extrema.MaximumTransverseDisplacement.Value.Meters, solution.Extrema.MaximumTransverseDisplacement.Position.Meters, solution.Extrema.MaximumTransverseDisplacement.Side, 0),
        (solution.Extrema.MinimumShearForce.Value.Newtons, solution.Extrema.MinimumShearForce.Position.Meters, solution.Extrema.MinimumShearForce.Side, 3),
        (solution.Extrema.MaximumShearForce.Value.Newtons, solution.Extrema.MaximumShearForce.Position.Meters, solution.Extrema.MaximumShearForce.Side, 3),
        (solution.Extrema.MinimumBendingMoment.Value.NewtonMeters, solution.Extrema.MinimumBendingMoment.Position.Meters, solution.Extrema.MinimumBendingMoment.Side, 2),
        (solution.Extrema.MaximumBendingMoment.Value.NewtonMeters, solution.Extrema.MaximumBendingMoment.Position.Meters, solution.Extrema.MaximumBendingMoment.Side, 2)
    ];

    private static EvaluationSide? PublicSide(FieldSide side) => side switch
    {
        FieldSide.Left => EvaluationSide.Left,
        FieldSide.Right => EvaluationSide.Right,
        _ => null
    };

    private static void Exact(double expected, double actual) =>
        Assert.Equal(BitConverter.DoubleToUInt64Bits(expected), BitConverter.DoubleToUInt64Bits(actual));
}
