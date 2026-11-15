using SpanDraft.Core.Materials;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Sections.Parametric;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;
using Xunit;

namespace SpanDraft.Tests;

/// <summary>
/// The reference of a position follows direct actions only: a freshly set position gets the segment under it, a moved one
/// keeps its reference while it stays in its segment, and a binding to a segment end is set and released by the user alone.
/// </summary>
public sealed class DesktopPositionReferenceEditingTests
{
    private static Length M(double value) => Length.FromMeters(value);
    private static readonly Material Steel = new("Steel", Pressure.FromPascals(210e9), Pressure.FromPascals(235e6),
        MaterialStrengthType.Re, MassDensity.FromKilogramsPerCubicMeter(7850));
    private static EditorSegment Segment(double length) => new(Steel, new RectangleSectionGeometry(M(0.1), M(0.2)),
        SectionAxisDesignation.Y) { Length = M(length), Id = Guid.NewGuid() };

    private static EditorDocument Document(int segments, IEnumerable<EditorSupport>? supports = null) =>
        new(Enumerable.Range(0, segments).Select(_ => Segment(1)).ToArray(),
            supports ?? [new EditorSupport(Guid.NewGuid(), M(0), SupportType.Pinned, "A")]);

    private static PositionRef.FromSegmentStart From(EditorDocument document, int index, double offset) =>
        new(document.Segments[index].Id, M(offset));

    // ---- The rules ----

    [Fact]
    public void AFreshPositionIsAFixedDistanceWithOneSegmentAndTheSegmentUnderItWithSeveral()
    {
        var one = Document(1);
        var three = Document(3);

        Assert.Equal(new PositionRef.FromBeamStart(M(0.4)), PositionReferences.ForNewPosition(one, M(0.4)));
        Assert.Equal(From(three, 1, 0.5), PositionReferences.ForNewPosition(three, M(1.5)));
        Assert.Equal(From(three, 0, 0), PositionReferences.ForNewPosition(three, M(0)));
        // On an inner boundary it is the segment that starts there, at the beam end the last segment: never an end binding.
        Assert.Equal(From(three, 1, 0), PositionReferences.ForNewPosition(three, M(1)));
        Assert.Equal(From(three, 2, 1), PositionReferences.ForNewPosition(three, M(3)));
    }

    [Fact]
    public void AMovedFixedDistanceStaysOneAndAnUnmovedPositionKeepsEverything()
    {
        var three = Document(3);
        var end = new PositionRef.AtSegmentEnd(three.Segments[1].Id);

        Assert.Equal(new PositionRef.FromBeamStart(M(2.5)),
            PositionReferences.AfterMove(three, new PositionRef.FromBeamStart(M(0.5)), M(0.5), M(2.5)));
        Assert.Same(end, PositionReferences.AfterMove(three, end, M(2), M(2)));
        var from = From(three, 1, 0.5);
        Assert.Same(from, PositionReferences.AfterMove(three, from, M(1.5), M(1.5)));
    }

    [Fact]
    public void ASegmentReferenceStaysWhileThePositionIsInItsSegmentIncludingItsEnds()
    {
        var three = Document(3);
        var current = From(three, 1, 0.5);

        Assert.Equal(From(three, 1, 0.25), PositionReferences.AfterMove(three, current, M(1.5), M(1.25)));
        Assert.Equal(From(three, 1, 0), PositionReferences.AfterMove(three, current, M(1.5), M(1)));
        Assert.Equal(From(three, 1, 1), PositionReferences.AfterMove(three, current, M(1.5), M(2)));
    }

