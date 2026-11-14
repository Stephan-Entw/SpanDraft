using SpanDraft.Analysis;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;
using Xunit;

namespace SpanDraft.Tests;

/// <summary>Placing, dragging and editing moment releases and the position rules they share with other entities.</summary>
public sealed class DesktopMomentReleaseEditTests
{
    private static Length Mm(double value) => Length.FromMillimeters(value);

    // A pinned end and two rollers: one release is statically determinate.
    private static EditorViewModel Editor(IEnumerable<EditorSupport>? supports = null, IEnumerable<EditorPointLoad>? loads = null,
        params EditorMomentRelease[] releases) => new(
        new EditorDocument(Mm(4000), ProjectTemplates.Material, ProjectTemplates.Section,
            supports ?? [new(Guid.NewGuid(), Mm(0), SupportType.Pinned, "A"), new(Guid.NewGuid(), Mm(2000), SupportType.Roller, "B"),
                new(Guid.NewGuid(), Mm(4000), SupportType.Roller, "C")], loads).WithMomentReleases(releases),
        () => { }, BeamAnalysis.Analyze);

    private static MomentReleaseDraftViewModel Place(EditorViewModel editor, double mm = 1000)
    {
        editor.ToggleMomentReleaseTool();
        Assert.True(editor.IsMomentReleaseTool);
        editor.HoverMomentReleasePlacement(Mm(mm));
        Assert.True(editor.PlaceMomentRelease());
        return editor.MomentReleaseDraft!;
    }

    [Fact]
    public void ThePlacementToolHoversPlacesAndCommitsAReleaseWithItsNextNameAndCounter()
    {
        var editor = Editor();
        editor.ToggleMomentReleaseTool();
        Assert.Equal("G1", editor.MomentReleasePreviewName);
        editor.HoverMomentReleasePlacement(Mm(1000));
        Assert.False(editor.MomentReleasePreview!.IsInvalid);
        Assert.True(editor.HasCoordinate);
        Assert.True(editor.PlaceMomentRelease());
        Assert.True(editor.IsMomentReleaseFlyoutVisible);
        Assert.Equal(MomentReleaseInteraction.NewDraft, editor.MomentReleaseState);
        Assert.True(editor.MomentReleaseDraft!.IsValid);
        Assert.Empty(editor.Document.MomentReleases);
        Assert.True(editor.ConfirmMomentRelease());
        var release = Assert.Single(editor.Document.MomentReleases);
        Assert.Equal((Mm(1000), "G1"), (release.Position, release.Name));
        Assert.Equal(2, editor.Document.NamingState.NextMomentReleaseNumber);
        Assert.Equal(MomentReleaseInteraction.Neutral, editor.MomentReleaseState);
        Assert.True(editor.Presentation.IsSuccess);
        Assert.NotNull(editor.Presentation.Result!.Solution.Nodes.Single(n => n.Position == Mm(1000)).RotationLeftRadians);
        Assert.Single(editor.Session.UndoHistory);
    }

    [Fact]
    public void ARepeatedToolClickCancelsAndEveryOtherToolCancelsTheReleaseTool()
    {
        var editor = Editor();
        editor.ToggleMomentReleaseTool();
        editor.ToggleMomentReleaseTool();
        Assert.Equal(MomentReleaseInteraction.Neutral, editor.MomentReleaseState);
        editor.ToggleMomentReleaseTool();
        editor.ToggleSupportTool(SupportType.Roller);
        Assert.Equal(MomentReleaseInteraction.Neutral, editor.MomentReleaseState);
        editor.ToggleMomentReleaseTool();
        editor.ToggleLoadTool(PointLoadKind.Force);
        Assert.False(editor.IsMomentReleaseTool);
        editor.ToggleMomentReleaseTool();
        editor.CancelEditorInteraction();
        Assert.False(editor.IsMomentReleaseTool);
    }

