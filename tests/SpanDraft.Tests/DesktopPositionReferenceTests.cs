using SpanDraft.Core.Materials;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Sections.Parametric;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.State;
using Xunit;

namespace SpanDraft.Tests;

/// <summary>
/// A position is a reference plus a distance. Geometry changes move what refers to a later segment, keep what is
/// measured from the beam start, and reject everything that would become invalid instead of correcting it.
/// </summary>
public sealed class DesktopPositionReferenceTests
{
    private static Length M(double value) => Length.FromMeters(value);
    private static readonly Material Steel = new("Steel", Pressure.FromPascals(210e9), Pressure.FromPascals(235e6),
        MaterialStrengthType.Re, MassDensity.FromKilogramsPerCubicMeter(7850));

    private static EditorSegment Segment(double length, Guid? id = null) =>
        new(Steel, new RectangleSectionGeometry(M(0.1), M(0.2)), SectionAxisDesignation.Y)
        { Length = M(length), Id = id ?? Guid.NewGuid() };

    private static PositionRef.FromBeamStart Beam(double distance) => new(M(distance));
    private static PositionRef.FromSegmentStart From(EditorSegment segment, double offset) => new(segment.Id, M(offset));
    private static PositionRef.AtSegmentEnd End(EditorSegment segment) => new(segment.Id);

    private static EditorSupport Support(string name, double x, PositionRef? reference = null,
        SupportType type = SupportType.Pinned) =>
        new(Guid.NewGuid(), M(x), type, name) { Reference = reference };

    private static EditorPointForce Force(string name, double x, PositionRef? reference = null) =>
        new(Guid.NewGuid(), M(x), SpanDraft.Core.Units.Force.FromNewtons(1000), name) { Reference = reference };

    private static EditorDocument Chain(EditorSegment[] segments, IEnumerable<EditorSupport>? supports = null,
        IEnumerable<EditorPointLoad>? loads = null, IEnumerable<EditorDistributedLoad>? lines = null,
        IEnumerable<EditorMomentRelease>? releases = null) =>
        new(segments, supports ?? [Support("A", 0)], loads, null, lines, null, releases);

    private static EditorDocument Accepted(GeometryChange change)
    {
        Assert.True(change.IsAccepted, string.Join(", ", change.Conflicts.Select(c => c.EntityName + ":" + c.Kind)));
        return change.Document!;
    }

    // ---- Resolution ----

    [Fact]
    public void ThreeFormsResolveToTheirGlobalX()
    {
        EditorSegment s1 = Segment(2), s2 = Segment(3), s3 = Segment(1);
        var document = Chain([s1, s2, s3]);

        Assert.Equal(M(4), document.ResolvePosition(Beam(4)));
        Assert.Equal(M(2), document.ResolvePosition(From(s2, 0)));
        Assert.Equal(M(3.5), document.ResolvePosition(From(s2, 1.5)));
        Assert.Equal(M(5), document.ResolvePosition(From(s2, 3)));
        Assert.Equal(M(5), document.ResolvePosition(End(s2)));
        Assert.Equal(M(6), document.ResolvePosition(End(s3)));
    }

    [Fact]
    public void ASegmentPositionBeyondItsSegmentOrOfAMissingSegmentDoesNotResolve()
    {
        EditorSegment s1 = Segment(2), s2 = Segment(3);
        var document = Chain([s1, s2]);

        Assert.False(document.TryResolve(From(s1, 2.5), out _, out var beyond));
        Assert.Equal(GeometryConflictKind.BeyondSegment, beyond);
        Assert.False(document.TryResolve(End(Segment(1)), out _, out var missing));
        Assert.Equal(GeometryConflictKind.SegmentMissing, missing);
        Assert.Throws<ArgumentException>(() => document.ResolvePosition(From(s1, 2.5)));
    }