    [Fact]
    public void ASegmentReferenceFollowsThePositionIntoTheInsideOfAnotherSegmentOnly()
    {
        var three = Document(3);
        var current = From(three, 1, 0.5);

        Assert.Equal(From(three, 2, 0.5), PositionReferences.AfterMove(three, current, M(1.5), M(2.5)));
        Assert.Equal(From(three, 0, 0.5), PositionReferences.AfterMove(three, current, M(1.5), M(0.5)));
        // On a boundary that does not touch its own segment it stays on the side the position came from.
        var first = From(three, 0, 0.5);
        Assert.Equal(From(three, 1, 1), PositionReferences.AfterMove(three, first, M(0.5), M(2)));
        var last = From(three, 2, 0.5);
        Assert.Equal(From(three, 1, 0), PositionReferences.AfterMove(three, last, M(2.5), M(1)));
        Assert.Equal(From(three, 0, 0), PositionReferences.AfterMove(three, last, M(2.5), M(0)));
        Assert.Equal(From(three, 2, 1), PositionReferences.AfterMove(three, first, M(0.5), M(3)));
    }

    [Fact]
    public void AMovedEndBindingBecomesAFixedDistanceAndMovingNeverCreatesOne()
    {
        var three = Document(3);
        var bound = new PositionRef.AtSegmentEnd(three.Segments[1].Id);

        var moved = PositionReferences.AfterMove(three, bound, M(2), M(1.75));
        Assert.Equal(From(three, 1, 0.75), moved);
        Assert.IsNotType<PositionRef.AtSegmentEnd>(PositionReferences.AfterMove(three, From(three, 1, 0.5), M(1.5), M(2)));
        Assert.IsNotType<PositionRef.AtSegmentEnd>(PositionReferences.AfterMove(three, From(three, 0, 0.5), M(0.5), M(1)));
    }

    [Fact]
    public void ADistanceFromASegmentStartResolvesToExactlyThePositionItWasMadeFor()
    {
        var document = new EditorDocument([Segment(0.1), Segment(0.2), Segment(0.7)]);
        foreach (double x in new[] { 0.1, 0.3, 0.30000000000000004, 0.45, 0.7, 1.0 })
        {
            var reference = PositionReferences.ForNewPosition(document, M(x));
            Assert.Equal(M(x), document.ResolvePosition(reference));
        }
    }

    // ---- Placing, moving and confirming in the editor ----

    private static EditorViewModel Editor(EditorDocument document) => new(document, () => { });

    private static EditorSupport PlaceSupport(EditorViewModel editor, double x, SupportType type = SupportType.Pinned)
    {
        editor.ToggleSupportTool(type);
        editor.HoverPlacement(M(x));
        Assert.True(editor.PlaceSupport());
        Assert.True(editor.ConfirmSupport());
        return editor.Document.Supports.Last();
    }

    [Fact]
    public void ASupportPlacedWithSeveralSegmentsIsMeasuredFromTheSegmentUnderIt()
    {
        var document = Document(3);
        var editor = Editor(document);

        var support = PlaceSupport(editor, 1.5);

        Assert.Equal(From(document, 1, 0.5), support.Reference);
        Assert.Equal(M(1.5), support.Position);
        // The beam end and a boundary are no reason for an end binding.
        Assert.Equal(From(document, 2, 1), PlaceSupport(editor, 3).Reference);
        Assert.Equal(From(document, 1, 0), PlaceSupport(editor, 1, SupportType.Roller).Reference);
    }

    [Fact]
    public void ASupportPlacedWithOneSegmentIsAFixedDistanceFromTheBeamStart()
    {
        var editor = Editor(Document(1));

        var support = PlaceSupport(editor, 0.4);

        Assert.Null(support.Reference);
        Assert.Equal(new PositionRef.FromBeamStart(M(0.4)), support.EffectiveReference);
    }