    [Theory]
    [InlineData(0, MomentReleaseConflict.NotInsideBeam)]
    [InlineData(4000, MomentReleaseConflict.NotInsideBeam)]
    [InlineData(1000, MomentReleaseConflict.Duplicate)]
    [InlineData(2000, MomentReleaseConflict.FixedSupport)]
    [InlineData(2500, MomentReleaseConflict.RotationalSpring)]
    [InlineData(3000, MomentReleaseConflict.PointMoment)]
    [InlineData(1500, MomentReleaseConflict.None)]
    [InlineData(2000.5, MomentReleaseConflict.None)]
    public void TheReleaseRulesMatchTheCoreValidatorAtEveryCollidingEntity(double mm, MomentReleaseConflict expected)
    {
        EditorSupport[] supports =
        [
            new(Guid.NewGuid(), Mm(0), SupportType.Pinned, "A"), new(Guid.NewGuid(), Mm(2000), SupportType.Fixed, "B"),
            new(Guid.NewGuid(), Mm(2500), SupportType.Spring, "C", new(null, RotationalStiffness.FromNewtonMetersPerRadian(5e3))),
            new(Guid.NewGuid(), Mm(4000), SupportType.Roller, "D")
        ];
        var editor = Editor(supports, [EditorPointLoad.Create(Guid.NewGuid(), Mm(3000), PointLoadKind.Moment, 10, "M1")],
            new EditorMomentRelease(Guid.NewGuid(), Mm(1000), "G1"));
        var document = editor.Document;
        Assert.Equal(expected, MomentReleaseRules.Conflict(document, Mm(mm)));
        // Whatever the rules reject, Core rejects too, and nothing else.
        var withRelease = document.WithMomentReleases([.. document.MomentReleases.Where(r => r.Position != Mm(mm)),
            new EditorMomentRelease(Guid.NewGuid(), Mm(mm), "G9")]);
        bool coreRejects = SpanDraft.Core.Validation.BeamModelValidator.Validate(withRelease.ToBeamModel())
            .Any(e => e.Code is >= SpanDraft.Core.Validation.ValidationErrorCode.MomentReleaseNotInsideBeam);
        Assert.Equal(expected != MomentReleaseConflict.None && expected != MomentReleaseConflict.Duplicate, coreRejects);
    }

    [Fact]
    public void AnInvalidPlacementShowsItsCauseAndCannotBePlaced()
    {
        var editor = Editor(releases: new EditorMomentRelease(Guid.NewGuid(), Mm(1000), "G1"));
        editor.ToggleMomentReleaseTool();
        foreach (double mm in new[] { 0, 4000, 1000, 2000 })
        {
            editor.HoverMomentReleasePlacement(Mm(mm));
            Assert.True(editor.MomentReleasePreview!.IsInvalid == (mm != 2000), $"{mm}");
            Assert.Equal(mm != 2000, editor.HasMomentReleaseFeedback);
            Assert.Equal(mm == 2000, editor.PlaceMomentRelease());
            if (mm == 2000) editor.CancelMomentReleaseInteraction();
            if (mm == 2000) editor.ToggleMomentReleaseTool();
        }
    }

    [Fact]
    public void TheFlyoutBufferValidatesNameAndPositionWithoutTouchingTheDocument()
    {
        var editor = Editor(releases: new EditorMomentRelease(Guid.NewGuid(), Mm(1000), "G1"));
        var draft = Place(editor, 3000);
        Assert.Equal("G2", draft.NameText);
        draft.PositionText = "0";
        Assert.True(draft.HasError);
        Assert.True(editor.MomentReleasePreview!.IsInvalid);
        draft.PositionText = "1000";
        Assert.True(draft.HasError);
        draft.PositionText = "2000";
        Assert.True(draft.IsValid);
        draft.NameText = "g1";
        Assert.True(draft.HasNameError);
        draft.NameText = "g";
        Assert.True(draft.HasNameError);
        draft.NameText = "Gerber hinge";
        Assert.True(draft.IsValid);
        Assert.Single(editor.Document.MomentReleases);
        Assert.True(editor.ConfirmMomentRelease());
        Assert.Equal([Mm(1000), Mm(2000)], editor.Document.MomentReleases.Select(r => r.Position));
        Assert.Equal("Gerber hinge", editor.Document.MomentReleases[1].Name);
        // The proposed number is consumed even though the name was typed over.
        Assert.Equal(3, editor.Document.NamingState.NextMomentReleaseNumber);
    }

    [Fact]
    public void CancelAndAnInvalidConfirmLeaveTheDocumentAndTheCounterUntouched()
    {
        var editor = Editor();
        var original = editor.Document;
        var draft = Place(editor);
        draft.PositionText = "5000";
        Assert.False(editor.ConfirmMomentRelease());
        Assert.Same(original, editor.Document);
        editor.CancelMomentReleaseInteraction();
        Assert.Same(original, editor.Document);
        Assert.Equal(1, editor.Document.NamingState.NextMomentReleaseNumber);
        Assert.Equal("G1", Place(editor).NameText);
    }

