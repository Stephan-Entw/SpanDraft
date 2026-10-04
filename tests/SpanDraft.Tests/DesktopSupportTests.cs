using System.Globalization;
using SpanDraft.Analysis;
using SpanDraft.Core.Beams;
using SpanDraft.Core.Materials;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Controls;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;
using Xunit;

namespace SpanDraft.Tests;

public sealed class DesktopSupportTests
{
    private static Length Mm(double value) => Length.FromMillimeters(value);
    private sealed class CultureScope(string culture) : IDisposable
    {
        private readonly CultureInfo _old = CultureInfo.CurrentUICulture;
        private readonly CultureInfo _selected = SetCulture(culture);
        private static CultureInfo SetCulture(string name) => CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(name);
        public void Dispose() { _ = _selected; CultureInfo.CurrentUICulture = _old; }
    }
    private sealed class Session
    {
        public int Calls { get; private set; }
        public BeamModel? Beam { get; private set; }
        public MainWindowViewModel Main { get; }
        public EditorViewModel Editor => Main.Editor!;
        public Session()
        {
            Main = new(beam => { Calls++; Beam = beam; return BeamAnalysis.Analyze(beam); });
            Main.Setup.ApplyCommand.Execute(null);
        }
        public EditorSupport Add(double mm, SupportType type = SupportType.Fixed)
        {
            Editor.ToggleSupportTool(type);
            Editor.HoverPlacement(Mm(mm));
            Assert.True(Editor.PlaceSupport());
            Assert.True(Editor.ConfirmSupport());
            return Editor.Document.Supports.Last();
        }
    }

    [Fact]
    public void DocumentStartsEmptyAndProtectsSupportCollection()
    {
        var empty = new EditorDocument(Mm(1000), ProjectTemplates.Material, ProjectTemplates.Section);
        Assert.Empty(empty.Supports);
        var original = new EditorSupport(Guid.NewGuid(), Mm(200), SupportType.Fixed);
        EditorSupport[] source = [original];
        var document = empty.WithSupports(source);
        source[0] = original with { Position = Mm(300) };
        Assert.Equal(original, Assert.Single(document.Supports));
        Assert.Throws<NotSupportedException>(() => ((IList<EditorSupport>)document.Supports).Clear());
        var model = document.ToBeamModel();
        Assert.Equal(original.Position, Assert.Single(model.Supports).Position);
        Assert.Equal(original.Type, model.Supports[0].Type);
        Assert.Empty(model.Loads);
    }

    [Fact]
    public void MappingPreservesDocumentOrderAndExactValues()
    {
        EditorSupport[] supports = [new(Guid.NewGuid(), Mm(1000.5), SupportType.Roller),
            new(Guid.NewGuid(), Mm(0), SupportType.Fixed), new(Guid.NewGuid(), Mm(207.5), SupportType.Pinned)];
        var document = new EditorDocument(Mm(1000.5), ProjectTemplates.Material, ProjectTemplates.Section, supports);
        var beam = document.ToBeamModel();
        Assert.Equal(supports.Select(s => s.Position), beam.Supports.Select(s => s.Position));
        Assert.Equal(supports.Select(s => s.Type), beam.Supports.Select(s => s.Type));
        Assert.Empty(beam.Loads);
    }

