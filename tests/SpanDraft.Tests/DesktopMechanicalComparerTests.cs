using SpanDraft.Core.Beams;
using SpanDraft.Core.Loads;
using SpanDraft.Core.Materials;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.State;
using Xunit;

namespace SpanDraft.Tests;

public sealed class DesktopMechanicalComparerTests
{
    private static Length M(double value) => Length.FromMeters(value);
    private static Material Material(double e = 210e9, double re = 235e6, string name = "Steel") =>
        new(name, Pressure.FromPascals(e), Pressure.FromPascals(re));
    private static Section Section(double a = .01, double i = 1e-6, double w = .001) =>
        new CustomSection(Area.FromSquareMeters(a), SecondMomentOfArea.FromMetersToTheFourth(i), SectionModulus.FromCubicMeters(w));
    private static BeamLoad[] Loads() => [new PointForce(M(.2), Force.FromNewtons(-1000)),
        new PointMoment(M(.5), Moment.FromNewtonMeters(100)),
        new UniformDistributedLoad(M(.1), M(.9), ForcePerLength.FromNewtonsPerMeter(-20))];
    private static Support[] Supports() => [new(M(0), SupportType.Pinned), new(M(1), SupportType.Roller)];
    private static BeamModel Beam(Length? length = null, Material? material = null, Section? section = null,
        IEnumerable<Support>? supports = null, IEnumerable<BeamLoad>? loads = null) =>
        new(length ?? M(1), material ?? Material(), section ?? Section(), supports ?? Supports(), loads ?? Loads());

    [Fact]
    public void IndependentCoreObjectsWithTheSameSemanticsAreEquivalent()
    {
        var left = Beam();
        var right = Beam();
        Assert.NotSame(left, right);
        Assert.NotSame(left.Material, right.Material);
        Assert.NotSame(left.Supports[0], right.Supports[0]);
        Assert.True(BeamModelMechanicalComparer.AreEquivalent(left, right));
        Assert.True(BeamModelMechanicalComparer.AreEquivalent(left, left));
    }

    [Theory]
    [InlineData("length")]
    [InlineData("E")]
    [InlineData("Re")]
    [InlineData("A")]
    [InlineData("I")]
    [InlineData("W")]
    [InlineData("support-position")]
    [InlineData("support-type")]
    [InlineData("support-order")]
    [InlineData("support-count")]
    [InlineData("force-position")]
    [InlineData("force-value")]
    [InlineData("moment-position")]
    [InlineData("moment-value")]
    [InlineData("load-type")]
    [InlineData("load-order")]
    [InlineData("load-count")]
    [InlineData("udl-start")]
    [InlineData("udl-end")]
    [InlineData("udl-intensity")]
    public void EachCurrentAnalysisRelevantValueIndependentlyBreaksEquivalence(string change)
    {
        var loads = Loads();
        var supports = Supports();
        switch (change)
        {
            case "support-position": supports[0] = new(M(.01), supports[0].Type); break;
            case "support-type": supports[0] = new(supports[0].Position, SupportType.Fixed); break;
            case "support-order": Array.Reverse(supports); break;
            case "support-count": supports = supports[..1]; break;
            case "force-position": loads[0] = new PointForce(M(.21), Force.FromNewtons(-1000)); break;
            case "force-value": loads[0] = new PointForce(M(.2), Force.FromNewtons(-1001)); break;
            case "moment-position": loads[1] = new PointMoment(M(.51), Moment.FromNewtonMeters(100)); break;
            case "moment-value": loads[1] = new PointMoment(M(.5), Moment.FromNewtonMeters(101)); break;
            case "load-type": loads[0] = new PointMoment(M(.2), Moment.FromNewtonMeters(-1000)); break;
            case "load-order": Array.Reverse(loads); break;
            case "load-count": loads = loads[..2]; break;
            case "udl-start": loads[2] = new UniformDistributedLoad(M(.11), M(.9), ForcePerLength.FromNewtonsPerMeter(-20)); break;
            case "udl-end": loads[2] = new UniformDistributedLoad(M(.1), M(.91), ForcePerLength.FromNewtonsPerMeter(-20)); break;
            case "udl-intensity": loads[2] = new UniformDistributedLoad(M(.1), M(.9), ForcePerLength.FromNewtonsPerMeter(-21)); break;
        }
        var next = Beam(length: change == "length" ? M(1.01) : null,
            material: Material(e: change == "E" ? 200e9 : 210e9, re: change == "Re" ? 355e6 : 235e6),
            section: Section(a: change == "A" ? .02 : .01, i: change == "I" ? 2e-6 : 1e-6, w: change == "W" ? .002 : .001),
            supports: supports, loads: loads);
        Assert.False(BeamModelMechanicalComparer.AreEquivalent(Beam(), next));
        Assert.False(BeamModelMechanicalComparer.AreEquivalent(next, Beam()));
    }