    [Fact]
    public void TheDocumentDerivesEveryPositionFromItsReferenceAndStoresABeamStartDistanceAsNoReference()
    {
        EditorSegment s1 = Segment(2), s2 = Segment(3);
        var document = Chain([s1, s2], [Support("A", 0), Support("B", 99, From(s2, 1)), Support("C", 0, Beam(5))]);

        Assert.Equal(M(3), document.Supports[1].Position);
        Assert.Equal(From(s2, 1), document.Supports[1].Reference);
        Assert.Equal(M(5), document.Supports[2].Position);
        Assert.Null(document.Supports[2].Reference);
        Assert.Equal(Support("C", 5).EffectiveReference, document.Supports[2].EffectiveReference);
    }

    [Fact]
    public void EveryPositionBearingElementKeepsAReferenceAndALineLoadHasOneForEachEnd()
    {
        EditorSegment s1 = Segment(2), s2 = Segment(3);
        var line = new EditorDistributedLoad(Guid.NewGuid(), M(0), M(0), ForcePerLength.FromNewtonsPerMeter(-1000), "q")
        { StartReference = From(s1, 1), EndReference = End(s2) };
        var document = Chain([s1, s2], [Support("A", 0)], [Force("F", 0, From(s2, 1))], [line],
            [new EditorMomentRelease(Guid.NewGuid(), M(0), "G") { Reference = End(s1) }]);

        Assert.Equal(M(3), document.Loads[0].Position);
        Assert.Equal(M(1), document.DistributedLoads[0].StartPosition);
        Assert.Equal(M(5), document.DistributedLoads[0].EndPosition);
        Assert.Equal(M(2), document.MomentReleases[0].Position);
        Assert.Equal(From(s1, 1), document.DistributedLoads[0].StartReference);
        Assert.Equal(End(s2), document.DistributedLoads[0].EndReference);
    }

    [Fact]
    public void ABeamLengthSetOnTheLastSegmentMovesItsEndAndRejectsADistancePastIt()
    {
        EditorSegment s1 = Segment(2), s2 = Segment(3);
        var document = Chain([s1, s2], [Support("A", 0), Support("B", 0, End(s2)), Support("C", 0, From(s2, 2))]);

        var longer = document with { Length = M(8) };
        Assert.Equal(M(8), longer.Supports[1].Position);
        Assert.Equal(M(4), longer.Supports[2].Position);
        Assert.Throws<ArgumentException>(() => document with { Length = M(3.5) });
    }

    // ---- Insert, duplicate, resize ----

    [Fact]
    public void InsertingInTheMiddleMovesWhatReferstoLaterSegmentsAndKeepsEveryReference()
    {
        EditorSegment s1 = Segment(2), s2 = Segment(3), s3 = Segment(1);
        var supports = new[]
        {
            Support("A", 0), Support("B", 0, From(s3, 0.5)), Support("C", 0, Beam(4)), Support("D", 0, End(s1))
        };
        var document = Chain([s1, s2, s3], supports);

        var inserted = Accepted(document.InsertSegment(1, Segment(1.5)));

        Assert.Equal([M(0), M(7), M(4), M(2)], inserted.Supports.Select(s => s.Position));
        Assert.Equal(document.Supports.Select(s => s.Reference), inserted.Supports.Select(s => s.Reference));
        Assert.Equal(M(7.5), inserted.Length);
        Assert.Equal([s1.Id, inserted.Segments[1].Id, s2.Id, s3.Id], inserted.Segments.Select(s => s.Id));
    }

    [Fact]
    public void InsertingInFrontOfTheFirstSegmentKeepsBeamStartDistancesAndShiftsSegmentReferences()
    {
        EditorSegment s1 = Segment(2), s2 = Segment(3);
        var document = Chain([s1, s2], [Support("A", 0, Beam(0)), Support("B", 0, From(s1, 1)), Support("C", 0, End(s1))]);

        var inserted = Accepted(document.InsertSegment(0, Segment(1)));

        Assert.Equal([M(0), M(2), M(3)], inserted.Supports.Select(s => s.Position));
        Assert.Equal(From(s1, 1), inserted.Supports[1].Reference);
        Assert.Equal(End(s1), inserted.Supports[2].Reference);
    }