    [Fact]
    public void TheEndBindingIsOfferedOnlyAtASegmentEndAndNamesTheSegment()
    {
        var document = Document(3);
        var editor = Editor(document);
        var inner = PlaceSupport(editor, 1);
        var end = PlaceSupport(editor, 3, SupportType.Roller);
        var middle = PlaceSupport(editor, 1.5, SupportType.Fixed);

        Assert.True(editor.EditSupport(inner.Id));
        // On an inner boundary the position is the start of segment 2 and the end of segment 1; the binding is to segment 1.
        Assert.True(editor.SupportDraft!.Reference.CanHoldEnd);
        Assert.Equal(string.Format(Strings.HoldAtEndOfSegment, 1), editor.SupportDraft.Reference.HoldEndLabel);
        Assert.False(editor.SupportDraft.Reference.HoldEnd);
        editor.CancelSupportInteraction();
        Assert.True(editor.EditSupport(end.Id));
        Assert.Equal(string.Format(Strings.HoldAtEndOfSegment, 3), editor.SupportDraft!.Reference.HoldEndLabel);
        editor.CancelSupportInteraction();
        Assert.True(editor.EditSupport(middle.Id));
        Assert.False(editor.SupportDraft!.Reference.CanHoldEnd);
    }

    [Fact]
    public void BindingToTheEndOfASegmentIsAnExplicitChoiceThatThePositionThenFollows()
    {
        var document = Document(3);
        var editor = Editor(document);
        var end = PlaceSupport(editor, 3, SupportType.Roller);
        Assert.IsType<PositionRef.FromSegmentStart>(end.Reference);

        Assert.True(editor.EditSupport(end.Id));
        editor.SupportDraft!.Reference.HoldEnd = true;
        Assert.True(editor.ConfirmSupport());

        var bound = editor.Document.Supports.Single(s => s.Id == end.Id);
        Assert.Equal(new PositionRef.AtSegmentEnd(document.Segments[2].Id), bound.Reference);
        var longer = editor.Document.ResizeSegment(document.Segments[2].Id, M(2.5));
        Assert.True(longer.IsAccepted);
        Assert.Equal(M(4.5), longer.Document!.Supports.Single(s => s.Id == end.Id).Position);
    }

    [Fact]
    public void ConfirmingABoundPositionWithoutMovingItKeepsTheBindingAndMovingItReleasesIt()
    {
        var document = Document(3);
        var editor = Editor(document);
        var support = PlaceSupport(editor, 3, SupportType.Roller);
        Assert.True(editor.EditSupport(support.Id));
        editor.SupportDraft!.Reference.HoldEnd = true;
        Assert.True(editor.ConfirmSupport());

        Assert.True(editor.EditSupport(support.Id));
        Assert.True(editor.SupportDraft!.Reference.HoldEnd);
        Assert.True(editor.ConfirmSupport());
        Assert.IsType<PositionRef.AtSegmentEnd>(editor.Document.Supports.Single(s => s.Id == support.Id).Reference);

        Assert.True(editor.EditSupport(support.Id));
        // The distance is measured from the segment the position is bound to; typing over it releases the binding.
        editor.SupportDraft!.PositionText = "500";
        Assert.False(editor.SupportDraft.Reference.HoldEnd);
        Assert.True(editor.ConfirmSupport());
        Assert.Equal(From(document, 2, 0.5), editor.Document.Supports.Single(s => s.Id == support.Id).Reference);
    }

    [Fact]
    public void DraggingKeepsTheReferenceInItsSegmentFollowsIntoAnotherAndNeverBindsTheEnd()
    {
        var document = Document(3);
        var editor = Editor(document);
        var support = PlaceSupport(editor, 1.5);

        Assert.True(editor.BeginSupportDrag(support.Id));
        editor.UpdateSupportDrag(M(1.75));
        Assert.True(editor.EndSupportDrag());
        Assert.True(editor.ConfirmSupport());
        Assert.Equal(From(document, 1, 0.75), editor.Document.Supports.Last().Reference);

        Assert.True(editor.BeginSupportDrag(support.Id));
        editor.UpdateSupportDrag(M(2.5));
        Assert.True(editor.EndSupportDrag());
        Assert.True(editor.ConfirmSupport());
        Assert.Equal(From(document, 2, 0.5), editor.Document.Supports.Single(s => s.Id == support.Id).Reference);

        Assert.True(editor.BeginSupportDrag(support.Id));
        editor.UpdateSupportDrag(M(3));
        Assert.True(editor.EndSupportDrag());
        Assert.True(editor.ConfirmSupport());
        Assert.IsType<PositionRef.FromSegmentStart>(editor.Document.Supports.Single(s => s.Id == support.Id).Reference);
    }