    [Fact]
    public void EditingMovesRenamesAndDeletesWhileUndoRestoresEachStep()
    {
        var id = Guid.NewGuid();
        var editor = Editor(releases: new EditorMomentRelease(id, Mm(1000), "G1"));
        Assert.True(editor.EditMomentRelease(id));
        Assert.Equal(MomentReleaseInteraction.EditDraft, editor.MomentReleaseState);
        Assert.Equal(id, editor.HiddenMomentReleaseId);
        editor.MomentReleaseDraft!.PositionText = "1500";
        editor.MomentReleaseDraft.NameText = "Hinge 1";
        Assert.True(editor.ConfirmMomentRelease());
        Assert.Equal((Mm(1500), "Hinge 1"), (editor.Document.MomentReleases[0].Position, editor.Document.MomentReleases[0].Name));
        Assert.Equal(1, editor.Document.NamingState.NextMomentReleaseNumber);
        Assert.True(editor.Undo());
        Assert.Equal((Mm(1000), "G1"), (editor.Document.MomentReleases[0].Position, editor.Document.MomentReleases[0].Name));
        Assert.True(editor.EditMomentRelease(id));
        editor.DeleteMomentRelease();
        Assert.Empty(editor.Document.MomentReleases);
        Assert.True(editor.Undo());
        Assert.Single(editor.Document.MomentReleases);
        Assert.True(editor.Redo());
        Assert.Empty(editor.Document.MomentReleases);
    }

    [Fact]
    public void DraggingMovesTheDraftOnlyOnValidPositionsAndWithoutSnappingToTheEnds()
    {
        var id = Guid.NewGuid();
        var editor = Editor(releases: new EditorMomentRelease(id, Mm(1000), "G1"));
        Assert.True(editor.BeginMomentReleaseDrag(id));
        Assert.Equal(MomentReleaseInteraction.Drag, editor.MomentReleaseState);
        editor.UpdateMomentReleaseDrag(Mm(1200));
        Assert.False(editor.MomentReleasePreview!.IsInvalid);
        editor.UpdateMomentReleaseDrag(Mm(2000));
        Assert.False(editor.MomentReleasePreview!.IsInvalid);
        editor.UpdateMomentReleaseDrag(Mm(0));
        Assert.True(editor.MomentReleasePreview!.IsInvalid);
        Assert.False(editor.EndMomentReleaseDrag());
        Assert.Equal(MomentReleaseInteraction.Neutral, editor.MomentReleaseState);
        Assert.Equal(Mm(1000), editor.Document.MomentReleases[0].Position);
        Assert.True(editor.BeginMomentReleaseDrag(id));
        editor.UpdateMomentReleaseDrag(Mm(1750));
        Assert.True(editor.EndMomentReleaseDrag());
        Assert.Equal(Mm(1750), editor.MomentReleaseDraft!.CanvasPosition);
        Assert.Equal(MomentReleaseInteraction.EditDraft, editor.MomentReleaseState);
        Assert.True(editor.ConfirmMomentRelease());
        Assert.Equal(Mm(1750), editor.Document.MomentReleases[0].Position);
    }

    [Fact]
    public void ADragOntoAFixedSupportOrAMomentIsInvalid()
    {
        var id = Guid.NewGuid();
        var editor = Editor([new(Guid.NewGuid(), Mm(0), SupportType.Pinned, "A"), new(Guid.NewGuid(), Mm(2000), SupportType.Fixed, "B"),
            new(Guid.NewGuid(), Mm(4000), SupportType.Roller, "C")],
            [EditorPointLoad.Create(Guid.NewGuid(), Mm(3000), PointLoadKind.Moment, 10, "M1")],
            new EditorMomentRelease(id, Mm(1000), "G1"));
        Assert.True(editor.BeginMomentReleaseDrag(id));
        foreach (double mm in new[] { 2000, 3000 })
        {
            editor.UpdateMomentReleaseDrag(Mm(mm));
            Assert.True(editor.MomentReleasePreview!.IsInvalid);
            Assert.True(editor.HasMomentReleaseFeedback);
        }
        editor.UpdateMomentReleaseDrag(Mm(1500));
        Assert.False(editor.MomentReleasePreview!.IsInvalid);
        editor.CancelEditorInteraction();
    }

