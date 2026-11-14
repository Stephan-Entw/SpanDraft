using SpanDraft.Core.Materials;
using SpanDraft.Core.Releases;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Core.Validation;
using SpanDraft.Desktop.State;
using Xunit;

namespace SpanDraft.Tests;

/// <summary>The editor document of a beam with internal moment releases: state, naming, equivalence and length rules.</summary>
public sealed class DesktopMomentReleaseDocumentTests
{
    private static Length M(double value) => Length.FromMeters(value);
    private static readonly Material Steel = new("Steel", Pressure.FromPascals(210e9), Pressure.FromPascals(235e6),
        MaterialStrengthType.Re, MassDensity.FromKilogramsPerCubicMeter(7850));
    private static EditorMomentRelease Release(double x, string name, Guid? id = null) => new(id ?? Guid.NewGuid(), M(x), name);

    private static EditorDocument Document(params EditorMomentRelease[] releases) => new EditorDocument(M(6), Steel,
        new RectangleSection(M(.1), M(.2)),
        new EditorSupport[] { new(Guid.NewGuid(), M(0), SupportType.Pinned, "A"), new(Guid.NewGuid(), M(3), SupportType.Roller, "B"),
            new(Guid.NewGuid(), M(6), SupportType.Roller, "C") }).WithMomentReleases(releases);

    private static ProjectState State(EditorDocument document) => new(document, new(new Dictionary<Guid, AnnotationOffset>(), true));

    [Fact]
    public void ADocumentWithoutReleasesHasNoneAndKeepsTheModelUnchanged()
    {
        EditorDocument document = Document();
        Assert.Empty(document.MomentReleases);
        Assert.Empty(document.ToBeamModel().MomentReleases);
    }

    [Fact]
    public void ReleasesReachTheBeamModelInDocumentOrderAndEveryWithMethodKeepsThem()
    {
        EditorDocument document = Document(Release(4.5, "G1"), Release(1.5, "G2"));
        Assert.Equal([4.5, 1.5], document.ToBeamModel().MomentReleases.Select(r => r.Position.Meters));
        EditorDocument[] derived =
        [
            document.WithSupports(document.Supports), document.WithLoads([]), document.WithDistributedLoads([]),
            document.WithSection(new RectangleSection(M(.1), M(.3)), SectionAxisDesignation.Y),
            document.WithBoundaryAt(M(2)), document.WithBoundaryAt(M(2)).WithBoundary(0, M(2.5)),
            document.WithBoundaryAt(M(2)).WithoutBoundary(0), document with { Length = M(8) }
        ];
        Assert.All(derived, d => Assert.Equal(document.MomentReleases, d.MomentReleases));
        Assert.Empty(document.WithMomentReleases([]).MomentReleases);
    }

    [Fact]
    public void ReleaseNamesJoinTheProjectWideUniqueNamesAndAreTrimmed()
    {
        Assert.Throws<ArgumentException>(() => Document(Release(1, "A")));
        Assert.Throws<ArgumentException>(() => Document(Release(1, "G1"), Release(2, "g1")));
        Assert.Equal("G1", Release(1, "  G1 ").Name);
        Assert.Contains(Document(Release(1, "G1")).NamedEntities, e => e.Name == "G1");
        Assert.Contains(Document(Release(1, "G1")).EntityIds, id => id != Guid.Empty);
    }

    [Fact]
    public void TheAutomaticNameHasItsOwnCounterAndSkipsOccupiedNames()
    {
        EditorDocument document = Document();
        Assert.Equal("G1", EntityNaming.Peek(document, AutoNameKind.MomentRelease).Name);
        Assert.Equal("G3", EntityNaming.Format(AutoNameKind.MomentRelease, 3));
        // A user name may take the next automatic one; the candidate then moves on.
        EditorDocument taken = Document(Release(1, "G1"), Release(2, "G2"));
        AutoNameCandidate candidate = EntityNaming.Peek(taken, AutoNameKind.MomentRelease);
        Assert.Equal(new AutoNameCandidate(AutoNameKind.MomentRelease, 3, "G3"), candidate);
        NamingState consumed = taken.NamingState.Consume(candidate);
        Assert.Equal(4, consumed.NextMomentReleaseNumber);
        Assert.Equal(1, consumed.NextSupportOrdinal);
        Assert.Equal(1, consumed.NextForceNumber);
        Assert.Equal(1, consumed.NextMomentNumber);
        Assert.Equal(1, consumed.NextDistributedLoadNumber);
        EditorDocument next = taken.WithMomentReleases([.. taken.MomentReleases, Release(4, candidate.Name)], consumed);
        Assert.Equal(4, next.NamingState.NextMomentReleaseNumber);
        Assert.Throws<ArgumentException>(() => next.WithMomentReleases([], new NamingState()));
    }

    [Fact]
    public void TheSelfWeightSymbolStaysReservedAndAReleaseNameCannotTakeIt()
    {
        // The automatic name G1 differs from the reserved bare symbol; a user name equal to it is a conflict.
        Assert.False(EntityNaming.IsReserved(EntityNaming.Format(AutoNameKind.MomentRelease, 1)));
        Assert.True(EntityNaming.IsReserved("g"));
        EditorDocument document = Document(Release(1, "G1")) with { SelfWeight = new(Guid.NewGuid(), true) };
        Assert.Null(document.SelfWeightActivationError);
        Assert.NotNull((Document(Release(1, "g")) with { SelfWeight = new(Guid.NewGuid(), true) }).SelfWeightActivationError);
        Assert.False(EntityNaming.TryNormalize(document, "g", null, out _, out NameValidationError error));
        Assert.Equal(NameValidationError.Reserved, error);
    }

