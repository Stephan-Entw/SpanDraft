using SpanDraft.Analysis;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.State;
using SpanDraft.Solver;
using Xunit;

namespace SpanDraft.Tests;

public sealed class DesktopMomentReleaseOverviewTests
{
    private static Length M(double meters) => Length.FromMeters(meters);

    private static EditorDocument Gerber(double load = -10000) => new EditorDocument(M(8), ProjectTemplates.Material, ProjectTemplates.Section,
        new EditorSupport[] { new(Guid.NewGuid(), M(0), SupportType.Pinned, "A"), new(Guid.NewGuid(), M(4), SupportType.Roller, "B"),
            new(Guid.NewGuid(), M(8), SupportType.Roller, "C") },
        [new EditorPointForce(Guid.NewGuid(), M(7), Force.FromNewtons(load), "F1")])
        .WithMomentReleases([new(Guid.NewGuid(), M(6), "G1")]);

    private static ProjectOverviewState Overview(EditorDocument document)
    {
        var outcome = BeamAnalysis.Analyze(document.ToBeamModel());
        return ProjectOverviewState.From(document, AnalysisPresentationState.FromOutcome(outcome, hasMomentReleases: true));
    }

    [Fact]
    public void TheModelRowNamesTheHingeAndItsPositionInTheDisplayUnitAndTheResultRowsReadTheSolver()
    {
        var document = Gerber();
        var overview = Overview(document);
        var row = Assert.Single(overview.MomentReleases);
        Assert.Equal((document.MomentReleases[0].Id, "G1", "6000"), (row.Id, row.Name, new string(row.Position.Where(char.IsDigit).ToArray())));
        Assert.True(overview.HasMomentReleases && overview.HasMomentReleaseResults);
        var solution = BeamAnalysis.Analyze(document.ToBeamModel()).Result!.Solution;
        var node = solution.Nodes.Single(n => n.Position == M(6));
        var result = Assert.Single(overview.MomentReleaseResults);
        var options = overview.Analysis.Options;
        Assert.Equal(document.MomentReleases[0].Id, result.Id);
        Assert.Equal("rad", UnitProfile.Default[QuantityKind.Rotation].Symbol);
        // The left limit is the hinge's own rotation, the right one the node's: the two cells carry exactly these values.
        Assert.Equal(QuantityFormatter.FormatResultNumber(node.RotationLeftRadians!.Value, QuantityKind.Rotation, options), result.RotationLeft);
        Assert.Equal(QuantityFormatter.FormatResultNumber(node.RotationRadians, QuantityKind.Rotation, options), result.RotationRight);
        Assert.NotEqual(result.RotationLeft, result.RotationRight);
        Assert.Equal(QuantityFormatter.FormatResultNumber(5000, QuantityKind.TransverseForce, options), result.Shear);
    }

    [Fact]
    public void AForceOnTheHingeMakesTheShearJumpAndTheCellShowsBothSides()
    {
        var document = new EditorDocument(M(6), ProjectTemplates.Material, ProjectTemplates.Section,
            new EditorSupport[] { new(Guid.NewGuid(), M(0), SupportType.Pinned, "A"), new(Guid.NewGuid(), M(3), SupportType.Roller, "B"),
                new(Guid.NewGuid(), M(6), SupportType.Roller, "C") },
            [new EditorPointForce(Guid.NewGuid(), M(4.5), Force.FromNewtons(-3000), "F1")])
            .WithMomentReleases([new(Guid.NewGuid(), M(4.5), "G1")]);
        var result = Assert.Single(Overview(document).MomentReleaseResults);
        Assert.Contains(" / ", result.Shear);
    }

    [Fact]
    public void WithoutAnAnalysisOnlyTheModelRowsExist()
    {
        var document = new EditorDocument(M(4), ProjectTemplates.Material, ProjectTemplates.Section,
            new EditorSupport[] { new(Guid.NewGuid(), M(0), SupportType.Pinned, "A") }).WithMomentReleases([new(Guid.NewGuid(), M(2), "G1")]);
        var overview = Overview(document);
        Assert.False(overview.Analysis.IsSuccess);
        Assert.True(overview.HasMomentReleases);
        Assert.False(overview.HasMomentReleaseResults);
    }
}