    [Theory]
    [InlineData(SupportType.Fixed)]
    [InlineData(SupportType.Pinned)]
    [InlineData(SupportType.Roller)]
    public void PlacementPreviewDraftAndCommitAreOneShot(SupportType type)
    {
        var s = new Session();
        var document = s.Editor.Document;
        s.Editor.ToggleSupportTool(type);
        Assert.Equal(type, s.Editor.PlacementTool);
        s.Editor.HoverPlacement(Mm(207));
        Assert.Equal(Mm(207), s.Editor.Preview!.Position);
        Assert.Same(document, s.Editor.Document);
        Assert.Equal(1, s.Calls);
        Assert.True(s.Editor.PlaceSupport());
        var draft = s.Editor.SupportDraft!;
        Assert.False(draft.IsExisting);
        draft.PositionText = "200";
        Assert.Equal(Mm(200), s.Editor.Preview!.Position);
        Assert.Same(document, s.Editor.Document);
        Assert.Equal(1, s.Calls);
        Assert.True(s.Editor.ConfirmSupport());
        var support = Assert.Single(s.Editor.Document.Supports);
        Assert.NotEqual(Guid.Empty, support.Id);
        Assert.Equal(type, support.Type);
        Assert.Equal(Mm(200), support.Position);
        Assert.Equal(2, s.Calls);
        Assert.Null(s.Editor.Preview);
        Assert.Null(s.Editor.PlacementTool);
        Assert.Equal(SupportInteraction.Neutral, s.Editor.Interaction);
        Assert.False(s.Editor.ConfirmSupport());
        Assert.False(s.Editor.PlaceSupport());
        s.Editor.HoverPlacement(Mm(400));
        Assert.Null(s.Editor.Preview);
        Assert.Equal(2, s.Calls);
        s.Add(400, type);
        Assert.Equal(2, s.Editor.Document.Supports.Count);
        Assert.Equal(3, s.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CancelPlacementOrNewFlyoutPreservesDocument(bool open)
    {
        var s = new Session();
        var document = s.Editor.Document;
        s.Editor.ToggleSupportTool(SupportType.Pinned);
        s.Editor.HoverPlacement(Mm(207));
        if (open) Assert.True(s.Editor.PlaceSupport());
        s.Editor.CancelSupportInteraction();
        s.Editor.CancelSupportInteraction();
        Assert.Same(document, s.Editor.Document);
        Assert.Empty(document.Supports);
        Assert.Equal(1, s.Calls);
        Assert.Equal(SupportInteraction.Neutral, s.Editor.Interaction);
        Assert.Null(s.Editor.Preview);
        Assert.Null(s.Editor.SupportDraft);
    }

    [Fact]
    public void ToggleAndSwitchToolsNeverCommitOrAnalyze()
    {
        var s = new Session();
        s.Editor.ToggleSupportTool(SupportType.Pinned);
        s.Editor.HoverPlacement(Mm(200));
        s.Editor.HoverPlacement(null);
        Assert.Null(s.Editor.Preview);
        Assert.True(s.Editor.IsPinnedTool);
        s.Editor.ToggleSupportTool(SupportType.Roller);
        Assert.False(s.Editor.IsPinnedTool);
        Assert.True(s.Editor.IsRollerTool);
        s.Editor.ToggleSupportTool(SupportType.Roller);
        Assert.Equal(SupportInteraction.Neutral, s.Editor.Interaction);
        Assert.Equal(1, s.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EditChangesBothValuesOnlyOnApplyAndPreservesId(bool apply)
    {
        var s = new Session();
        var support = s.Add(207, SupportType.Pinned);
        var document = s.Editor.Document;
        s.Editor.HoverSupport(support.Id);
        Assert.Equal(support.Id, s.Editor.HoveredSupportId);
        Assert.True(s.Editor.EditSupport(support.Id));
        Assert.Equal(support.Id, s.Editor.HiddenSupportId);
        s.Editor.SupportDraft!.Type = SupportType.Roller;
        s.Editor.SupportDraft.PositionText = "200";
        Assert.Equal(SupportType.Roller, s.Editor.Preview!.Type);
        Assert.Equal(Mm(200), s.Editor.Preview.Position);
        Assert.Same(document, s.Editor.Document);
        Assert.Equal(2, s.Calls);
        if (apply) Assert.True(s.Editor.ConfirmSupport());
        else s.Editor.CancelSupportInteraction();
        var result = Assert.Single(s.Editor.Document.Supports);
        Assert.Equal(support.Id, result.Id);
        Assert.Equal(apply ? Mm(200) : support.Position, result.Position);
        Assert.Equal(apply ? SupportType.Roller : support.Type, result.Type);
        Assert.Equal(apply ? 3 : 2, s.Calls);
        Assert.Null(s.Editor.HiddenSupportId);
        if (!apply) Assert.Same(document, s.Editor.Document);
    }

    [Fact]
    public void UnchangedEditAndRepeatedCompletionDoNotAnalyze()
    {
        var s = new Session();
        var support = s.Add(207.5);
        var document = s.Editor.Document;
        s.Editor.EditSupport(support.Id);
        Assert.True(s.Editor.SupportDraft!.IsValid);
        Assert.True(s.Editor.ConfirmSupport());
        s.Editor.CancelSupportInteraction();
        s.Editor.DeleteSupport();
        Assert.Same(document, s.Editor.Document);
        Assert.Equal(2, s.Calls);
    }

    [Fact]
    public void UnchangedPositionTextPreservesExactSiValueWithoutMillimeterRoundtrip()
    {
        var position = Length.FromMeters(0.027387593197926163);
        Assert.NotEqual(position, Mm(position.Millimeters));
        var support = new EditorSupport(Guid.NewGuid(), position, SupportType.Fixed);
        var document = new EditorDocument(Mm(1000), ProjectTemplates.Material, ProjectTemplates.Section, [support]);
        int calls = 0;
        var editor = new EditorViewModel(document, () => { }, beam => { calls++; return BeamAnalysis.Analyze(beam); });
        Assert.True(editor.EditSupport(support.Id));
        Assert.True(editor.ConfirmSupport());
        Assert.Same(document, editor.Document);
        Assert.Equal(1, calls);
        Assert.True(editor.EditSupport(support.Id));
        editor.SupportDraft!.Type = SupportType.Pinned;
        Assert.True(editor.ConfirmSupport());
        Assert.Equal(position, editor.Document.Supports[0].Position);
        Assert.Equal(support.Id, editor.Document.Supports[0].Id);
        Assert.Equal(2, calls);
    }

    [Fact]
    public void DeleteCommitsOnceAndRestoresMissingSupports()
    {
        var s = new Session();
        var support = s.Add(0);
        Assert.True(s.Editor.Presentation.IsSuccess);
        s.Editor.EditSupport(support.Id);
        s.Editor.DeleteSupport();
        s.Editor.DeleteSupport();
        Assert.Empty(s.Editor.Document.Supports);
        Assert.Equal(3, s.Calls);
        Assert.Equal(AnalysisPresentationKind.MissingSupports, s.Editor.Presentation.Kind);
        Assert.Equal(SupportInteraction.Neutral, s.Editor.Interaction);
    }

    [Theory]
    [InlineData(SupportType.Fixed)]
    [InlineData(SupportType.Pinned)]
    [InlineData(SupportType.Roller)]
    public void DuplicateHoverAndDraftAreRejectedRegardlessOfType(SupportType type)
    {
        var s = new Session();
        s.Add(200);
        var document = s.Editor.Document;
        s.Editor.ToggleSupportTool(type);
        s.Editor.HoverPlacement(Mm(200));
        Assert.True(s.Editor.Preview!.IsInvalid);
        Assert.Equal(Strings.SupportAlreadyExists, s.Editor.SupportFeedback);
        Assert.False(s.Editor.PlaceSupport());
        Assert.Equal(type, s.Editor.PlacementTool);
        s.Editor.HoverPlacement(Mm(300));
        Assert.True(s.Editor.PlaceSupport());
        s.Editor.SupportDraft!.PositionText = "200";
        Assert.False(s.Editor.ConfirmSupport());
        Assert.True(s.Editor.SupportDraft.HasError);
        Assert.Same(document, s.Editor.Document);
        Assert.Equal(2, s.Calls);
        s.Editor.SupportDraft.PositionText = "201";
        Assert.True(s.Editor.ConfirmSupport());
        Assert.Equal(3, s.Calls);
    }

    [Fact]
    public void EditCollisionExcludesSelfAndKeepsLastPreviewForInvalidText()
    {
        using var culture = new CultureScope("en-US");
        var s = new Session();
        var first = s.Add(200);
        s.Add(300, SupportType.Pinned);
        var document = s.Editor.Document;
        s.Editor.EditSupport(first.Id);
        Assert.True(s.Editor.SupportDraft!.IsValid);
        s.Editor.SupportDraft.PositionText = "300";
        Assert.Equal(Strings.SupportAlreadyExists, s.Editor.SupportDraft.ErrorText);
        Assert.False(s.Editor.ConfirmSupport());
        s.Editor.SupportDraft.PositionText = "250.5";
        s.Editor.SupportDraft.PositionText = "invalid";
        Assert.Equal(Mm(250.5), s.Editor.Preview!.Position);
        Assert.True(s.Editor.Preview.IsInvalid);
        Assert.False(s.Editor.ConfirmSupport());
        s.Editor.CancelSupportInteraction();
        Assert.Same(document, s.Editor.Document);
        Assert.Equal(3, s.Calls);
    }

    [Theory]
    [InlineData("de-DE", "207,5", 207.5)]
    [InlineData("en-US", "207.5", 207.5)]
    [InlineData("en-US", "0", 0)]
    [InlineData("en-US", "1000.5", 1000.5)]
    public void PositionParsingSupportsCultureAndInclusiveEndpoints(string culture, string text, double expected)
    {
        using var scope = new CultureScope(culture);
        var old = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            Assert.True(UiNumbers.TryParsePosition(text, Mm(1000.5), out var position));
            Assert.Equal(Mm(expected), position);
            Assert.Equal(text, UiNumbers.Format(position.Millimeters));
        }
        finally { CultureInfo.CurrentCulture = old; }
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("-0.1")]
    [InlineData("1000.1")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("1e309")]
    [InlineData("1,000")]
    public void InvalidPositionCannotCommit(string text)
    {
        using var scope = new CultureScope("en-US");
        var s = new Session();
        s.Editor.ToggleSupportTool(SupportType.Fixed);
        s.Editor.HoverPlacement(Mm(200));
        s.Editor.PlaceSupport();
        s.Editor.SupportDraft!.PositionText = text;
        Assert.False(s.Editor.ConfirmSupport());
        Assert.NotNull(s.Editor.SupportDraft);
        Assert.Empty(s.Editor.Document.Supports);
        Assert.Equal(1, s.Calls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1000)]
    public void EndpointsCanBeCommitted(double mm)
    {
        var s = new Session();
        Assert.Equal(Mm(mm), s.Add(mm).Position);
        Assert.Equal(2, s.Calls);
    }

    [Theory]
    [InlineData(207.4, 207)]
    [InlineData(207.5, 208)]
    [InlineData(207.6, 208)]
    public void MouseSnapRoundsWholeMillimetersAwayFromZero(double x, double expected)
    {
        var v = new BeamViewport(0, 1000, 200, 160, 1);
        Assert.Equal(Mm(expected), SupportSnap.Placement(v, x, 200));
    }

    [Theory]
    [InlineData(-8, 0)]
    [InlineData(8, 0)]
    [InlineData(992, 1000.5)]
    [InlineData(1008, 1000.5)]
    public void EndpointSnapPrecedesRoundingAndPreservesFractionalLength(double x, double expected)
    {
        var v = new BeamViewport(0, 1000, 200, 160, Mm(1000.5).Meters);
        Assert.Equal(Mm(expected), SupportSnap.AtX(v, x));
    }

    [Fact]
    public void NoBeamHitOutsideDipBandOrHorizontalRange()
    {
        var v = new BeamViewport(0, 1000, 200, 160, 1);
        Assert.Null(SupportSnap.Placement(v, 200, 219));
        Assert.Null(SupportSnap.Placement(v, -11, 200));
        Assert.Null(SupportSnap.Placement(v, 1011, 200));
        Assert.Null(SupportSnap.AtX(v, double.NaN));
        Assert.Null(SupportSnap.AtX(v, double.PositiveInfinity));
        Assert.Null(SupportSnap.AtX(v with { LengthMeters = 0 }, 200));
        var fractional = v with { LengthMeters = Mm(1000.6).Meters };
        Assert.Null(SupportSnap.AtX(fractional, fractional.BeamToScreen(Mm(1000.55).Meters), 0));
    }

    [Fact]
    public void ResizePreservesPhysicalPositionsAndHitTesting()
    {
        var s = new Session();
        var support = s.Add(207.5);
        var document = s.Editor.Document;
        foreach (var width in new[] { 1100.0, 1250.0, 1600.0 })
        {
            var viewport = BeamViewport.Fit(width, 650, document.Length.Meters);
            double x = viewport.BeamToScreen(support.Position.Meters);
            NumericAssert.Close(support.Position.Meters, viewport.ScreenToBeam(x));
            Assert.Equal(support.Id, SupportSymbol.HitTest(document.Supports, viewport, x, viewport.BeamY));
        }
        Assert.Same(document, s.Editor.Document);
        Assert.Equal(2, s.Calls);
    }

    [Fact]
    public void HitTestSelectsNearestThenDocumentOrderWithinDipExtents()
    {
        var v = new BeamViewport(0, 1000, 200, 160, 1);
        EditorSupport[] supports = [new(Guid.NewGuid(), Mm(200), SupportType.Pinned), new(Guid.NewGuid(), Mm(220), SupportType.Roller)];
        Assert.Equal(supports[0].Id, SupportSymbol.HitTest(supports, v, 210, 210));
        Assert.Equal(supports[1].Id, SupportSymbol.HitTest(supports, v, 215, 210));
        Assert.Null(SupportSymbol.HitTest(supports, v, 200, 250));
        Assert.Null(SupportSymbol.HitTest(supports, v, 200, 190));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SetupPreservesSupportIdentitiesAndPositions(bool apply)
    {
        var s = new Session();
        s.Add(200);
        s.Add(900, SupportType.Roller);
        var document = s.Editor.Document;
        s.Editor.ToggleSupportTool(SupportType.Pinned);
        s.Editor.ChangeProjectCommand.Execute(null);
        Assert.Equal(SupportInteraction.Neutral, s.Editor.Interaction);
        var material = new Material("Alternative", Pressure.FromPascals(100e9), Pressure.FromMegapascals(100));
        var section = new RectangleSection(Mm(30), Mm(40));
        s.Main.Setup.SelectedMaterial = material;
        s.Main.Setup.SelectedSection = section;
        if (apply) s.Main.Setup.ApplyCommand.Execute(null);
        else s.Main.Setup.CancelCommand.Execute(null);
        Assert.Equal(document.Supports, s.Editor.Document.Supports);
        Assert.Equal(document.Length, s.Editor.Document.Length);
        Assert.Equal(apply ? 4 : 3, s.Calls);
        if (!apply) Assert.Same(document, s.Editor.Document);
        else { Assert.Same(material, s.Editor.Document.Material); Assert.Same(section, s.Editor.Document.Section); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ShorteningCannotExcludeSupportsAndFocusLossRetainsRejection(bool loseFocus)
    {
        var s = new Session();
        s.Add(900);
        var document = s.Editor.Document;
        var input = s.Editor.DimensionLength;
        input.Begin();
        input.Text = "800";
        Assert.False(input.Confirm());
        Assert.True(input.IsEditing);
        Assert.Equal(Strings.LengthExcludesSupports, input.ErrorText);
        if (loseFocus)
        {
            input.LoseFocus();
            Assert.False(input.IsEditing);
            Assert.Equal("1000", input.Text);
        }
        Assert.True(input.HasError);
        Assert.Same(document, s.Editor.Document);
        Assert.Equal(2, s.Calls);
        input.Cancel();
        Assert.False(input.HasError);
    }

    [Theory]
    [InlineData("900")]
    [InlineData("950")]
    public void LengthAtOrAboveOutermostSupportCommitsOnce(string text)
    {
        var s = new Session();
        var support = s.Add(900);
        s.Editor.DimensionLength.Begin();
        s.Editor.DimensionLength.Text = text;
        Assert.True(s.Editor.DimensionLength.Confirm());
        s.Editor.DimensionLength.LoseFocus();
        Assert.Equal(support, Assert.Single(s.Editor.Document.Supports));
        Assert.Equal(3, s.Calls);
    }

    [Fact]
    public void FixedPlacementUsesRealAnalysisAndUnloadedEngineeringResult()
    {
        var s = new Session();
        Assert.Equal(AnalysisPresentationKind.MissingSupports, s.Editor.Presentation.Kind);
        s.Add(0);
        Assert.Equal(AnalysisPresentationKind.Success, s.Editor.Presentation.Kind);
        Assert.Equal("0 mm", s.Editor.Presentation.Displacement);
        Assert.Equal("0 kNm", s.Editor.Presentation.Moment);
        Assert.Equal("0 MPa", s.Editor.Presentation.Stress);
        Assert.Equal("∞", s.Editor.Presentation.SafetyFactor);
        Assert.Empty(s.Beam!.Loads);
    }

    [Fact]
    public void GestureDistinguishesClickFromDragAndPreservesGrabOffset()
    {
        var v = new BeamViewport(0, 1000, 200, 160, 1);
        var gesture = new SupportDragGesture(Guid.NewGuid(), Mm(200), 210);
        Assert.Null(gesture.Update(v, 213, 220, 1000, 500));
        Assert.False(gesture.IsDragging);
        Assert.Equal(Mm(204), gesture.Update(v, 214, 250, 1000, 500));
        Assert.True(gesture.IsDragging);
        Assert.Equal(Mm(240), gesture.Update(v, 250, 400, 1000, 500));
        Assert.Null(gesture.Update(v, 250, 501, 1000, 500));
        Assert.Null(gesture.Update(v, -20, 220, 1000, 500));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DragReleaseOpensTransactionalFlyoutAndKeepsId(bool apply)
    {
        var s = new Session();
        var support = s.Add(200);
        var document = s.Editor.Document;
        Assert.True(s.Editor.BeginSupportDrag(support.Id));
        s.Editor.UpdateSupportDrag(Mm(200));
        Assert.False(s.Editor.Preview!.IsInvalid);
        s.Editor.UpdateSupportDrag(Mm(250));
        Assert.Same(document, s.Editor.Document);
        Assert.Equal(support.Id, s.Editor.HiddenSupportId);
        Assert.True(s.Editor.EndSupportDrag());
        Assert.Equal(SupportInteraction.EditDraft, s.Editor.Interaction);
        Assert.Equal(Mm(250), s.Editor.Preview!.Position);
        Assert.Equal(2, s.Calls);
        if (apply) Assert.True(s.Editor.ConfirmSupport());
        else s.Editor.CancelSupportInteraction();
        Assert.Equal(support.Id, Assert.Single(s.Editor.Document.Supports).Id);
        Assert.Equal(apply ? Mm(250) : Mm(200), s.Editor.Document.Supports[0].Position);
        Assert.Equal(apply ? 3 : 2, s.Calls);
    }

    [Theory]
    [InlineData("collision")]
    [InlineData("outside")]
    [InlineData("cancel")]
    public void InvalidDragReleaseAndCancellationRestoreOriginal(string reason)
    {
        var s = new Session();
        var support = s.Add(200);
        s.Add(300, SupportType.Pinned);
        var document = s.Editor.Document;
        s.Editor.BeginSupportDrag(support.Id);
        s.Editor.UpdateSupportDrag(reason == "collision" ? Mm(300) : null);
        if (reason == "cancel") s.Editor.CancelSupportInteraction();
        else Assert.False(s.Editor.EndSupportDrag());
        Assert.Equal(SupportInteraction.Neutral, s.Editor.Interaction);
        Assert.Same(document, s.Editor.Document);
        Assert.Null(s.Editor.Preview);
        Assert.Null(s.Editor.SupportDraft);
        Assert.Null(s.Editor.HiddenSupportId);
        Assert.Equal(3, s.Calls);
        if (reason != "cancel") Assert.True(s.Editor.HasSupportFeedback);
    }

    [Fact]
    public void SupportResourcesLocalizeAndFallback()
    {
        using (new CultureScope("de-DE"))
        {
            Assert.Equal("Lager", Strings.Support);
            Assert.Equal("Einspannung", SupportDraftViewModel.Types[0].Name);
            Assert.Equal("Festlager", SupportDraftViewModel.Types[1].Name);
            Assert.Equal("Loslager", SupportDraftViewModel.Types[2].Name);
        }
        using (new CultureScope("fr-FR")) Assert.Equal("Support", Strings.Support);
    }

    [Fact]
    public void LengthCommitIgnoresReentrantFocusLossWithoutHidingRejectedInput()
    {
        var length = Mm(1000);
        int calls = 0;
        bool accepted = false;
        LengthInputViewModel? input = null;
        input = new(() => length, candidate =>
        {
            calls++;
            Assert.True(input!.IsEditing);
            input.LoseFocus(); // Simulates a UI focus event during document notifications.
            if (!accepted) return new(false, Strings.LengthExcludesSupports);
            length = candidate;
            return LengthCommitResult.Success;
        });
        input.Begin();
        input.Text = "900";
        Assert.False(input.Confirm());
        Assert.True(input.IsEditing);
        Assert.Equal(1, calls);
        accepted = true;
        input.Text = "1200";
        Assert.True(input.Confirm());
        input.LoseFocus();
        Assert.False(input.IsEditing);
        Assert.Equal(Mm(1200), length);
        Assert.Equal(2, calls);
    }

    [Fact]
    public void InvalidDraftTypeCannotCommitAndDirectSetupNavigationDiscardsDraft()
    {
        var s = new Session();
        var support = s.Add(200);
        var document = s.Editor.Document;
        s.Editor.EditSupport(support.Id);
        s.Editor.SupportDraft!.Type = (SupportType)999;
        Assert.Equal(Strings.InvalidSupportType, s.Editor.SupportDraft.ErrorText);
        Assert.False(s.Editor.ConfirmSupport());
        s.Main.EditProject();
        Assert.Null(s.Editor.SupportDraft);
        Assert.Null(s.Editor.Preview);
        s.Main.Setup.CancelCommand.Execute(null);
        Assert.Same(document, s.Editor.Document);
        Assert.Equal(2, s.Calls);
    }
}