    [Fact]
    public void AFixedDistanceFromTheBeamStartStaysOneWhenDraggedIntoAnotherSegment()
    {
        var document = Document(3, [new EditorSupport(Guid.NewGuid(), M(0), SupportType.Pinned, "A"),
            new EditorSupport(Guid.NewGuid(), M(1.5), SupportType.Roller, "B")]);
        var editor = Editor(document);
        var support = document.Supports[1];

        Assert.True(editor.BeginSupportDrag(support.Id));
        editor.UpdateSupportDrag(M(2.5));
        Assert.True(editor.EndSupportDrag());
        Assert.True(editor.ConfirmSupport());

        var moved = editor.Document.Supports.Single(s => s.Id == support.Id);
        Assert.Null(moved.Reference);
        Assert.Equal(M(2.5), moved.Position);
    }

    [Fact]
    public void TheFlyoutShowsTheDistanceFromTheChosenReferenceAndChoosingKeepsThePoint()
    {
        var document = Document(3);
        var editor = Editor(document);
        var support = PlaceSupport(editor, 1.5);

        Assert.True(editor.EditSupport(support.Id));
        var draft = editor.SupportDraft!;
        var reference = draft.Reference;
        Assert.True(reference.ShowReference);
        Assert.Equal([Strings.PositionFromBeamStart, string.Format(Strings.SegmentSelected, 1), string.Format(Strings.SegmentSelected, 2),
            string.Format(Strings.SegmentSelected, 3)], reference.Options.Select(o => o.Name));
        Assert.Equal(document.Segments[1].Id, reference.SelectedOption!.Segment);
        // Measured from segment 2 (which starts at 1000 mm) the point is 500 mm away.
        Assert.Equal("500", draft.PositionText);
        // The label says what the field holds: a distance, not the position x.
        Assert.Equal(Strings.PositionDistance, draft.PositionLabel.PlainText);
        Assert.True(reference.ShowPositionInfo);
        Assert.Contains("1500", reference.PositionInfo);

        reference.SelectedOption = reference.Options[0];
        Assert.Equal("1500", draft.PositionText);
        Assert.NotEqual(Strings.PositionDistance, draft.PositionLabel.PlainText);
        Assert.Equal(new PositionRef.FromBeamStart(M(1.5)), reference.Current);
        Assert.Equal(M(1.5), reference.Position);
        Assert.False(reference.ShowPositionInfo);

        reference.SelectedOption = reference.Options[3];
        Assert.Equal(M(0), reference.Distance);
        Assert.Equal("0", draft.PositionText);
        Assert.Equal(M(2), reference.Position);

        reference.SelectedOption = reference.Options[2];
        Assert.Equal(M(2), reference.Position);
        Assert.Equal("1000", draft.PositionText);
        Assert.True(editor.ConfirmSupport());
        var changed = editor.Document.Supports.Single(s => s.Id == support.Id);
        Assert.Equal(From(document, 1, 1), changed.Reference);
        Assert.Equal(M(2), changed.Position);
    }

    [Fact]
    public void ChoosingASegmentThatContainsThePointKeepsItAndTheFlyoutDistanceFollows()
    {
        var document = Document(3);
        var editor = Editor(document);
        var support = PlaceSupport(editor, 1.5);
        Assert.True(editor.EditSupport(support.Id));
        var draft = editor.SupportDraft!;

        draft.Reference.SelectedOption = draft.Reference.Options[0];
        Assert.Equal("1500", draft.PositionText);
        draft.Reference.SelectedOption = draft.Reference.Options[2];

        Assert.Equal("500", draft.PositionText);
        Assert.Equal(M(1.5), draft.CanvasPosition);
        Assert.True(draft.IsValid);
        Assert.True(editor.ConfirmSupport());
        Assert.Equal(M(1.5), editor.Document.Supports.Single(s => s.Id == support.Id).Position);
    }