    [Fact]
    public void ChangesAreExactRatherThanToleranceBased()
    {
        Assert.False(BeamModelMechanicalComparer.AreEquivalent(Beam(), Beam(length: M(Math.BitIncrement(1)))));
        Assert.False(BeamModelMechanicalComparer.AreEquivalent(Beam(), Beam(material: Material(re: Math.BitIncrement(235e6)))));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void AllExistingProfilesAreExplicitlyCoveredByTheirCurrentAIWSemantics(int profile)
    {
        Section section = profile switch
        {
            0 => new RectangleSection(M(.1), M(.2)),
            1 => new RectangularHollowSection(M(.1), M(.2), M(.005)),
            2 => new CircleSection(M(.1)),
            3 => new CircularHollowSection(M(.1), M(.005)),
            _ => Section()
        };
        var equivalentProperties = new CustomSection(section.Area, section.SecondMomentOfArea, section.SectionModulus);
        Assert.True(BeamModelMechanicalComparer.AreEquivalent(Beam(section: section), Beam(section: equivalentProperties)));
    }

    [Fact]
    public void MaterialNamingIsNotAdditionallyAnalysisRelevantWhenEAndReMatch() =>
        Assert.True(BeamModelMechanicalComparer.AreEquivalent(Beam(material: Material(name: "Template A")),
            Beam(material: Material(name: "Template B"))));

    [Fact]
    public void UnknownVariantsUseTheConservativeFallback()
    {
        Assert.False(BeamModelMechanicalComparer.IsKnownSupportType((SupportType)999));
        Assert.True(BeamModelMechanicalComparer.IsKnownSupportType(SupportType.Fixed));
        Assert.False(BeamModelMechanicalComparer.LoadsEquivalent(new PointForce(M(.2), Force.FromNewtons(1)),
            new PointMoment(M(.2), Moment.FromNewtonMeters(1))));
        Assert.False(BeamModelMechanicalComparer.LoadsEquivalent(null!, null!));
    }

    [Fact]
    public void ClassifierUsesCoreSemanticsAndDesktopMetadataWithoutLocalEditLists()
    {
        var support = new EditorSupport(Guid.NewGuid(), M(0), SupportType.Fixed, "A");
        var document = new EditorDocument(M(1), Material(), Section(), [support]);
        var presentation = new EditorPresentationState();
        Assert.Equal(EditorChangeKind.None, EditorChangeClassifier.Classify(document,
            document.WithSupports([support with { }]), presentation, new()));
        Assert.Equal(EditorChangeKind.MetadataOnly, EditorChangeClassifier.Classify(document,
            document.WithSupports([support with { Name = "Bearing" }]), presentation, presentation));
        Assert.Equal(EditorChangeKind.MetadataOnly, EditorChangeClassifier.Classify(document,
            document with { Material = Material(name: "Other template") }, presentation, presentation));
        Assert.Equal(EditorChangeKind.MetadataOnly, EditorChangeClassifier.Classify(document, document,
            presentation, presentation.WithOffset(support.Id, new(10, 20))));
        Assert.Equal(EditorChangeKind.Mechanical, EditorChangeClassifier.Classify(document,
            document.WithSupports([support with { Name = "Bearing", Position = M(.1) }]), presentation, presentation));
        Assert.Equal(EditorChangeKind.Mechanical, EditorChangeClassifier.Classify(document,
            document with { Material = Material(re: 355e6) }, presentation, presentation));
        Assert.Equal(EditorChangeKind.Mechanical, EditorChangeClassifier.Classify(document,
            document with { Section = Section(w: .002) }, presentation, presentation));
    }
}