    [Fact]
    public void ASupportOrMomentThatCollidesWithAReleaseIsRefusedAtPlacementDragAndInTheFlyout()
    {
        var editor = Editor(releases: new EditorMomentRelease(Guid.NewGuid(), Mm(1000), "G1"));
        // A fixed support and a rotational spring cannot stand on the release; a roller and a pinned support can.
        editor.ToggleSupportTool(SupportType.Fixed);
        editor.HoverPlacement(Mm(1000));
        Assert.True(editor.Preview!.IsInvalid);
        Assert.False(editor.PlaceSupport());
        Assert.False(string.IsNullOrEmpty(editor.SupportFeedback));
        editor.ToggleSupportTool(SupportType.Roller);
        editor.HoverPlacement(Mm(1000));
        Assert.False(editor.Preview!.IsInvalid);
        Assert.True(editor.PlaceSupport());
        var draft = editor.SupportDraft!;
        draft.Type = SupportType.Fixed;
        Assert.False(draft.IsValid);
        Assert.False(editor.ConfirmSupport());
        draft.Type = SupportType.Spring;
        draft.TranslationalText = "1e6";
        Assert.True(draft.IsValid);
        draft.HasRotational = true;
        draft.RotationalText = "5000";
        Assert.False(draft.IsValid);
        draft.HasRotational = false;
        Assert.True(draft.IsValid);
        draft.Type = SupportType.Pinned;
        Assert.True(editor.ConfirmSupport());

        editor.ToggleLoadTool(PointLoadKind.Moment);
        editor.HoverLoadPlacement(Mm(1000));
        Assert.True(editor.LoadPreview!.IsInvalid);
        Assert.False(editor.PlaceLoad());
        editor.ToggleLoadTool(PointLoadKind.Force);
        editor.HoverLoadPlacement(Mm(1000));
        Assert.False(editor.LoadPreview!.IsInvalid);
        Assert.True(editor.PlaceLoad());
        Assert.True(editor.ConfirmLoad());
        editor.ToggleLoadTool(PointLoadKind.Moment);
        editor.HoverLoadPlacement(Mm(1500));
        Assert.True(editor.PlaceLoad());
        editor.LoadDraft!.PositionText = "1000";
        Assert.False(editor.LoadDraft.IsValid);
        Assert.False(editor.ConfirmLoad());
    }

    [Fact]
    public void ADraggedSupportOrMomentIsInvalidOnARelease()
    {
        var fixedId = Guid.NewGuid();
        var editor = Editor([new(Guid.NewGuid(), Mm(0), SupportType.Pinned, "A"), new(fixedId, Mm(3000), SupportType.Fixed, "B"),
                new(Guid.NewGuid(), Mm(4000), SupportType.Roller, "C")],
            [EditorPointLoad.Create(Guid.NewGuid(), Mm(500), PointLoadKind.Moment, 10, "M1")],
            new EditorMomentRelease(Guid.NewGuid(), Mm(1000), "G1"));
        Assert.True(editor.BeginSupportDrag(fixedId));
        editor.UpdateSupportDrag(Mm(1000));
        Assert.True(editor.Preview!.IsInvalid);
        editor.UpdateSupportDrag(Mm(1100));
        Assert.False(editor.Preview!.IsInvalid);
        editor.CancelEditorInteraction();
        Assert.True(editor.BeginLoadDrag(editor.Document.Loads[0].Id));
        editor.UpdateLoadDrag(Mm(1000));
        Assert.True(editor.LoadPreview!.IsInvalid);
        editor.UpdateLoadDrag(Mm(1100));
        Assert.False(editor.LoadPreview!.IsInvalid);
    }

    [Fact]
    public void TheBeamLengthCannotShrinkOverAReleaseAndTheConflictNamesIt()
    {
        var release = new EditorMomentRelease(Guid.NewGuid(), Mm(3500), "G1");
        var editor = Editor(supports: [new(Guid.NewGuid(), Mm(0), SupportType.Pinned, "A"), new(Guid.NewGuid(), Mm(2000), SupportType.Roller, "B"),
            new(Guid.NewGuid(), Mm(3000), SupportType.Roller, "C")], releases: release);
        Assert.Contains(release.Id, BeamLengthConstraints.BlockingEntities(editor.Document, Mm(3500)));
        Assert.DoesNotContain(release.Id, BeamLengthConstraints.BlockingEntities(editor.Document, Mm(3500.001)));
        Assert.NotNull(BeamLengthConstraints.Error(editor.Document, Mm(3500)));
    }