    [Fact]
    public void ATypedDistanceIsMeasuredFromTheReferenceAndNeverChangesIt()
    {
        var document = Document(3);
        var editor = Editor(document);
        var support = PlaceSupport(editor, 1.5);
        Assert.True(editor.EditSupport(support.Id));
        var draft = editor.SupportDraft!;

        draft.PositionText = "750";
        Assert.True(draft.IsValid);
        Assert.Equal(M(1.75), draft.Reference.Position);
        Assert.Equal(document.Segments[1].Id, draft.Reference.SelectedOption!.Segment);

        // Beyond its segment a distance is an error, and the reference stays.
        draft.PositionText = "1500";
        Assert.False(draft.IsValid);
        Assert.Equal(Strings.DistanceBeyondSegment, draft.ErrorText);
        Assert.Equal(document.Segments[1].Id, draft.Reference.SelectedOption!.Segment);

        draft.PositionText = "1000";
        Assert.True(draft.IsValid);
        Assert.Equal(M(2), draft.Reference.Position);
        Assert.False(draft.Reference.HoldEnd);
        Assert.True(editor.ConfirmSupport());
        Assert.Equal(From(document, 1, 1), editor.Document.Supports.Single(s => s.Id == support.Id).Reference);
    }

    [Fact]
    public void ADistanceFromTheBeamStartKeepsTheOldMeaningOfThePositionField()
    {
        var document = Document(3);
        var editor = Editor(document);
        var support = PlaceSupport(editor, 1.5);
        Assert.True(editor.EditSupport(support.Id));
        var draft = editor.SupportDraft!;
        draft.Reference.SelectedOption = draft.Reference.Options[0];

        draft.PositionText = "2750";

        Assert.True(draft.IsValid);
        Assert.Equal(M(2.75), draft.Reference.Position);
        draft.PositionText = "3500";
        Assert.False(draft.IsValid);
        Assert.Equal(Strings.PositionInsideBeam, draft.ErrorText);
        draft.PositionText = "2750";
        Assert.True(editor.ConfirmSupport());
        var moved = editor.Document.Supports.Single(s => s.Id == support.Id);
        Assert.Null(moved.Reference);
        Assert.Equal(M(2.75), moved.Position);
    }

    [Fact]
    public void ABoundPositionShowsTheSegmentLengthAndATypedValueReleasesTheBinding()
    {
        var document = Document(3);
        var editor = Editor(document);
        var support = PlaceSupport(editor, 2, SupportType.Roller);
        Assert.True(editor.EditSupport(support.Id));
        var draft = editor.SupportDraft!;

        draft.Reference.SelectedOption = draft.Reference.Options[2];
        draft.Reference.HoldEnd = true;

        Assert.Equal("1000", draft.PositionText);
        Assert.True(draft.Reference.HoldEnd);
        draft.PositionText = "800";
        Assert.False(draft.Reference.HoldEnd);
        Assert.Equal(M(1.8), draft.Reference.Position);
        Assert.True(editor.ConfirmSupport());
        Assert.Equal(From(document, 1, 0.8), editor.Document.Supports.Single(s => s.Id == support.Id).Reference);
    }

    [Fact]
    public void ABeamStartDistanceStaysWhereItIsWhenASegmentIsInsertedInFront()
    {
        var document = Document(2);
        var editor = Editor(document);
        var support = PlaceSupport(editor, 1.5);
        Assert.True(editor.EditSupport(support.Id));
        editor.SupportDraft!.Reference.SelectedOption = editor.SupportDraft.Reference.Options[0];
        Assert.True(editor.ConfirmSupport());

        var inserted = editor.Document.InsertSegment(0, Segment(1));

        Assert.True(inserted.IsAccepted);
        Assert.Equal(M(1.5), inserted.Document!.Supports.Single(s => s.Id == support.Id).Position);
    }

