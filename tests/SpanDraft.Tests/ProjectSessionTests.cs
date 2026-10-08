using SpanDraft.Analysis;
using SpanDraft.Core.Supports;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;
using Xunit;
using static SpanDraft.Tests.ProjectTestSupport;

namespace SpanDraft.Tests;

public sealed class ProjectSessionTests
{
    [Fact]
    public void HistoryUsesExistingRevisionIdsAndBranchesOnlyForRealChanges()
    {
        var state = State();
        var session = ProjectSession.Create(state);
        var first = session.CurrentRevision;
        Assert.False(session.Commit(State())); // Independent Material/Section objects are equal by content.
        Assert.Empty(session.UndoHistory);
        Assert.True(session.Commit(state with { Document = state.Document with { Length = M(2) } }));
        var second = session.CurrentRevision;
        Assert.NotEqual(first.RevisionId, second.RevisionId);
        Assert.True(session.Undo()); Assert.Same(first, session.CurrentRevision);
        Assert.False(session.Commit(State())); Assert.True(session.CanRedo);
        Assert.True(session.Redo()); Assert.Same(second, session.CurrentRevision);
        Assert.True(session.Undo());
        Assert.True(session.Commit(state with { Presentation = state.Presentation.WithOffset(SupportId, new(100, 200)) }));
        Assert.False(session.CanRedo); Assert.Empty(session.RedoHistory);
    }

    [Fact]
    public void SavepointTracksIdentityEvenAfterBranchingOrHistoryEviction()
    {
        var state = State();
        var session = ProjectSession.Create(state);
        Assert.True(session.IsDirty);
        session.MarkSaved(session.CurrentRevision, TestPath("project.spandraft"));
        var savepoint = session.SavedRevisionId;
        Assert.False(session.IsDirty);
        session.Commit(state with { Document = state.Document with { Length = M(2) } });
        Assert.True(session.IsDirty);
        session.Undo(); Assert.False(session.IsDirty);
        session.Redo(); Assert.True(session.IsDirty);
        for (int i = 0; i < 210; i++) session.Commit(state with { Document = state.Document with { Length = M(3 + i) } });
        Assert.Equal(200, session.UndoHistory.Count);
        Assert.Equal(savepoint, session.SavedRevisionId);
        for (int i = 0; i < 200; i++) Assert.True(session.Undo());
        Assert.False(session.Undo()); Assert.True(session.IsDirty);
        Assert.Equal(200, session.RedoHistory.Count);
        for (int i = 0; i < 200; i++) Assert.True(session.Redo());
        Assert.Equal(200, session.UndoHistory.Count);
    }

    [Fact]
    public void SaveRetainsHistoryAndAnUnreachableSavepointDoesNotBecomeCleanByContent()
    {
        var state = State();
        var session = ProjectSession.Create(state);
        var changed = state with { Document = state.Document with { Length = M(2) } };
        session.Commit(changed);
        session.MarkSaved(session.CurrentRevision, TestPath("saved.spandraft"));
        var saved = session.SavedRevisionId;
        Assert.Single(session.UndoHistory);
        session.Undo(); Assert.True(session.IsDirty);
        session.Commit(changed);
        Assert.True(session.IsDirty); Assert.Equal(saved, session.SavedRevisionId); Assert.False(session.CanRedo);
    }

    [Fact]
    public void HistoricalNamingAndOffsetsAreRestoredTogetherIncludingDeletion()
    {
        var state = State();
        var session = ProjectSession.Create(state);
        var document = state.Document.WithSupports([], new(20, 11, 12, 13));
        session.Commit(new(document, state.Presentation.RetainEntities(document)));
        Assert.False(session.CurrentRevision.State.Presentation.AnnotationOffsets.ContainsKey(SupportId));
        session.Undo();
        Assert.Same(state, session.CurrentRevision.State);
        Assert.Equal(10, session.CurrentRevision.State.Document.NamingState.NextSupportOrdinal);
        session.Redo();
        Assert.Equal(20, session.CurrentRevision.State.Document.NamingState.NextSupportOrdinal);
        Assert.Throws<ArgumentException>(() => session.Commit(state));
    }