    [Fact]
    public void EveryNamingKindPeeksItsOwnCounterAndOnlyReleasesUseG()
    {
        EditorDocument document = Document();
        Assert.Equal(["D", "F1", "M1", "q1", "G1"],
        [
            EntityNaming.Peek(document, AutoNameKind.Support).Name, EntityNaming.Peek(document, AutoNameKind.Force).Name,
            EntityNaming.Peek(document, AutoNameKind.Moment).Name, EntityNaming.Peek(document, AutoNameKind.DistributedLoad).Name,
            EntityNaming.Peek(document, AutoNameKind.MomentRelease).Name
        ]);
    }

    [Fact]
    public void TheLengthMustStayBeyondEveryRelease()
    {
        EditorDocument document = Document(Release(4.5, "G1"), Release(2, "G2")) with { Length = M(9) };
        Guid[] releases = [.. document.MomentReleases.Select(r => r.Id)];
        Guid[] Blocked(double length) => [.. BeamLengthConstraints.BlockingEntities(document, M(length)).Intersect(releases)];
        Assert.Equal([releases[0]], Blocked(4.5));
        Assert.Equal([releases[0]], Blocked(4));
        Assert.Equal(releases, Blocked(2));
        Assert.Empty(Blocked(Math.BitIncrement(4.5)));
        // The same exact comparison holds for the error: a supported length of 6 m keeps all three supports.
        EditorDocument noSupportBeyond = document.WithSupports(document.Supports.Take(2));
        Assert.NotNull(BeamLengthConstraints.Error(noSupportBeyond, M(4.5)));
        Assert.Null(BeamLengthConstraints.Error(noSupportBeyond, M(Math.BitIncrement(4.5))));
    }

    [Fact]
    public void ThePositionOfAReleaseIsAMechanicalChangeAndItsNameIsOnlyMetadata()
    {
        Guid id = Guid.NewGuid();
        ProjectState before = State(Document(Release(4.5, "G1", id)));
        Assert.Equal(EditorChangeKind.None, EditorChangeClassifier.Classify(before, State(before.Document)));
        Assert.Equal(EditorChangeKind.MetadataOnly, EditorChangeClassifier.Classify(before,
            State(before.Document.WithMomentReleases([Release(4.5, "Gerber hinge", id)]))));
        Assert.Equal(EditorChangeKind.Mechanical, EditorChangeClassifier.Classify(before,
            State(before.Document.WithMomentReleases([Release(4.25, "G1", id)]))));
        Assert.Equal(EditorChangeKind.Mechanical, EditorChangeClassifier.Classify(before,
            State(before.Document.WithMomentReleases([Release(Math.BitIncrement(4.5), "G1", id)]))));
        Assert.Equal(EditorChangeKind.Mechanical, EditorChangeClassifier.Classify(before, State(before.Document.WithMomentReleases([]))));
        Assert.Equal(EditorChangeKind.Mechanical, EditorChangeClassifier.Classify(State(before.Document.WithMomentReleases([])), before));
        Assert.Equal(EditorChangeKind.Mechanical, EditorChangeClassifier.Classify(before,
            State(before.Document.WithMomentReleases([.. before.Document.MomentReleases, Release(1.5, "G2")]))));
        Assert.False(before.ContentEquals(State(before.Document.WithMomentReleases([Release(4.5, "G9", id)]))));
    }

    [Fact]
    public void TheMechanicalComparerComparesReleasePositionsExactlyAndInOrder()
    {
        EditorDocument two = Document(Release(1.5, "G1"), Release(4.5, "G2"));
        Assert.True(BeamModelMechanicalComparer.AreEquivalent(two.ToBeamModel(), two.ToBeamModel()));
        Assert.False(BeamModelMechanicalComparer.AreEquivalent(two.ToBeamModel(),
            two.WithMomentReleases([two.MomentReleases[1], two.MomentReleases[0]]).ToBeamModel()));
        Assert.False(BeamModelMechanicalComparer.AreEquivalent(two.ToBeamModel(), Document().ToBeamModel()));
        Assert.False(BeamModelMechanicalComparer.AreEquivalent(Document().ToBeamModel(), two.ToBeamModel()));
    }

    [Fact]
    public void ADocumentWithAReleaseOnAFixedSupportIsAnInvalidModelThatTheCoreReports()
    {
        EditorDocument document = new EditorDocument(M(6), Steel, new RectangleSection(M(.1), M(.2)),
            new EditorSupport[] { new(Guid.NewGuid(), M(0), SupportType.Pinned, "A"), new(Guid.NewGuid(), M(3), SupportType.Fixed, "B") })
            .WithMomentReleases([Release(3, "G1")]);
        Assert.Equal(ValidationErrorCode.MomentReleaseAtFixedSupport,
            Assert.Single(BeamModelValidator.Validate(document.ToBeamModel())).Code);
    }
}