    [Fact]
    public void ALoadBoundToTheEndOfASegmentStaysAtThatBoundaryWhenASegmentIsAddedBehind()
    {
        EditorSegment s1 = Segment(4);
        var document = Chain([s1], [Support("A", 0), Support("B", 0, End(s1))]);

        var grown = Accepted(document.AddSegment(Segment(2)));

        Assert.Equal(M(4), grown.Supports[1].Position);
        Assert.Equal(M(6), grown.Length);
        Assert.Equal(End(s1), grown.Supports[1].Reference);
    }

    [Fact]
    public void ResizingASegmentMovesWhatFollowsItAndAnEndBindingFollowsItsLength()
    {
        EditorSegment s1 = Segment(2), s2 = Segment(3);
        var document = Chain([s1, s2], [Support("A", 0), Support("B", 0, End(s1)), Support("C", 0, From(s1, 1.5)),
            Support("D", 0, From(s2, 1)), Support("E", 0, Beam(1))]);

        var longer = Accepted(document.ResizeSegment(s1.Id, M(2.5)));

        Assert.Equal([M(0), M(2.5), M(1.5), M(3.5), M(1)], longer.Supports.Select(s => s.Position));
        Assert.Equal(M(5.5), longer.Length);
        Assert.Equal([s1.Id, s2.Id], longer.Segments.Select(s => s.Id));
    }

    [Fact]
    public void ShorteningASegmentBelowAnUnboundDistanceIsRejectedAndNamesTheElement()
    {
        EditorSegment s1 = Segment(2), s2 = Segment(3);
        var document = Chain([s1, s2], [Support("A", 0), Support("B", 0, From(s1, 1.8))]);

        var change = document.ResizeSegment(s1.Id, M(1.5));

        Assert.False(change.IsAccepted);
        Assert.Equal(GeometryRejection.Conflicts, change.Rejection);
        var conflict = Assert.Single(change.Conflicts);
        Assert.Equal("B", conflict.EntityName);
        Assert.Equal(GeometryConflictKind.BeyondSegment, conflict.Kind);
        // The original is untouched.
        Assert.Equal(M(1.8), document.Supports[1].Position);
    }

    [Fact]
    public void ShorteningIsFineWhenTheElementIsBoundToTheEnd()
    {
        EditorSegment s1 = Segment(2), s2 = Segment(3);
        var document = Chain([s1, s2], [Support("A", 0), Support("B", 0, End(s1))]);

        var shorter = Accepted(document.ResizeSegment(s1.Id, M(1.5)));

        Assert.Equal(M(1.5), shorter.Supports[1].Position);
    }

    [Fact]
    public void ShorteningTheBeamBelowABeamStartDistanceIsRejected()
    {
        EditorSegment s1 = Segment(2), s2 = Segment(3);
        var document = Chain([s1, s2], [Support("A", 0), Support("B", 0, Beam(4.5))],
            [Force("F", 0, Beam(4.5))]);

        var change = document.ResizeSegment(s2.Id, M(2));

        Assert.False(change.IsAccepted);
        Assert.Equal(["B", "F"], change.Conflicts.Select(c => c.EntityName).Order());
        Assert.All(change.Conflicts, c => Assert.Equal(GeometryConflictKind.RuleViolation, c.Kind));
    }

