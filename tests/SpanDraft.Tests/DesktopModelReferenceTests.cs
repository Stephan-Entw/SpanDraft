using SpanDraft.Analysis;
using SpanDraft.Core.Beams;
using SpanDraft.Core.Loads;
using SpanDraft.Core.Materials;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Presentation;
using Xunit;
using static SpanDraft.Tests.SolverTestSupport;

namespace SpanDraft.Tests;

public sealed class DesktopModelReferenceTests
{
    private static BeamAnalysisResult Analyze(BeamModel beam) => Assert.IsType<BeamAnalysisResult>(BeamAnalysis.Analyze(beam).Result);

    [Fact]
    public void OpposingPointForcesAreNotCancelled()
    {
        var references = ModelReferenceValues.FromAnalysis(Analyze(Beam(loads: [Point(0.5, 400), Point(0.5, -400)])));
        Assert.Equal(400, references.ForceReferenceNewtons);
        Assert.Equal(0, references.MomentReferenceNewtonMeters);
    }

    [Fact]
    public void IndividualDistributedLoadResultantsUseTheirActualLengths()
    {
        var references = ModelReferenceValues.FromAnalysis(Analyze(Beam(loads:
            [Uniform(0.25, 1.75, 120), Uniform(0.25, 1.75, -120)])));
        Assert.Equal(180, references.ForceReferenceNewtons);
        Assert.Equal(0, references.MomentReferenceNewtonMeters);
    }

    [Fact]
    public void OpposingPointMomentsStillContributeEquivalentForce()
    {
        var references = ModelReferenceValues.FromAnalysis(Analyze(Beam(loads: [Couple(0.5, 400), Couple(0.5, -400)])));
        Assert.Equal(200, references.ForceReferenceNewtons);
        Assert.Equal(0, references.MomentReferenceNewtonMeters);
    }

    [Fact]
    public void LargestAmongDifferentIndividualLoadKindsWins()
    {
        BeamLoad[] loads = [Point(0.5, 100), Point(0.5, -100),
            Uniform(0.25, 1.75, 120), Uniform(0.25, 1.75, -120), Couple(1, 600), Couple(1, -600)];
        Assert.Equal(300, ModelReferenceValues.FromAnalysis(Analyze(Beam(loads: loads))).ForceReferenceNewtons);
    }

    [Fact]
    public void ReactionAndShearCanExceedIndividualLoads()
    {
        var beam = Beam([SupportAt(0, SupportType.Pinned), SupportAt(0.5, SupportType.Roller)], [Point(2, -1000)]);
        var analysis = Analyze(beam);
        var references = ModelReferenceValues.FromAnalysis(analysis);
        Close(4000, references.ForceReferenceNewtons, 1e-7);
        Close(1500, references.MomentReferenceNewtonMeters, 1e-7);
    }

    [Fact]
    public void MomentReferenceUsesExistingInteriorAnalyticalExtremum()
    {
        var analysis = Analyze(Beam(loads: [Uniform(0, 2, -1000)]));
        Assert.DoesNotContain(analysis.Solution.Nodes, node => node.Position.Meters == 1);
        Assert.Equal(1, analysis.Solution.Extrema.MaximumBendingMoment.Position.Meters);
        var references = ModelReferenceValues.FromAnalysis(analysis);
        Close(500, references.MomentReferenceNewtonMeters, 1e-7);
        Close(2000, references.ForceReferenceNewtons, 1e-7);
        Assert.Equal(2, references.BeamLengthMeters);
        Assert.Equal(235e6, references.YieldStrengthPascals);
        Assert.Same(analysis.Beam, analysis.Solution.Beam);
    }

    [Fact]
    public void NegativeMomentExtremumAndSelectedMaterialAreUsed()
    {
        var material = new Material("Test", Pressure.FromPascals(E), Pressure.FromMegapascals(355));
        var analysis = Analyze(Beam([SupportAt(0, SupportType.Fixed)], [Point(2, -1000)], material));
        var references = ModelReferenceValues.FromAnalysis(analysis);
        Assert.True(analysis.Solution.Extrema.MinimumBendingMoment.Value.NewtonMeters < 0);
        Close(2000, references.MomentReferenceNewtonMeters, 1e-7);
        Assert.Equal(355e6, references.YieldStrengthPascals);
    }

    [Fact]
    public void ReferencesCanBeReusedWithoutChangingExistingAnalysis()
    {
        var analysis = Analyze(Beam(loads: [Point(1, -1000)]));
        var solution = analysis.Solution;
        var extrema = solution.Extrema;
        var engineering = analysis.Engineering;
        var references = ModelReferenceValues.FromAnalysis(analysis);
        foreach (var profile in new[] { UnitProfile.Default, UnitProfile.StructuralEngineering, UnitProfile.UnitedStates })
        foreach (var mode in Enum.GetValues<PresentationMode>())
            QuantityFormatter.Format(engineering.BendingMomentMagnitude.NewtonMeters, QuantityKind.Moment,
                profile, mode, references);
        Assert.Same(solution, analysis.Solution);
        Assert.Same(extrema, analysis.Solution.Extrema);
        Assert.Same(engineering, analysis.Engineering);
    }

    [Fact]
    public void UnloadedModelHasZeroForceAndMomentReferences()
    {
        var references = ModelReferenceValues.FromAnalysis(Analyze(Beam()));
        Assert.Equal(0, references.ForceReferenceNewtons);
        Assert.Equal(0, references.MomentReferenceNewtonMeters);
    }

    [Fact]
    public void InvalidExplicitReferencesAreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => ModelReferenceValues.FromAnalysis(null!));
        foreach (double invalid in new[] { -1, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ModelReferenceValues(invalid, 0, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ModelReferenceValues(0, invalid, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ModelReferenceValues(0, 0, invalid, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ModelReferenceValues(0, 0, 0, invalid));
        }
    }
}
