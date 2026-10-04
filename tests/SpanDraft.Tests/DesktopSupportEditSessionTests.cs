using System.Globalization;
using SpanDraft.Analysis;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Controls;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;
using Xunit;

namespace SpanDraft.Tests;

public sealed class DesktopSupportEditSessionTests
{
    private static Length Mm(double value) => Length.FromMillimeters(value);
    private sealed class Session
    {
        public int Analyses { get; private set; }
        public EditorViewModel Editor { get; }
        public Session(params double[] positions) => Editor = new(
            new EditorDocument(Mm(1000), ProjectTemplates.Material, ProjectTemplates.Section,
                positions.Select(p => new EditorSupport(Guid.NewGuid(), Mm(p), SupportType.Pinned))), () => { },
            beam => { Analyses++; return BeamAnalysis.Analyze(beam); });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PositionKeystrokesNeverMoveExistingOrNewPreviewUntilCommit(bool existing)
    {
        var s = existing ? new Session(689) : new Session();
        var editor = s.Editor;
        var document = editor.Document;
        if (existing) Assert.True(editor.EditSupport(document.Supports[0].Id));
        else
        {
            editor.ToggleSupportTool(SupportType.Pinned);
            editor.HoverPlacement(Mm(689));
            Assert.True(editor.PlaceSupport());
        }
        var draft = editor.SupportDraft!;
        foreach (var text in new[] { "7", "70", "700" })
        {
            draft.PositionText = text;
            Assert.True(draft.IsValid);
            Assert.Equal(Mm(689), draft.CanvasPosition);
            Assert.Equal(Mm(689), editor.Preview!.Position);
            Assert.Same(document, editor.Document);
            Assert.Equal(1, s.Analyses);
        }
        Assert.True(draft.TryGetValue(out var input));
        Assert.Equal(Mm(700), input);
        Assert.True(editor.ConfirmSupport());
        Assert.Equal(Mm(700), Assert.Single(editor.Document.Supports).Position);
        Assert.Equal(2, s.Analyses);
        Assert.False(editor.IsSupportFlyoutVisible);
        Assert.Null(editor.Preview);
        if (existing) Assert.Equal(document.Supports[0].Id, editor.Document.Supports[0].Id);
    }