    [Fact]
    public void AnUnstableModelWithReleasesNamesTheReleasesInItsStatusText()
    {
        // Pinned end and one roller with a release in the middle: a mechanism the solver reports exactly.
        var editor = Editor([new(Guid.NewGuid(), Mm(0), SupportType.Pinned, "A"), new(Guid.NewGuid(), Mm(4000), SupportType.Roller, "B")],
            releases: new EditorMomentRelease(Guid.NewGuid(), Mm(2000), "G1"));
        Assert.Equal(AnalysisPresentationKind.UnstableModel, editor.Presentation.Kind);
        Assert.Contains(SpanDraft.Desktop.Resources.Strings.InsufficientSupportWithMomentReleases, editor.Presentation.StatusText);
        var without = Editor([new(Guid.NewGuid(), Mm(0), SupportType.Pinned, "A")]);
        Assert.Equal(AnalysisPresentationKind.UnstableModel, without.Presentation.Kind);
        Assert.DoesNotContain(SpanDraft.Desktop.Resources.Strings.InsufficientSupportWithMomentReleases, without.Presentation.StatusText);
    }

    [Fact]
    public void AReleaseSurvivesAProjectSetupEditAndALabelOffset()
    {
        var id = Guid.NewGuid();
        var editor = Editor(releases: new EditorMomentRelease(id, Mm(1000), "G1"));
        Assert.True(editor.SetAnnotationOffset(id, new(5, -9)));
        Assert.Equal(new AnnotationOffset(5, -9), editor.EditorPresentation.AnnotationOffsets[id]);
        Assert.True(editor.ApplySetup(Mm(4500), [], [new(ProjectTemplates.Material, ProjectTemplates.Section, SpanDraft.Core.Sections.SectionAxisDesignation.Y)],
            editor.Session.CurrentRevision.State.Info));
        Assert.Equal(Mm(1000), Assert.Single(editor.Document.MomentReleases).Position);
        Assert.True(editor.EditorPresentation.AnnotationOffsets.ContainsKey(id));
        editor.Undo();
        Assert.Single(editor.Document.MomentReleases);
    }

    [Fact]
    public void EditingOnlyTheNameOrDraggingBackOntoTheOwnPositionIsNotAConflictWithItself()
    {
        var id = Guid.NewGuid();
        var editor = Editor(releases: new EditorMomentRelease(id, Mm(1000), "G1"));
        Assert.True(editor.EditMomentRelease(id));
        editor.MomentReleaseDraft!.NameText = "Hinge A";
        Assert.True(editor.MomentReleaseDraft.IsValid);
        Assert.True(editor.ConfirmMomentRelease());
        Assert.Equal((Mm(1000), "Hinge A"), (editor.Document.MomentReleases[0].Position, editor.Document.MomentReleases[0].Name));
        Assert.Equal(MomentReleaseConflict.None, MomentReleaseRules.Conflict(editor.Document, Mm(1000), id));
        Assert.Equal(MomentReleaseConflict.Duplicate, MomentReleaseRules.Conflict(editor.Document, Mm(1000)));
        Assert.True(editor.BeginMomentReleaseDrag(id));
        editor.UpdateMomentReleaseDrag(Mm(1000));
        Assert.False(editor.MomentReleasePreview!.IsInvalid);
        Assert.True(editor.EndMomentReleaseDrag());
    }

    [Fact]
    public void APointForceOrALoadOtherThanAMomentNeverBlocksAReleaseAtItsPosition()
    {
        var editor = Editor(loads: [EditorPointLoad.Create(Guid.NewGuid(), Mm(1000), PointLoadKind.Force, -1000, "F1"),
            EditorPointLoad.Create(Guid.NewGuid(), Mm(1500), PointLoadKind.Moment, 10, "M1")]);
        Assert.Equal(MomentReleaseConflict.None, MomentReleaseRules.Conflict(editor.Document, Mm(1000)));
        Assert.Equal(MomentReleaseConflict.PointMoment, MomentReleaseRules.Conflict(editor.Document, Mm(1500)));
        var draft = Place(editor, 1000);
        Assert.True(draft.IsValid);
        Assert.True(editor.ConfirmMomentRelease());
        Assert.Equal(Mm(1000), Assert.Single(editor.Document.MomentReleases).Position);
        Assert.True(editor.Presentation.IsSuccess);
        var node = editor.Presentation.Result!.Solution.Nodes.Single(n => n.Position == Mm(1000));
        Assert.NotNull(node.RotationLeftRadians);
    }
}