    [Fact]
    public void DuplicatingInsertsAnIdenticalSegmentWithANewIdentityDirectlyBehindTheOriginal()
    {
        EditorSegment s1 = Segment(2), s2 = Segment(3);
        var document = Chain([s1, s2], [Support("A", 0), Support("B", 0, From(s2, 1))]);

        var copy = Accepted(document.DuplicateSegment(s1.Id));

        Assert.Equal(3, copy.Segments.Count);
        Assert.Equal(s1.Id, copy.Segments[0].Id);
        Assert.NotEqual(s1.Id, copy.Segments[1].Id);
        Assert.Equal(s2.Id, copy.Segments[2].Id);
        Assert.Equal(copy.Segments[0] with { Id = copy.Segments[1].Id }, copy.Segments[1]);
        Assert.Equal(M(5), copy.Supports[1].Position);
        Assert.Equal(GeometryRejection.UnknownSegment, document.DuplicateSegment(Guid.NewGuid()).Rejection);
    }

    // ---- Remove ----

    [Fact]
    public void RemovingASegmentMovesSegmentReferencesLeftAndKeepsBeamStartDistances()
    {
        EditorSegment s1 = Segment(2), s2 = Segment(3), s3 = Segment(2);
        var document = Chain([s1, s2, s3], [Support("A", 0), Support("PA", 0, From(s3, 0.5))],
            [Force("B", 0, Beam(4))]);

        var removed = Accepted(document.RemoveSegment(s2.Id));

        Assert.Equal(M(2.5), removed.Supports[1].Position);
        Assert.Equal(M(4), removed.Loads[0].Position);
        Assert.Null(removed.Loads[0].Reference);
        Assert.Equal(M(4), removed.Length);
    }

    [Fact]
    public void RemovingASegmentIsRejectedWhenAnElementRefersToItDirectly()
    {
        EditorSegment s1 = Segment(2), s2 = Segment(3), s3 = Segment(2);
        var document = Chain([s1, s2, s3], [Support("A", 0), Support("Start", 0, From(s2, 1)), Support("Finish", 0, End(s2))]);

        var change = document.RemoveSegment(s2.Id);

        Assert.False(change.IsAccepted);
        Assert.Equal(["Finish", "Start"], change.Conflicts.Select(c => c.EntityName).Order());
        Assert.All(change.Conflicts, c => Assert.Equal(GeometryConflictKind.SegmentMissing, c.Kind));
    }

    [Fact]
    public void RemovingASegmentIsRejectedWhenABeamStartDistanceFallsOutsideTheBeam()
    {
        EditorSegment s1 = Segment(2), s2 = Segment(3);
        var document = Chain([s1, s2], [Support("A", 0), Support("B", 0, Beam(4))]);

        var change = document.RemoveSegment(s2.Id);

        Assert.False(change.IsAccepted);
        Assert.Equal("B", Assert.Single(change.Conflicts).EntityName);
    }

    [Fact]
    public void RemovingASegmentIsRejectedWhenElementsWouldCoincideAfterwards()
    {
        EditorSegment s1 = Segment(2), s2 = Segment(3), s3 = Segment(2);
        var document = Chain([s1, s2, s3], [Support("A", 0), Support("B", 0, Beam(2)), Support("C", 0, From(s3, 0))]);
        Assert.Equal(M(5), document.Supports[2].Position);

        var change = document.RemoveSegment(s2.Id);

        // The spring-free supports differ in kind only by name here, so the duplicate rule applies.
        Assert.False(change.IsAccepted);
        Assert.Equal("C", Assert.Single(change.Conflicts).EntityName);
    }

    [Fact]
    public void AMomentReleaseCannotEndUpAtTheBeamEnd()
    {
        EditorSegment s1 = Segment(2), s2 = Segment(3);
        var document = Chain([s1, s2], releases: [new EditorMomentRelease(Guid.NewGuid(), M(0), "G") { Reference = End(s1) }]);

        var change = document.RemoveSegment(s2.Id);

        Assert.False(change.IsAccepted);
        Assert.Equal("G", Assert.Single(change.Conflicts).EntityName);
    }

    [Fact]
    public void TheLastRemainingSegmentCannotBeRemoved()
    {
        EditorSegment only = Segment(4);
        var document = Chain([only]);

        var change = document.RemoveSegment(only.Id);

        Assert.False(change.IsAccepted);
        Assert.Equal(GeometryRejection.LastSegment, change.Rejection);
        Assert.Equal(GeometryRejection.UnknownSegment, document.RemoveSegment(Guid.NewGuid()).Rejection);
    }

