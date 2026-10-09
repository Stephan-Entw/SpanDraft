using SpanDraft.Core.Beams;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.State;
using SpanDraft.Engineering;
using SpanDraft.Solver;
using Xunit;
using static SpanDraft.Tests.SolverTestSupport;

namespace SpanDraft.Tests;

public sealed class LegacySectionAxisRegressionTests
{
    private static Section Profile(int profile) => profile switch
    {
        0 => new RectangleSection(M(.04), M(.08)),
        1 => new RectangularHollowSection(M(.04), M(.08), M(.004)),
        2 => new CircleSection(M(.06)),
        3 => new CircularHollowSection(M(.06), M(.004)),
        _ => new CustomSection(Area.FromSquareMeters(.003), SecondMomentOfArea.FromMetersToTheFourth(8e-6),
            SectionModulus.FromCubicMeters(.0002))
    };

    // Captured on the validated pre-integration implementation (67fb4e3), with end force and full UDL.
    [Theory]
    [InlineData(0, -.010230654761904778, -.007440476190476204, -.0005269422903878355,
        -.0027269003441220273, 70312500.00000007, 3.342222222222219)]
    [InlineData(1, -.024545716799195684, -.01785143039941504, -.0012642569347116961,
        -.006542467236377218, 168696017.2744722, 1.393038222222222)]
    [InlineData(2, -.02744588475815559, -.019960643460476796, -.0014136336053520032,
        -.007315484134058845, 141471060.52612922, 1.6611171155856028)]
    [InlineData(3, -.06297352773212586, -.0457989292597279, -.003243527976384386,
        -.016785097184858988, 324599911.12832147, .7239681587808549)]
    [InlineData(4, -.0021825396825396826, -.0015873015873015873, -.00011241435528273811,
        -.0005817387400793651, 15000000.000000002, 15.666666666666664)]
    public void EveryLegacyProfilePreservesFrozenSolverAndEngineeringResults(int profile,
        double endW, double endTheta, double interiorW, double interiorTheta, double stress, double safety)
    {
        var section = Profile(profile);
        var axis = Assert.Single(((ISectionDefinition)section).Axes);
        Assert.Equal(SectionAxisDesignation.Y, axis.AxisDesignation);
        Assert.Equal(section.SecondMomentOfArea, axis.SecondMomentOfArea);
        Assert.Equal(section.SectionModulus, axis.PositiveSectionModulus);
        Assert.Equal(section.SectionModulus, axis.NegativeSectionModulus);
        Assert.Same(axis, section.GetAxis(SectionAxisDesignation.Y));
        Assert.Throws<NotSupportedException>(() => ((IList<SectionAxisProperties>)section.Axes).Clear());
        Assert.Throws<KeyNotFoundException>(() => section.GetAxis(SectionAxisDesignation.Z));
        Assert.Throws<ArgumentOutOfRangeException>(() => section.GetAxis((SectionAxisDesignation)999));
        Assert.Equal("bendingAxis", Assert.Throws<ArgumentException>(() => new BeamModel(M(L),
            Beam().Material, section, SectionAxisDesignation.Z, [], [])).ParamName);

        var beam = Beam([SupportAt(0, SpanDraft.Core.Supports.SupportType.Fixed)],
            [Point(L, -1000), Uniform(0, L, -500)], section: section);
        Assert.Equal(SectionAxisDesignation.Y, beam.BendingAxis);
        Assert.Same(section, beam.Section);
        var solution = Solve(beam);
        DisplacementClose(endW, At(solution, L).TransverseDisplacement);
        RotationClose(endTheta, At(solution, L).RotationRadians);
        var interior = solution.EvaluateAt(M(.37), EvaluationSide.Left);
        DisplacementClose(interiorW, interior.TransverseDisplacement);
        RotationClose(interiorTheta, interior.RotationRadians);
        ForceClose(2000, At(solution, 0).ReactionY);
        MomentClose(3000, At(solution, 0).ReactionMoment);
        Close(-3000, solution.Extrema.MinimumBendingMoment.Value.NewtonMeters, 1e-7);
        var engineering = BeamEngineeringAnalysis.Analyze(solution);
        NumericAssert.Close(stress, engineering.MaximumBendingStress.Pascals);
        NumericAssert.Close(safety, engineering.SafetyFactor);

        var explicitBeam = new BeamModel(beam.Length, beam.Material, section, SectionAxisDesignation.Y, beam.Supports, beam.Loads);
        var explicitSolution = Solve(explicitBeam);
        SameExtremum(solution.Extrema.MinimumTransverseDisplacement, explicitSolution.Extrema.MinimumTransverseDisplacement);
        SameExtremum(solution.Extrema.MaximumTransverseDisplacement, explicitSolution.Extrema.MaximumTransverseDisplacement);
        SameExtremum(solution.Extrema.MinimumShearForce, explicitSolution.Extrema.MinimumShearForce);
        SameExtremum(solution.Extrema.MaximumShearForce, explicitSolution.Extrema.MaximumShearForce);
        SameExtremum(solution.Extrema.MinimumBendingMoment, explicitSolution.Extrema.MinimumBendingMoment);
        SameExtremum(solution.Extrema.MaximumBendingMoment, explicitSolution.Extrema.MaximumBendingMoment);
        foreach (var pair in solution.Nodes.Zip(explicitSolution.Nodes))
        {
            Assert.Equal(pair.First.TransverseDisplacement, pair.Second.TransverseDisplacement);
            Assert.Equal(pair.First.RotationRadians, pair.Second.RotationRadians);
            Assert.Equal(pair.First.ReactionY, pair.Second.ReactionY);
            Assert.Equal(pair.First.ReactionMoment, pair.Second.ReactionMoment);
        }
        Assert.Equal(engineering.MaximumBendingStress, BeamEngineeringAnalysis.Analyze(explicitSolution).MaximumBendingStress);
    }

    private static void SameExtremum<T>(BeamExtremum<T> expected, BeamExtremum<T> actual)
    {
        Assert.Equal(expected.Value, actual.Value);
        Assert.Equal(expected.Position, actual.Position);
        Assert.Equal(expected.Side, actual.Side);
    }

    [Fact]
    public void ExistingEditorDocumentKeepsItsLegacySectionAndImplicitYAxis()
    {
        var section = Profile(0);
        var document = new EditorDocument(M(L), Beam().Material, section);
        var beam = document.ToBeamModel();
        Assert.Same(section, document.Section);
        Assert.Same(section, beam.Section);
        Assert.Equal(SectionAxisDesignation.Y, beam.BendingAxis);
        Assert.Same(section.GetAxis(SectionAxisDesignation.Y), beam.BendingAxisProperties);
    }
}
