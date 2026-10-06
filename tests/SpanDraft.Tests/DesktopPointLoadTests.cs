using System.Globalization;
using SpanDraft.Analysis;
using SpanDraft.Core.Beams;
using SpanDraft.Core.Loads;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Controls;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;
using Xunit;

namespace SpanDraft.Tests;

public sealed class DesktopPointLoadTests
{
    private static Length Mm(double value) => Length.FromMillimeters(value);
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
        public EditorPointLoad Add(PointLoadKind kind, double x = 300, double? value = null)
        {
            Editor.ToggleLoadTool(kind);
            Editor.HoverLoadPlacement(Mm(x));
            Assert.True(Editor.PlaceLoad());
            if (value is { } v) Editor.LoadDraft!.ValueText = UiNumbers.Format(v);
            Assert.True(Editor.ConfirmLoad());
            return Editor.Document.Loads.Last();
        }
    }

    [Fact]
    public void DocumentProtectsLoadsAndMapsMixedOrderWithoutDesktopIdentities()
    {
        var empty = new EditorDocument(Mm(1000), ProjectTemplates.Material, ProjectTemplates.Section);
        Assert.Empty(empty.Loads);
        EditorPointLoad[] source = [new EditorPointMoment(Guid.NewGuid(), Mm(0), Moment.FromNewtonMeters(12), "M1"),
            new EditorPointForce(Guid.NewGuid(), Mm(1000), Force.FromNewtons(-20), "F1"),
            new EditorPointMoment(Guid.NewGuid(), Mm(300), Moment.FromNewtonMeters(-30), "M2")];
        var document = empty.WithLoads(source);
        source[0] = source[1];
        Assert.Throws<NotSupportedException>(() => ((IList<EditorPointLoad>)document.Loads).Clear());
        var beam = document.ToBeamModel();
        Assert.Equal(12, Assert.IsType<PointMoment>(beam.Loads[0]).Moment.NewtonMeters);
        Assert.Equal(Mm(0), Assert.IsType<PointMoment>(beam.Loads[0]).Position);
        Assert.Equal(-20, Assert.IsType<PointForce>(beam.Loads[1]).Force.Newtons);
        Assert.Equal(Mm(1000), Assert.IsType<PointForce>(beam.Loads[1]).Position);
        Assert.Equal(-30, Assert.IsType<PointMoment>(beam.Loads[2]).Moment.NewtonMeters);
        var support = new EditorSupport(Guid.NewGuid(), Mm(0), SupportType.Fixed, "A");
        var withSupport = document.WithSupports([support]);
        Assert.Equal(document.Loads, withSupport.Loads);
        Assert.Equal(withSupport.Supports, withSupport.WithLoads([]).Supports);
    }

    [Theory]
    [InlineData(PointLoadKind.Force, -1000)]
    [InlineData(PointLoadKind.Moment, 100)]
    public void PlacementPreviewAndDraftAreTransientThenCommitOnceAndReturnNeutral(PointLoadKind kind, double expected)
    {
        var s = new Session();
        var editor = s.Editor;
        var document = editor.Document;
        editor.ToggleLoadTool(kind);
        Assert.Equal(LoadInteraction.Placement, editor.LoadState);
        editor.HoverLoadPlacement(Mm(1000));
        Assert.Equal(expected, editor.LoadPreview!.Value);
        Assert.True(editor.HasCoordinate);
        Assert.True(editor.PlaceLoad());
        Assert.Equal(LoadInteraction.NewDraft, editor.LoadState);
        Assert.Null(editor.LoadDraft!.OriginalId);
        Assert.False(editor.LoadDraft.IsExisting);
        Assert.Same(document, editor.Document);
        Assert.Equal(1, s.Calls);
        Assert.Empty(s.Beam!.Loads);
        Assert.True(editor.ConfirmLoad());
        var load = Assert.Single(editor.Document.Loads);
        Assert.NotEqual(Guid.Empty, load.Id);
        Assert.Equal(expected, load.Value);
        Assert.Equal(Mm(1000), load.Position);
        Assert.Equal(kind, load.Kind);
        Assert.Single(s.Beam.Loads);
        Assert.Equal(2, s.Calls);
        Assert.Equal(LoadInteraction.Neutral, editor.LoadState);
        Assert.Null(editor.LoadTool);
        Assert.Null(editor.LoadDraft);
        Assert.Null(editor.LoadPreview);
        Assert.False(editor.PlaceLoad());
        Assert.False(editor.ConfirmLoad());
        editor.DeleteLoad();
        Assert.Equal(2, s.Calls);
    }

    [Theory]
    [InlineData(PointLoadKind.Force)]
    [InlineData(PointLoadKind.Moment)]
    public void ToolsReplaceSupportAndLoadDraftsAndToggleWithoutAnalysis(PointLoadKind kind)
    {
        var s = new Session();
        s.Editor.ToggleSupportTool(SupportType.Fixed);
        s.Editor.HoverPlacement(Mm(0));
        s.Editor.PlaceSupport();
        s.Editor.ToggleLoadTool(kind);
        Assert.Null(s.Editor.SupportDraft);
        Assert.Null(s.Editor.Preview);
        Assert.Null(s.Editor.PlacementTool);
        Assert.False(s.Editor.EditSupport(Guid.NewGuid()));
        s.Editor.HoverLoadPlacement(Mm(300));
        s.Editor.PlaceLoad();
        var draft = s.Editor.LoadDraft;
        Assert.False(s.Editor.EditSupport(Guid.NewGuid()));
        s.Editor.ToggleSupportTool(SupportType.Roller);
        Assert.Null(s.Editor.LoadDraft);
        Assert.Null(s.Editor.LoadPreview);
        Assert.Null(s.Editor.LoadTool);
        Assert.Equal(SupportType.Roller, s.Editor.PlacementTool);
        s.Editor.ToggleLoadTool(kind);
        s.Editor.ToggleLoadTool(kind);
        Assert.Equal(LoadInteraction.Neutral, s.Editor.LoadState);
        Assert.Equal(SupportInteraction.Neutral, s.Editor.Interaction);
        Assert.Equal(1, s.Calls);
        Assert.Empty(s.Editor.Document.Loads);
        Assert.Empty(s.Editor.Document.Supports);
        Assert.NotNull(draft);
    }

    [Theory]
    [InlineData(PointLoadKind.Force, true)]
    [InlineData(PointLoadKind.Force, false)]
    [InlineData(PointLoadKind.Moment, true)]
    [InlineData(PointLoadKind.Moment, false)]
    public void PositionAndValueCommitTogetherOrCancelWithStableCanvasAnchor(PointLoadKind kind, bool apply)
    {
        var s = new Session();
        var load = s.Add(kind);
        var document = s.Editor.Document;
        s.Editor.EditLoad(load.Id);
        var draft = s.Editor.LoadDraft!;
        var viewport = DesktopLayoutFixture.Fit(1250, 600, 1);
        var before = PointLoadSymbol.Layout(document.Loads, viewport, s.Editor.LoadPreview, load.Id).Single();
        draft.PositionText = "700";
        draft.ValueText = UiNumbers.Format(-123.5);
        var after = PointLoadSymbol.Layout(document.Loads, viewport, s.Editor.LoadPreview, load.Id).Single();
        Assert.Equal(before.X, after.X);
        Assert.Equal(before.Y, after.Y);
        Assert.Equal(Mm(300), s.Editor.LoadPreview!.Position);
        Assert.Equal(-123.5, s.Editor.LoadPreview.Value);
        Assert.Same(document, s.Editor.Document);
        Assert.Equal(2, s.Calls);
        Assert.True(s.Editor.EditLoad(load.Id)); // Pressing active object preserves the buffers.
        Assert.Same(draft, s.Editor.LoadDraft);
        Assert.Equal("700", draft.PositionText);
        if (apply)
        {
            Assert.True(s.Editor.ConfirmLoad());
            var updated = Assert.Single(s.Editor.Document.Loads);
            Assert.Equal(load.Id, updated.Id);
            Assert.Equal(Mm(700), updated.Position);
            Assert.Equal(-123.5, updated.Value);
            Assert.Equal(3, s.Calls);
        }
        else
        {
            s.Editor.CancelEditorInteraction();
            Assert.Same(document, s.Editor.Document);
            Assert.Equal(2, s.Calls);
        }
    }

    [Theory]
    [InlineData(PointLoadKind.Force)]
    [InlineData(PointLoadKind.Moment)]
    public void UnchangedOkPreservesExactSiAndDoesNotAnalyze(PointLoadKind kind)
    {
        var exactPosition = Length.FromMeters(0.027387593197926163);
        var exactValue = 123.45678901234567;
        var load = EditorPointLoad.Create(Guid.NewGuid(), exactPosition, kind, exactValue, "Load");
        var document = new EditorDocument(Mm(1000), ProjectTemplates.Material, ProjectTemplates.Section, loads: [load]);
        int calls = 0;
        var editor = new EditorViewModel(document, () => { }, b => { calls++; return BeamAnalysis.Analyze(b); });
        editor.EditLoad(load.Id);
        Assert.True(editor.ConfirmLoad());
        Assert.Same(document, editor.Document);
        Assert.Equal(exactPosition, editor.Document.Loads[0].Position);
        Assert.Equal(exactValue, editor.Document.Loads[0].Value);
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(PointLoadKind.Force, "de-DE", "700,5", "-12,5")]
    [InlineData(PointLoadKind.Force, "en-US", "700.5", "-12.5")]
    [InlineData(PointLoadKind.Moment, "de-DE", "700,5", "12,5")]
    [InlineData(PointLoadKind.Moment, "en-US", "700.5", "12.5")]
    public void ParsingAndLabelsUseUiCultureEvenWhenNumericCultureDiffers(PointLoadKind kind, string culture, string position, string value)
    {
        var oldUi = CultureInfo.CurrentUICulture;
        var old = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture == "de-DE" ? "en-US" : "de-DE");
            var s = new Session();
            s.Editor.ToggleLoadTool(kind);
            s.Editor.HoverLoadPlacement(Mm(300));
            s.Editor.PlaceLoad();
            var draft = s.Editor.LoadDraft!;
            draft.PositionText = position;
            draft.ValueText = value;
            Assert.Equal(draft.NameText + " = " + value + " " + draft.Unit, PointLoadSymbol.Label(draft.Preview, draft.NameText));
            Assert.True(s.Editor.ConfirmLoad());
            Assert.Equal(Mm(700.5), s.Editor.Document.Loads[0].Position);
            Assert.Equal(kind == PointLoadKind.Force ? -12.5 : 12.5, s.Editor.Document.Loads[0].Value);
            Assert.Equal(2, s.Calls);
        }
        finally { CultureInfo.CurrentUICulture = oldUi; CultureInfo.CurrentCulture = old; }
    }

    [Theory]
    [InlineData(PointLoadKind.Force, "NaN")]
    [InlineData(PointLoadKind.Moment, "NaN")]
    [InlineData(PointLoadKind.Force, "Infinity")]
    [InlineData(PointLoadKind.Moment, "-Infinity")]
    [InlineData(PointLoadKind.Force, "1e999")]
    [InlineData(PointLoadKind.Moment, "-")]
    [InlineData(PointLoadKind.Force, "")]
    [InlineData(PointLoadKind.Moment, "1,234.5")]
    public void InvalidValueKeepsLastValidPreviewAndFlyoutOpen(PointLoadKind kind, string text)
    {
        var old = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            var s = new Session();
            var load = s.Add(kind);
            var document = s.Editor.Document;
            s.Editor.EditLoad(load.Id);
            s.Editor.LoadDraft!.ValueText = "123";
            s.Editor.LoadDraft.ValueText = text;
            Assert.True(s.Editor.LoadDraft.HasValueError);
            Assert.Equal(123, s.Editor.LoadPreview!.Value);
            Assert.True(s.Editor.LoadPreview.IsInvalid);
            Assert.False(s.Editor.ConfirmLoad());
            Assert.True(s.Editor.IsLoadFlyoutVisible);
            Assert.Same(document, s.Editor.Document);
            Assert.Equal(2, s.Calls);
            s.Editor.LoadDraft.ValueText = "0";
            Assert.True(s.Editor.ConfirmLoad());
            Assert.Equal(0, s.Editor.Document.Loads[0].Value);
            Assert.Equal(3, s.Calls);
        }
        finally { CultureInfo.CurrentUICulture = old; }
    }

    [Theory]
    [InlineData(PointLoadKind.Force, "-1")]
    [InlineData(PointLoadKind.Moment, "-1")]
    [InlineData(PointLoadKind.Force, "1001")]
    [InlineData(PointLoadKind.Moment, "1001")]
    [InlineData(PointLoadKind.Force, "NaN")]
    [InlineData(PointLoadKind.Moment, "1e999")]
    [InlineData(PointLoadKind.Force, "invalid")]
    [InlineData(PointLoadKind.Moment, "")]
    public void ManualPositionIsRejectedRatherThanClamped(PointLoadKind kind, string text)
    {
        var s = new Session();
        var load = s.Add(kind);
        var document = s.Editor.Document;
        s.Editor.EditLoad(load.Id);
        s.Editor.LoadDraft!.PositionText = text;
        Assert.True(s.Editor.LoadDraft.HasPositionError);
        Assert.False(s.Editor.ConfirmLoad());
        Assert.Equal(Mm(300), s.Editor.LoadPreview!.Position);
        Assert.True(s.Editor.IsLoadFlyoutVisible);
        Assert.Same(document, s.Editor.Document);
        Assert.Equal(2, s.Calls);
    }

    [Theory]
    [InlineData(PointLoadKind.Force, 0, 0)]
    [InlineData(PointLoadKind.Force, 1000, 100)]
    [InlineData(PointLoadKind.Force, 500, -100)]
    [InlineData(PointLoadKind.Moment, 0, 0)]
    [InlineData(PointLoadKind.Moment, 1000, 100)]
    [InlineData(PointLoadKind.Moment, 500, -100)]
    public void EndpointsSignedAndZeroValuesAreAllowed(PointLoadKind kind, double x, double value)
    {
        var s = new Session();
        var load = s.Add(kind, x, value);
        Assert.Equal(Mm(x), load.Position);
        Assert.Equal(value, load.Value);
        Assert.True(s.Editor.CanLoadPosition(x / 1000));
        Assert.False(s.Editor.CanLoadPosition(-0.001));
        Assert.False(s.Editor.CanLoadPosition(1.001));
        Assert.False(s.Editor.CanLoadPosition(double.NaN));
        Assert.False(s.Editor.CanLoadPosition(double.PositiveInfinity));
    }

    [Theory]
    [InlineData(PointLoadKind.Force)]
    [InlineData(PointLoadKind.Moment)]
    public void DeleteCommitsExactlyOnceAndIsDisabledWhileDragging(PointLoadKind kind)
    {
        var s = new Session();
        var load = s.Add(kind);
        s.Editor.EditLoad(load.Id);
        s.Editor.BeginLoadDrag(load.Id);
        s.Editor.DeleteLoad();
        Assert.False(s.Editor.ConfirmLoad());
        Assert.Single(s.Editor.Document.Loads);
        Assert.Equal(2, s.Calls);
        s.Editor.UpdateLoadDrag(Mm(500));
        s.Editor.EndLoadDrag();
        s.Editor.DeleteLoad();
        Assert.Empty(s.Editor.Document.Loads);
        Assert.Empty(s.Beam!.Loads);
        Assert.Equal(3, s.Calls);
        s.Editor.DeleteLoad();
        Assert.Equal(3, s.Calls);
        Assert.Equal(LoadInteraction.Neutral, s.Editor.LoadState);
    }

    [Fact]
    public void SamePositionAllowsMultipleForcesMomentsAndSupportAndPreservesOrder()
    {
        var s = new Session();
        s.Editor.ToggleSupportTool(SupportType.Fixed);
        s.Editor.HoverPlacement(Mm(300));
        s.Editor.PlaceSupport();
        s.Editor.ConfirmSupport();
        var first = s.Add(PointLoadKind.Force);
        var second = s.Add(PointLoadKind.Moment);
        var third = s.Add(PointLoadKind.Force);
        var fourth = s.Add(PointLoadKind.Moment);
        Assert.Equal(4, s.Editor.Document.Loads.Count);
        Assert.Equal(4, s.Beam!.Loads.Count);
        Assert.Equal(new[] { first.Id, second.Id, third.Id, fourth.Id }, s.Editor.Document.Loads.Select(l => l.Id));
        Assert.Single(s.Beam.Supports);
        Assert.Equal(6, s.Calls);
        s.Editor.EditLoad(third.Id);
        s.Editor.LoadDraft!.ValueText = "200";
        s.Editor.ConfirmLoad();
        Assert.Equal(third.Id, s.Editor.Document.Loads[2].Id);
        Assert.Equal(200, s.Editor.Document.Loads[2].Value);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SetupNavigationDiscardsDraftAndPreservesCommittedEntities(bool apply)
    {
        var s = new Session();
        var first = s.Add(PointLoadKind.Force);
        var second = s.Add(PointLoadKind.Moment);
        s.Editor.ToggleSupportTool(SupportType.Fixed);
        s.Editor.HoverPlacement(Mm(0));
        s.Editor.PlaceSupport();
        s.Editor.ConfirmSupport();
        var document = s.Editor.Document;
        s.Editor.EditLoad(first.Id);
        s.Editor.LoadDraft!.ValueText = "500";
        s.Main.EditProject();
        Assert.Null(s.Editor.LoadDraft);
        Assert.Null(s.Editor.LoadPreview);
        if (apply)
        {
            // The Apply branch exercises a real mechanical setup change; an unchanged Apply is a no-op.
            s.Main.Setup.SelectedMaterial = new("Alternative", Pressure.FromPascals(200e9), Pressure.FromMegapascals(355));
            s.Main.Setup.ApplyCommand.Execute(null);
        }
        else s.Main.Setup.CancelCommand.Execute(null);
        Assert.Equal(new[] { first, second }, s.Editor.Document.Loads);
        Assert.Equal(document.Supports, s.Editor.Document.Supports);
        Assert.Equal(apply ? 5 : 4, s.Calls);
        Assert.Equal(2, s.Beam!.Loads.Count);
    }

    [Theory]
    [InlineData(PointLoadKind.Force)]
    [InlineData(PointLoadKind.Moment)]
    public void RealAnalysisUsesCommittedSupportAndLoad(PointLoadKind kind)
    {
        var s = new Session();
        s.Editor.ToggleSupportTool(SupportType.Fixed);
        s.Editor.HoverPlacement(Mm(0));
        s.Editor.PlaceSupport();
        s.Editor.ConfirmSupport();
        Assert.True(s.Editor.Presentation.IsSuccess);
        s.Add(kind, 1000);
        Assert.True(s.Editor.Presentation.IsSuccess);
        Assert.NotEqual("0 mm", s.Editor.Presentation.Displacement);
        Assert.Single(s.Beam!.Supports);
        Assert.Single(s.Beam.Loads);
    }

    [Fact]
    public void NewDraftCancelAndHoverLeaveNeverAnalyze()
    {
        var s = new Session();
        var document = s.Editor.Document;
        s.Editor.ToggleLoadTool(PointLoadKind.Force);
        s.Editor.HoverLoadPlacement(Mm(400));
        s.Editor.HoverLoadPlacement(null);
        Assert.Null(s.Editor.LoadPreview);
        Assert.Equal(LoadInteraction.Placement, s.Editor.LoadState);
        s.Editor.HoverLoadPlacement(Mm(400));
        s.Editor.PlaceLoad();
        s.Editor.LoadDraft!.PositionText = "500";
        s.Editor.LoadDraft.ValueText = "200";
        s.Editor.CancelLoadInteraction();
        Assert.Same(document, s.Editor.Document);
        Assert.Null(s.Editor.LoadPreview);
        Assert.Equal(1, s.Calls);
    }

    [Fact]
    public void LoadResourcesLocalizeAndFallback()
    {
        var old = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");
            Assert.Equal("Kraft F", Strings.ForceValueLabel);
            Assert.Contains("Punktlasten", Strings.LengthExcludesEntities);
            Assert.NotEmpty(Strings.InvalidLoadValue);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");
            Assert.Equal("Force F", Strings.ForceValueLabel);
            Assert.Equal("Moment M", Strings.MomentValueLabel);
        }
        finally { CultureInfo.CurrentUICulture = old; }
    }
}