    [Fact]
    public void OneSegmentShowsNoChoiceOfReferenceButStillOffersTheEndBinding()
    {
        var document = Document(1);
        var editor = Editor(document);
        var end = PlaceSupport(editor, 1, SupportType.Roller);
        var middle = PlaceSupport(editor, 0.5, SupportType.Fixed);

        Assert.True(editor.EditSupport(end.Id));
        Assert.False(editor.SupportDraft!.Reference.ShowReference);
        Assert.True(editor.SupportDraft.Reference.CanHoldEnd);
        Assert.Equal(Strings.HoldAtSegmentEnd, editor.SupportDraft.Reference.HoldEndLabel);
        editor.SupportDraft.Reference.HoldEnd = true;
        Assert.True(editor.ConfirmSupport());
        Assert.Equal(new PositionRef.AtSegmentEnd(document.Segments[0].Id), editor.Document.Supports.Single(s => s.Id == end.Id).Reference);
        Assert.True(editor.EditSupport(middle.Id));
        Assert.False(editor.SupportDraft!.Reference.CanHoldEnd);
        editor.CancelSupportInteraction();
    }

    [Fact]
    public void PointLoadsAndHingesStoreTheirReferenceLikeSupports()
    {
        var document = Document(3);
        var editor = Editor(document);

        editor.ToggleLoadTool(PointLoadKind.Force);
        editor.HoverLoadPlacement(M(2.25));
        Assert.True(editor.PlaceLoad());
        Assert.True(editor.ConfirmLoad());
        Assert.Equal(From(document, 2, 0.25), editor.Document.Loads.Single().Reference);

        editor.ToggleMomentReleaseTool();
        editor.HoverMomentReleasePlacement(M(1.5));
        Assert.True(editor.PlaceMomentRelease());
        Assert.True(editor.ConfirmMomentRelease());
        Assert.Equal(From(document, 1, 0.5), editor.Document.MomentReleases.Single().Reference);
        Assert.Equal(M(1.5), editor.Document.MomentReleases.Single().Position);
    }

    [Fact]
    public void ALineLoadHasOneReferenceForEachEndAndKeepsThemIndependently()
    {
        var document = Document(3);
        var editor = Editor(document);

        editor.ToggleDistributedLoadTool();
        editor.HoverDistributedLoadPlacement(M(0.5));
        Assert.True(editor.PlaceDistributedLoadEndpoint());
        editor.HoverDistributedLoadPlacement(M(2.5));
        Assert.True(editor.PlaceDistributedLoadEndpoint());
        var draft = editor.DistributedLoadDraft!;
        Assert.Equal(From(document, 0, 0.5), draft.StartReference.Current);
        Assert.Equal(From(document, 2, 0.5), draft.EndReference.Current);
        draft.StartReference.SelectedOption = draft.StartReference.Options[0];
        draft.EndText = "1000";
        Assert.True(draft.EndReference.CanHoldEnd);
        draft.EndReference.HoldEnd = true;
        Assert.True(editor.ConfirmDistributedLoad());

        var load = editor.Document.DistributedLoads.Single();
        Assert.Null(load.StartReference);
        Assert.Equal(M(0.5), load.StartPosition);
        Assert.Equal(new PositionRef.AtSegmentEnd(document.Segments[2].Id), load.EndReference);
        var longer = editor.Document.ResizeSegment(document.Segments[2].Id, M(2));
        Assert.Equal(M(4), longer.Document!.DistributedLoads.Single().EndPosition);
        Assert.Equal(M(0.5), longer.Document.DistributedLoads.Single().StartPosition);
    }
}