    [Fact]
    public void CommitUndoRedoShareTheAnalysisBoundaryAndCancelTransientBuffersFirst()
    {
        int calls = 0;
        var state = State();
        var session = ProjectSession.Create(state);
        var editor = EditorViewModel.ForSession(session, () => { }, b => { calls++; return BeamAnalysis.Analyze(b); });
        session.Commit(state with { Document = state.Document with { Length = M(2) } });
        Assert.Equal(2, calls);
        editor.SetAnnotationOffset(SupportId, new(90, 80)); Assert.Equal(2, calls);
        editor.EditSupport(SupportId); editor.SupportDraft!.NameText = "Invalid draft"; editor.SupportDraft.PositionText = "-";
        editor.DimensionLength.Begin(); editor.DimensionLength.Text = "invalid";
        editor.PreviewAnnotationOffset(SupportId, new(33, 44));
        bool cancelling = false;
        editor.InteractionsCancelling += () => cancelling = true;
        Assert.True(editor.Undo()); Assert.True(cancelling);
        Assert.Null(editor.SupportDraft); Assert.False(editor.DimensionLength.IsEditing); Assert.False(editor.DimensionLength.HasError);
        Assert.True(editor.RenderPresentation.ContentEquals(editor.EditorPresentation)); Assert.Equal(2, calls);
        Assert.True(editor.Undo()); Assert.Equal(3, calls);
        Assert.True(editor.Redo()); Assert.Equal(4, calls);
        Assert.True(editor.Redo()); Assert.Equal(4, calls);
        Assert.Same(session.CurrentRevision.State.Document, editor.Document);
        Assert.Same(session.CurrentRevision.State.Presentation, editor.EditorPresentation);
    }

    [Fact]
    public void RenameIsUndoableWithoutAnalysisAndPreviewCancelNeverChangesRevision()
    {
        int calls = 0;
        var editor = new EditorViewModel(State().Document, () => { }, b => { calls++; return BeamAnalysis.Analyze(b); });
        var revision = editor.Session.CurrentRevision;
        editor.PreviewAnnotationOffset(SupportId, new(30, 40));
        editor.CancelEditorInteraction();
        Assert.Same(revision, editor.Session.CurrentRevision); Assert.Empty(editor.Session.UndoHistory);
        editor.EditSupport(SupportId); editor.SupportDraft!.NameText = "Bearing";
        Assert.True(editor.ConfirmSupport()); Assert.Single(editor.Session.UndoHistory); Assert.Equal(1, calls);
        editor.Undo(); Assert.Equal("A", editor.Document.Supports[0].Name); Assert.Equal(1, calls);
        editor.Redo(); Assert.Equal("Bearing", editor.Document.Supports[0].Name); Assert.Equal(1, calls);
    }

    [Fact]
    public void BusySessionRejectsMutationsButAllowsTheCapturedRevisionToBeSaved()
    {
        var state = State(); var session = ProjectSession.Create(state);
        session.Commit(state with { Document = state.Document with { Length = M(2) } });
        session.SetBusy(true);
        Assert.False(session.CanUndo); Assert.False(session.Commit(state)); Assert.False(session.Undo()); Assert.False(session.Redo());
        session.MarkSaved(session.CurrentRevision, TestPath("file.spandraft")); Assert.False(session.IsDirty);
        session.SetBusy(false); Assert.True(session.CanUndo);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void NativeCaptureLossDuringSaveRestoresPointerDraftWithoutDiscardingItsText(int kind)
    {
        var editor = new EditorViewModel(State().Document, () => { });
        var revision = editor.Session.CurrentRevision;
        if (kind == 0)
        {
            editor.EditSupport(SupportId); var draft = editor.SupportDraft!;
            draft.PositionText = "unfinished"; draft.NameText = "Pending";
            editor.BeginSupportDrag(SupportId); editor.UpdateSupportDrag(M(.9));
            editor.CancelPointerDrag();
            Assert.Same(draft, editor.SupportDraft); Assert.Equal("unfinished", draft.PositionText);
            Assert.Equal("Pending", draft.NameText); Assert.Equal(SupportInteraction.EditDraft, editor.Interaction);
        }
        else if (kind == 1)
        {
            editor.EditLoad(LoadId); var draft = editor.LoadDraft!;
            draft.PositionText = "unfinished"; draft.ValueText = "-";
            editor.BeginLoadDrag(LoadId); editor.UpdateLoadDrag(M(.9));
            editor.CancelPointerDrag();
            Assert.Same(draft, editor.LoadDraft); Assert.Equal("unfinished", draft.PositionText);
            Assert.Equal("-", draft.ValueText); Assert.Equal(LoadInteraction.EditDraft, editor.LoadState);
        }
        else
        {
            var id = editor.Document.DistributedLoads[0].Id;
            editor.EditDistributedLoad(id); var draft = editor.DistributedLoadDraft!;
            draft.StartText = "unfinished"; draft.IntensityText = "-";
            editor.BeginDistributedLoadDrag(id, DistributedLoadEndpoint.End); editor.UpdateDistributedLoadDrag(M(.9));
            editor.CancelPointerDrag();
            Assert.Same(draft, editor.DistributedLoadDraft); Assert.Equal("unfinished", draft.StartText);
            Assert.Equal("-", draft.IntensityText); Assert.Equal(DistributedLoadInteraction.EditDraft, editor.DistributedLoadState);
        }
        Assert.Same(revision, editor.Session.CurrentRevision); Assert.Empty(editor.Session.UndoHistory);
    }
}