    // ---- Line loads ----

    [Fact]
    public void ALineLoadChangesItsLoadedLengthWithTheGeometryBetweenItsTwoReferences()
    {
        EditorSegment s1 = Segment(2), s2 = Segment(3), s3 = Segment(1);
        var line = new EditorDistributedLoad(Guid.NewGuid(), M(0), M(0), ForcePerLength.FromNewtonsPerMeter(-1000), "q")
        { StartReference = From(s1, 1), EndReference = End(s3) };
        var document = Chain([s1, s2, s3], lines: [line]);

        var changed = Accepted(document.ResizeSegment(s2.Id, M(5)));

        Assert.Equal(M(1), changed.DistributedLoads[0].StartPosition);
        Assert.Equal(M(8), changed.DistributedLoads[0].EndPosition);
        Assert.Equal(ForcePerLength.FromNewtonsPerMeter(-1000), changed.DistributedLoads[0].StartIntensity);
    }

    [Fact]
    public void ALineLoadWhoseEndsWouldCrossIsRejected()
    {
        EditorSegment s1 = Segment(2), s2 = Segment(3), s3 = Segment(1);
        var line = new EditorDistributedLoad(Guid.NewGuid(), M(0), M(0), ForcePerLength.FromNewtonsPerMeter(-1000), "q")
        { StartReference = From(s3, 0.5), EndReference = Beam(5.8) };
        var document = Chain([s1, s2, s3], lines: [line]);
        Assert.Equal(M(5.5), document.DistributedLoads[0].StartPosition);

        var change = document.InsertSegment(1, Segment(1));

        Assert.False(change.IsAccepted);
        Assert.Equal("q", Assert.Single(change.Conflicts).EntityName);
    }

    // ---- The geometry itself ----

    [Fact]
    public void AnAcceptedChangeNeverChangesAnyReference()
    {
        EditorSegment s1 = Segment(2), s2 = Segment(3), s3 = Segment(2);
        var document = Chain([s1, s2, s3], [Support("A", 0), Support("B", 0, From(s3, 1)), Support("C", 0, End(s1)),
            Support("D", 0, Beam(1))]);

        var changed = Accepted(document.InsertSegment(2, Segment(1)));
        changed = Accepted(changed.ResizeSegment(s1.Id, M(2.5)));

        Assert.Equal(document.Supports.Select(s => s.Reference), changed.Supports.Select(s => s.Reference));
    }

    [Fact]
    public void ChangesWorkWithTheSelfWeightOnAndAreCheckedOnPositionsOnly()
    {
        EditorSegment s1 = Segment(2), s2 = Segment(3);
        var document = new EditorDocument([s1, s2], [Support("A", 0), Support("B", 0, End(s2))], null, null, null,
            new EditorSelfWeight(Guid.NewGuid(), true));

        var grown = Accepted(document.ResizeSegment(s2.Id, M(4)));

        Assert.Equal(M(6), grown.Supports[1].Position);
        Assert.Equal(2, grown.SelfWeightLoads.Count);
        Assert.Equal(M(2), grown.SelfWeightLoads[0].EndPosition);
    }

    [Fact]
    public void ReferencedPositionsKeepTheirReferenceWhenTheDocumentIsRebuiltFromItsParts()
    {
        EditorSegment s1 = Segment(2), s2 = Segment(3);
        var document = Chain([s1, s2], [Support("A", 0), Support("B", 0, From(s2, 1))]);

        var rebuilt = document.WithSupports(document.Supports).WithLoads([Force("F", 0, End(s1))]);

        Assert.Equal(From(s2, 1), rebuilt.Supports[1].Reference);
        Assert.Equal(M(2), rebuilt.Loads[0].Position);
    }
}