    [Theory]
    [InlineData(false, "")]
    [InlineData(false, "NaN")]
    [InlineData(false, "-1")]
    [InlineData(false, "1001")]
    [InlineData(false, "800")]
    [InlineData(true, "")]
    [InlineData(true, "NaN")]
    [InlineData(true, "-1")]
    [InlineData(true, "1001")]
    [InlineData(true, "800")]
    public void InvalidInputAndEnterKeepCanvasPositionAndSession(bool existing, string text)
    {
        var s = existing ? new Session(689, 800) : new Session(800);
        var document = s.Editor.Document;
        if (existing) s.Editor.EditSupport(document.Supports[0].Id);
        else
        {
            s.Editor.ToggleSupportTool(SupportType.Pinned);
            s.Editor.HoverPlacement(Mm(689));
            s.Editor.PlaceSupport();
        }
        var draft = s.Editor.SupportDraft!;
        draft.PositionText = text;
        Assert.False(draft.IsValid);
        Assert.False(s.Editor.ConfirmSupport());
        Assert.Equal(Mm(689), s.Editor.Preview!.Position);
        Assert.True(s.Editor.Preview.IsInvalid);
        Assert.Same(draft, s.Editor.SupportDraft);
        Assert.True(s.Editor.IsSupportFlyoutVisible);
        Assert.Same(document, s.Editor.Document);
        Assert.Equal(1, s.Analyses);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CancellingTextChangesPreservesOriginalDocumentOrEmptyPlacement(bool existing)
    {
        var s = existing ? new Session(689) : new Session();
        var document = s.Editor.Document;
        if (existing) s.Editor.EditSupport(document.Supports[0].Id);
        else
        {
            s.Editor.ToggleSupportTool(SupportType.Roller);
            s.Editor.HoverPlacement(Mm(689));
            s.Editor.PlaceSupport();
        }
        s.Editor.SupportDraft!.PositionText = "700";
        s.Editor.CancelSupportInteraction(); // Same command used by Escape and outside-dismiss.
        Assert.Same(document, s.Editor.Document);
        Assert.Equal(1, s.Analyses);
        Assert.Equal(SupportInteraction.Neutral, s.Editor.Interaction);
        Assert.Null(s.Editor.SupportDraft);
    }

    [Fact]
    public void LiveTypeChangeNeverMovesTextBufferedPosition()
    {
        var s = new Session(689);
        s.Editor.EditSupport(s.Editor.Document.Supports[0].Id);
        var draft = s.Editor.SupportDraft!;
        draft.PositionText = "700";
        draft.Type = SupportType.Roller;
        Assert.Equal(Mm(689), s.Editor.Preview!.Position);
        Assert.Equal(SupportType.Roller, s.Editor.Preview.Type);
        Assert.True(s.Editor.ConfirmSupport());
        Assert.Equal(Mm(700), s.Editor.Document.Supports[0].Position);
        Assert.Equal(SupportType.Roller, s.Editor.Document.Supports[0].Type);
        Assert.Equal(2, s.Analyses);
    }

    [Fact]
    public void ThreeDragsAndActiveClicksShareOneDraftAndOneFinalCommit()
    {
        var s = new Session(300);
        var editor = s.Editor;
        var document = editor.Document;
        var support = document.Supports[0];
        Assert.True(editor.EditSupport(support.Id));
        var draft = editor.SupportDraft!;
        draft.Type = SupportType.Roller;
        foreach (double position in new[] { 400.0, 500.0, 450.0 })
        {
            Assert.True(editor.EditSupport(support.Id));
            Assert.Same(draft, editor.SupportDraft);
            Assert.True(editor.BeginSupportDrag(support.Id));
            Assert.False(editor.IsSupportFlyoutVisible);
            Assert.Same(draft, editor.SupportDraft);
            Assert.False(editor.ConfirmSupport());
            editor.DeleteSupport();
            editor.UpdateSupportDrag(Mm(position));
            Assert.True(editor.EndSupportDrag());
            Assert.Same(draft, editor.SupportDraft);
            Assert.Equal(Mm(position), draft.CanvasPosition);
            Assert.Equal(UiNumbers.Format(position), draft.PositionText);
            Assert.Equal(SupportType.Roller, editor.Preview!.Type);
            Assert.True(editor.IsSupportFlyoutVisible);
            Assert.True(editor.EditSupport(support.Id));
            Assert.Same(document, editor.Document);
            Assert.Equal(1, s.Analyses);
        }
        Assert.True(editor.ConfirmSupport());
        Assert.Equal(support.Id, editor.Document.Supports[0].Id);
        Assert.Equal(Mm(450), editor.Document.Supports[0].Position);
        Assert.Equal(SupportType.Roller, editor.Document.Supports[0].Type);
        Assert.Equal(2, s.Analyses);
        Assert.False(editor.ConfirmSupport());
        Assert.Equal(2, s.Analyses);
    }

    [Fact]
    public void ClickKeepsManualBufferButDragStartsAtCanvasAndReplacesIt()
    {
        var s = new Session(300);
        var editor = s.Editor;
        var id = editor.Document.Supports[0].Id;
        editor.EditSupport(id);
        var draft = editor.SupportDraft!;
        draft.PositionText = "700";
        editor.EditSupport(id);
        Assert.Equal("700", draft.PositionText);
        Assert.True(editor.BeginSupportDrag(id));
        Assert.Equal(Mm(300), editor.Preview!.Position);
        editor.UpdateSupportDrag(Mm(400));
        Assert.True(editor.EndSupportDrag());
        Assert.Equal("400", draft.PositionText);
        Assert.Equal(Mm(400), draft.CanvasPosition);
        Assert.Equal(1, s.Analyses);
        Assert.True(editor.BeginSupportDrag(id));
        Assert.Equal(Mm(400), editor.Preview!.Position);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidRedragRestoresPriorDraftTextTypeAndCanvas(bool outside)
    {
        var s = new Session(300, 800);
        var editor = s.Editor;
        var document = editor.Document;
        var id = document.Supports[0].Id;
        editor.EditSupport(id);
        var draft = editor.SupportDraft!;
        editor.BeginSupportDrag(id);
        editor.UpdateSupportDrag(Mm(400));
        editor.EndSupportDrag();
        draft.Type = SupportType.Roller;
        draft.PositionText = "invalid";
        var preview = draft.Preview;
        Assert.True(editor.BeginSupportDrag(id));
        editor.UpdateSupportDrag(outside ? null : Mm(800));
        Assert.False(editor.EndSupportDrag());
        Assert.Same(draft, editor.SupportDraft);
        Assert.Equal(preview, editor.Preview);
        Assert.Equal("invalid", draft.PositionText);
        Assert.Equal(SupportType.Roller, draft.Type);
        Assert.True(editor.IsSupportFlyoutVisible);
        Assert.True(editor.HasSupportFeedback);
        Assert.Same(document, editor.Document);
        Assert.Equal(1, s.Analyses);
        draft.PositionText = "400";
        Assert.False(editor.HasSupportFeedback);
        Assert.True(editor.BeginSupportDrag(id));
        editor.UpdateSupportDrag(Mm(450));
        Assert.True(editor.EndSupportDrag());
        Assert.True(editor.ConfirmSupport());
        Assert.Equal(2, s.Analyses);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CancellationDuringOrAfterRedragDiscardsWholeSession(bool during)
    {
        var s = new Session(300);
        var editor = s.Editor;
        var document = editor.Document;
        var id = document.Supports[0].Id;
        editor.EditSupport(id);
        editor.BeginSupportDrag(id);
        editor.UpdateSupportDrag(Mm(400));
        editor.EndSupportDrag();
        editor.BeginSupportDrag(id);
        editor.UpdateSupportDrag(Mm(500));
        if (!during) editor.EndSupportDrag();
        editor.CancelSupportInteraction(); // Escape/capture-loss during drag, or outside-dismiss after release.
        editor.CancelSupportInteraction();
        Assert.Same(document, editor.Document);
        Assert.Equal(1, s.Analyses);
        Assert.Null(editor.Preview);
        Assert.Null(editor.SupportDraft);
        Assert.False(editor.IsSupportFlyoutVisible);
        Assert.False(editor.EndSupportDrag());
    }

    [Fact]
    public void OtherSupportCannotReplaceActiveSessionOrStartDrag()
    {
        var s = new Session(300, 800);
        s.Editor.EditSupport(s.Editor.Document.Supports[0].Id);
        var draft = s.Editor.SupportDraft;
        Assert.False(s.Editor.EditSupport(s.Editor.Document.Supports[1].Id));
        Assert.False(s.Editor.BeginSupportDrag(s.Editor.Document.Supports[1].Id));
        Assert.Same(draft, s.Editor.SupportDraft);
        Assert.Equal(1, s.Analyses);
    }

    [Fact]
    public void ActiveSymbolHitZoneFollowsDraftCanvasAndTypeRatherThanOriginalOrText()
    {
        var s = new Session(300);
        var editor = s.Editor;
        var id = editor.Document.Supports[0].Id;
        var viewport = new BeamViewport(0, 1000, 200, 160, 1);
        editor.EditSupport(id);
        editor.BeginSupportDrag(id);
        editor.UpdateSupportDrag(Mm(500));
        editor.EndSupportDrag();
        editor.SupportDraft!.PositionText = "700";
        Assert.True(SupportSymbol.Contains(editor.Preview!, viewport, 500, 225));
        Assert.False(SupportSymbol.Contains(editor.Preview!, viewport, 300, 225));
        Assert.False(SupportSymbol.Contains(editor.Preview!, viewport, 700, 225));
        editor.SupportDraft.Type = SupportType.Fixed;
        Assert.True(SupportSymbol.Contains(editor.Preview!, viewport, 495, 180));
        Assert.False(SupportSymbol.Contains(editor.Preview!, viewport, 515, 225));
        var resized = BeamViewport.Fit(1600, 800, 1);
        Assert.True(SupportSymbol.Contains(editor.Preview!, resized, resized.BeamToScreen(0.5), resized.BeamY));
    }

    [Fact]
    public void DragBufferReferencePreservesExactSiPositionOnUnchangedOk()
    {
        var s = new Session(300);
        var editor = s.Editor;
        var id = editor.Document.Supports[0].Id;
        var exact = Length.FromMeters(0.027387593197926163);
        editor.EditSupport(id);
        editor.BeginSupportDrag(id);
        editor.UpdateSupportDrag(exact);
        editor.EndSupportDrag();
        Assert.True(editor.ConfirmSupport());
        Assert.Equal(exact, editor.Document.Supports[0].Position);
        var document = editor.Document;
        editor.EditSupport(id);
        Assert.True(editor.ConfirmSupport());
        Assert.Same(document, editor.Document);
        Assert.Equal(2, s.Analyses);
    }

    [Theory]
    [InlineData("de-DE", "700,5")]
    [InlineData("en-US", "700.5")]
    public void CultureParsingCommitsExactTextWithoutMovingPreview(string culture, string text)
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            var s = new Session(689);
            s.Editor.EditSupport(s.Editor.Document.Supports[0].Id);
            s.Editor.SupportDraft!.PositionText = text;
            Assert.Equal(Mm(689), s.Editor.Preview!.Position);
            Assert.True(s.Editor.ConfirmSupport());
            Assert.Equal(Mm(700.5), s.Editor.Document.Supports[0].Position);
            Assert.Equal(2, s.Analyses);
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }
}
