using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using SpanDraft.Analysis;
using SpanDraft.Core.Supports;
using SpanDraft.Desktop;
using SpanDraft.Desktop.Controls;
using SpanDraft.Desktop.Layout;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.State;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.ViewModels;
using SpanDraft.Desktop.Views;
using Xunit;

namespace SpanDraft.Tests;

[Collection("Schematic text")]
public sealed class DesktopNavigationTests
{
    [Theory]
    [InlineData("de-DE", 1100, 650)]
    [InlineData("en-US", 1250, 800)]
    [InlineData("de-DE", 1600, 900)]
    public void OverviewNameButtonsCenterShortCaptionsInAllThreeTables(string culture, double width, double height)
    {
        using var environment = new DesktopControlEnvironment();
        using var scope = new UiCultureScope(culture);
        var model = new MainWindowViewModel();
        var window = new MainWindow { DataContext = model };
        model.Setup.ApplyCommand.Execute(null);
        var state = ProjectTestSupport.State();
        Assert.True(model.Editor!.Session.Commit(new(state.Document with { Length = Length.FromMeters(1) }, state.Presentation)));
        Layout(window, width, height);
        var overview = Assert.Single(window.GetVisualDescendants().OfType<ProjectOverviewView>());
        AssertTables(overview);
    }

    [Theory]
    [InlineData("de-DE", false)]
    [InlineData("en-US", false)]
    [InlineData("de-DE", true)]
    [InlineData("en-US", true)]
    public void OverviewNamesOpenExistingEditorsByIdWithoutCommittingAndPreserveRepeatedClickBuffers(string culture, bool keyboard)
    {
        using var environment = new DesktopControlEnvironment();
        using var scope = new UiCultureScope(culture);
        int analyses = 0;
        var model = new MainWindowViewModel(beam => { analyses++; return BeamAnalysis.Analyze(beam); });
        var window = new MainWindow { DataContext = model };
        model.Setup.ApplyCommand.Execute(null);
        var state = ProjectTestSupport.State();
        Assert.True(model.Editor!.Session.Commit(new(state.Document with { Length = Length.FromMeters(1) }, state.Presentation)));
        var editor = model.Editor;
        editor.Session.MarkSaved(editor.Session.CurrentRevision, "/private/tmp/overview-test.spandraft");
        Layout(window);
        var overview = Assert.Single(window.GetVisualDescendants().OfType<ProjectOverviewView>());
        var surface = Assert.Single(window.GetVisualDescendants().OfType<BeamEditorSurface>());
        var revision = editor.Session.CurrentRevision;
        var projection = editor.Overview;
        int historyCount = editor.Session.UndoHistory.Count;
        int initialAnalyses = analyses;
        foreach (string tableName in new[] { "SupportTable", "LoadTable", "ReactionTable" })
        {
            var buttons = overview.FindControl<ItemsControl>(tableName)!.GetVisualDescendants().OfType<Button>().ToArray();
            Assert.NotEmpty(buttons);
            foreach (var button in buttons)
            {
                Guid id = button.DataContext is ProjectOverviewItem item ? item.Id : ((ProjectOverviewReaction)button.DataContext!).Id;
                ActivateName(button, window, keyboard);
                object draft;
                if (editor.Document.Supports.Any(s => s.Id == id))
                {
                    draft = editor.SupportDraft!;
                    Assert.Equal(id, editor.SupportDraft!.OriginalId);
                    Assert.True(surface.FindControl<Popup>("SupportPopup")!.IsOpen);
                    editor.SupportDraft.PositionText = "-";
                    Assert.False(editor.ConfirmSupport());
                }
                else if (editor.Document.Loads.Any(l => l.Id == id))
                {
                    draft = editor.LoadDraft!;
                    Assert.Equal(id, editor.LoadDraft!.OriginalId);
                    Assert.True(surface.FindControl<Popup>("LoadPopup")!.IsOpen);
                    editor.LoadDraft.ValueText = "-";
                    Assert.False(editor.ConfirmLoad());
                }
                else
                {
                    draft = editor.DistributedLoadDraft!;
                    Assert.Equal(id, editor.DistributedLoadDraft!.OriginalId);
                    Assert.True(surface.FindControl<Popup>("DistributedLoadPopup")!.IsOpen);
                    editor.DistributedLoadDraft.IntensityText = "-";
                    Assert.False(editor.ConfirmDistributedLoad());
                }
                ActivateName(button, window, keyboard);
                Assert.Same(draft, (object?)editor.SupportDraft ?? (object?)editor.LoadDraft ?? editor.DistributedLoadDraft);
                Assert.Equal("-", editor.SupportDraft?.PositionText ?? editor.LoadDraft?.ValueText ?? editor.DistributedLoadDraft?.IntensityText);
                Assert.Same(revision, editor.Session.CurrentRevision);
                Assert.Same(projection, editor.Overview);
                Assert.Equal(historyCount, editor.Session.UndoHistory.Count);
                Assert.Equal(initialAnalyses, analyses);
                Assert.False(editor.Session.IsDirty);
            }
        }
        editor.CancelEditorInteraction();
    }

    [Fact]
    public void OverviewNameActivationCancelsPlacementButRespectsBusyAndPreservedDrafts()
    {
        using var environment = new DesktopControlEnvironment();
        var model = new MainWindowViewModel();
        var window = new MainWindow { DataContext = model };
        model.Setup.ApplyCommand.Execute(null);
        var state = ProjectTestSupport.State();
        Assert.True(model.Editor!.Session.Commit(new(state.Document with { Length = Length.FromMeters(1) }, state.Presentation)));
        Layout(window);
        var editor = model.Editor;
        var overview = Assert.Single(window.GetVisualDescendants().OfType<ProjectOverviewView>());
        var button = overview.FindControl<ItemsControl>("SupportTable")!.GetVisualDescendants().OfType<Button>().First();
        var revision = editor.Session.CurrentRevision;
        editor.ToggleDistributedLoadTool();
        editor.HoverDistributedLoadPlacement(Length.FromMillimeters(250));
        Assert.True(editor.PlaceDistributedLoadEndpoint());
        ActivateName(button, window, false);
        Assert.Equal(ProjectTestSupport.SupportId, editor.SupportDraft!.OriginalId);
        Assert.Equal(DistributedLoadInteraction.Neutral, editor.DistributedLoadState);
        var draft = editor.SupportDraft;
        draft.PositionText = "-";
        var other = overview.FindControl<ItemsControl>("LoadTable")!.GetVisualDescendants().OfType<Button>().First();
        editor.IsFileMenuOpen = true;
        other.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Same(draft, editor.SupportDraft);
        editor.IsFileMenuOpen = false;
        editor.Session.SetBusy(true);
        Assert.False(other.IsEnabled);
        other.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Same(draft, editor.SupportDraft);
        editor.Session.SetBusy(false);
        Assert.True(other.IsEnabled);
        ActivateName(other, window, true);
        Assert.Null(editor.SupportDraft);
        Assert.NotNull(editor.LoadDraft);
        Assert.Same(revision, editor.Session.CurrentRevision);
        editor.CancelEditorInteraction();
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("en-US")]
    public void OverviewEntryUsesExistingRenameCommitAndHistoryAnalysisSemantics(string culture)
    {
        using var environment = new DesktopControlEnvironment();
        using var scope = new UiCultureScope(culture);
        int analyses = 0;
        var model = new MainWindowViewModel(beam => { analyses++; return BeamAnalysis.Analyze(beam); });
        var window = new MainWindow { DataContext = model };
        model.Setup.ApplyCommand.Execute(null);
        var state = ProjectTestSupport.State();
        Assert.True(model.Editor!.Session.Commit(new(state.Document with { Length = Length.FromMeters(1) }, state.Presentation)));
        Layout(window);
        var editor = model.Editor;
        var overview = Assert.Single(window.GetVisualDescendants().OfType<ProjectOverviewView>());
        var support = overview.FindControl<ItemsControl>("SupportTable")!.GetVisualDescendants().OfType<Button>().First();
        int initialAnalyses = analyses;
        ActivateName(support, window, true);
        editor.SupportDraft!.NameText = "Renamed support";
        Assert.True(editor.ConfirmSupport());
        Assert.Equal(initialAnalyses, analyses);
        Assert.Equal("Renamed support", editor.Overview.Reactions.Single(r => r.Id == ProjectTestSupport.SupportId).Name);
        Assert.True(editor.Undo());
        Assert.Equal("A", editor.Overview.Supports.Single(s => s.Id == ProjectTestSupport.SupportId).Name);
        Assert.True(editor.Redo());
        Assert.Equal(initialAnalyses, analyses);
        Layout(window);
        var reaction = overview.FindControl<ItemsControl>("ReactionTable")!.GetVisualDescendants().OfType<Button>()
            .Single(b => ((ProjectOverviewReaction)b.DataContext!).Id == ProjectTestSupport.SupportId);
        ActivateName(reaction, window, true);
        Assert.Equal(ProjectTestSupport.SupportId, editor.SupportDraft!.OriginalId);
        Assert.Equal("Renamed support", editor.SupportDraft.NameText);
        var force = overview.FindControl<ItemsControl>("LoadTable")!.GetVisualDescendants().OfType<Button>()
            .Single(b => editor.Document.Loads.Any(l => l.Id == ((ProjectOverviewItem)b.DataContext!).Id && l.Kind == PointLoadKind.Force));
        ActivateName(force, window, true);
        Guid forceId = editor.LoadDraft!.OriginalId!.Value;
        double originalValue = editor.Document.Loads.Single(l => l.Id == forceId).Value;
        editor.LoadDraft.ValueText = "-2000";
        Assert.True(editor.ConfirmLoad());
        Assert.Equal(initialAnalyses + 1, analyses);
        Assert.Equal(-2000, editor.Document.Loads.Single(l => l.Id == forceId).Value);
        Assert.True(editor.Undo());
        Assert.Equal(initialAnalyses + 2, analyses);
        Assert.Equal(originalValue, editor.Document.Loads.Single(l => l.Id == forceId).Value);
        Assert.True(editor.Redo());
        Assert.Equal(initialAnalyses + 3, analyses);
        Assert.Equal(-2000, editor.Document.Loads.Single(l => l.Id == forceId).Value);
        editor.CancelEditorInteraction();
    }

    private static void ActivateName(Button button, Window window, bool keyboard)
    {
        if (keyboard)
        {
            button.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            return;
        }
        var source = Assert.IsType<TextBlock>(button.Content);
        var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
        var pointer = new Pointer(1, PointerType.Mouse, true);
        var editor = (EditorViewModel)button.GetVisualAncestors().OfType<ProjectOverviewView>().Single().DataContext!;
        var previousDraft = (object?)editor.SupportDraft ?? (object?)editor.LoadDraft ?? editor.DistributedLoadDraft;
        source.RaiseEvent(new PointerPressedEventArgs(source, pointer, window, point, 0,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None, 1));
        // The window's outside-press handler must let the name action retain its buffers.
        Assert.Same(previousDraft, (object?)editor.SupportDraft ?? (object?)editor.LoadDraft ?? editor.DistributedLoadDraft);
        source.RaiseEvent(new PointerReleasedEventArgs(source, pointer, window, point, 1,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased), KeyModifiers.None, MouseButton.Left));
        pointer.Capture(null);
        // The hidden native root has no mouse hit testing, so dispatch the resulting
        // Click explicitly after testing the real routed pointer events.
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    [Fact]
    public void SectionTemplateAllowsClearedSelectionDuringSetupDetachment()
    {
        using var environment = new DesktopControlEnvironment();
        var view = new ProjectSetupView();
        var picker = view.FindControl<ComboBox>("SectionPicker")!;
        var placeholder = Assert.IsType<TextBlock>(picker.ItemTemplate!.Build(null));
        Assert.True(string.IsNullOrEmpty(placeholder.Text));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("en-US")]
    public void CreateReplacesSetupWithCompleteEditorThroughWindowContentBinding(string culture)
    {
        using var environment = new DesktopControlEnvironment();
        var previousCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            int analyses = 0;
            var model = new MainWindowViewModel(beam => { analyses++; return BeamAnalysis.Analyze(beam); });
            // Build the real window/XAML, but never show it or run startup recovery.
            var window = new MainWindow { DataContext = model };
            Layout(window);
            Assert.Single(window.GetVisualDescendants().OfType<ProjectSetupView>());
            var host = Assert.Single(((Grid)window.Content!).Children.OfType<ContentControl>());
            Assert.Same(model.Setup, host.Content);

            model.Setup.ApplyCommand.Execute(null);
            Layout(window);

            var editor = Assert.IsType<EditorViewModel>(model.CurrentViewModel);
            Assert.Same(editor, model.Editor);
            Assert.Same(editor.Session, model.Session);
            Assert.Same(editor, host.Content);
            Assert.Empty(window.GetVisualDescendants().OfType<ProjectSetupView>());
            var view = Assert.Single(window.GetVisualDescendants().OfType<EditorView>());
            Assert.Same(editor, view.DataContext);
            Assert.True(view.Bounds.Width > 0 && view.Bounds.Height > 0);
            Assert.Single(view.GetVisualDescendants().OfType<BeamEditorSurface>());
            var overview = Assert.Single(view.GetVisualDescendants().OfType<ProjectOverviewView>());
            Assert.Same(editor, overview.DataContext);
            var canvas = Assert.Single(view.GetVisualDescendants().OfType<BeamCanvas>());
            Assert.NotNull(canvas.Scene);
            Assert.True(canvas.Bounds.Width > 0 && canvas.Bounds.Height > 0);
            Assert.Equal(6, view.GetVisualDescendants().OfType<ToggleButton>().Count(b => b.Command is not null));
            Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == editor.Presentation.StatusText);
        Assert.Equal(editor.ChangeProjectCommand, Assert.Single(overview.GetVisualDescendants().OfType<Button>()).Command);
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<Button>(), b => !b.IsEnabled);
            Assert.True(editor.Session.IsDirty);
            Assert.Empty(editor.Session.UndoHistory);
            Assert.Equal(Strings.Untitled + "* — " + Strings.ApplicationTitle, window.Title);
            Assert.Equal(1, analyses);

            // Editing the project uses the same setup; cancelling must restore the editor too.
            model.EditProject();
            Layout(window);
            Assert.Single(window.GetVisualDescendants().OfType<ProjectSetupView>());
            Assert.Contains(window.GetVisualDescendants().OfType<Button>(),
                b => Equals(b.Content, culture == "de-DE" ? "Übernehmen" : "Apply"));
            Assert.Equal(culture == "de-DE" ? "Rückgängig" : "Undo", window.FindControl<MenuItem>("UndoMenuItem")!.Header);
            model.Setup.CancelCommand.Execute(null);
            Layout(window);
            Assert.Same(editor, host.Content);
            Assert.Single(window.GetVisualDescendants().OfType<BeamCanvas>());
            Assert.Equal(1, analyses);
        }
        finally { CultureInfo.CurrentUICulture = previousCulture; }
    }

    [Theory]
    [InlineData("de-DE", 1100, 650)]
    [InlineData("en-US", 1250, 800)]
    [InlineData("de-DE", 1600, 900)]
    public void WorkspaceKeepsToolbarIndependentAndOverviewScrollableAtSupportedSizes(string culture, double width, double height)
    {
        using var environment = new DesktopControlEnvironment();
        using var scope = new UiCultureScope(culture);
        var model = new MainWindowViewModel();
        var window = new MainWindow { DataContext = model };
        model.Setup.ApplyCommand.Execute(null);
        var state = ProjectTestSupport.State();
        var loads = Enumerable.Range(1, 35).Select(i => (EditorPointLoad)new EditorPointForce(Guid.NewGuid(),
            Length.FromMillimeters(500), Force.FromNewtons(-i), "Long committed load name " + i));
        var document = state.Document.WithLoads(loads) with { Length = Length.FromMeters(1) };
        Assert.True(model.Editor!.Session.Commit(new(document, state.Presentation)));
        Assert.True(model.Editor.Presentation.IsSuccess, model.Editor.Presentation.StatusText);
        Layout(window, width, height);
        var view = Assert.Single(window.GetVisualDescendants().OfType<EditorView>());
        var overview = Assert.Single(view.GetVisualDescendants().OfType<ProjectOverviewView>());
        var toolbar = view.FindControl<Border>("EditorToolbar")!;
        var workspace = view.FindControl<Grid>("EditorWorkspace")!;
        var editorScroll = view.FindControl<ScrollViewer>("EditorScroll")!;
        var overviewScroll = overview.FindControl<ScrollViewer>("OverviewScroll")!;
        var canvas = Assert.Single(view.GetVisualDescendants().OfType<BeamCanvas>());
        Assert.Equal(view.Bounds.Width, toolbar.Bounds.Width);
        Assert.Equal(toolbar.Bounds.Bottom, workspace.Bounds.Top);
        Assert.True(editorScroll.Bounds.Width > overview.Bounds.Width);
        Assert.True(overview.Bounds.Width >= 320);
        Assert.Equal(editorScroll.Bounds.Right, overview.Bounds.Left);
        Assert.True(canvas.Bounds.Width > 0 && canvas.Bounds.Height > 0);
        Assert.Equal(6, toolbar.GetVisualDescendants().OfType<ToggleButton>().Count(b => b.Command is not null));
        Assert.True(overviewScroll.Extent.Height > overviewScroll.Viewport.Height);
        Assert.Equal(ScrollBarVisibility.Disabled, overviewScroll.HorizontalScrollBarVisibility);
        var editorOffset = editorScroll.Offset;
        overviewScroll.Offset = new Vector(0, 100);
        Assert.True(overviewScroll.Offset.Y > 0);
        Assert.Equal(editorOffset, editorScroll.Offset);
        Assert.Contains(overview.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Long committed load name 35");
        Assert.True(overview.FindControl<StackPanel>("ReactionsSection")!.IsVisible);
        Assert.True(overview.FindControl<StackPanel>("IndicatorsPanel")!.IsVisible);
        Assert.False(overview.FindControl<TextBlock>("AnalysisStatus")!.IsVisible);
        AssertTables(overview);
        Assert.True(overview.FindControl<Grid>("ProjectBlock")!.Bounds.Height < 100);
        var projectText = overview.FindControl<Grid>("ProjectBlock")!.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text);
        Assert.Contains(culture == "de-DE" ? "Balkenmodell" : "Beam model", projectText);
        var materialAndLength = overview.FindControl<TextBlock>("MaterialAndLength")!;
        Assert.Equal(model.Editor.Overview.Material + " · " + model.Editor.Overview.Length,
            string.Concat(materialAndLength.Inlines!.OfType<Avalonia.Controls.Documents.Run>().Select(r => r.Text)));
        Assert.Single(materialAndLength.TextLayout.TextLines);
        Assert.Equal(culture == "de-DE" ? "Ändern" : "Change",
            Assert.Single(overview.FindControl<Grid>("ProjectBlock")!.GetVisualDescendants().OfType<Button>()).Content);
        Assert.DoesNotContain(overview.GetVisualDescendants().OfType<TextBlock>(),
            t => t.Text == Strings.Section || t.Text == Strings.Material);
        // Dense annotations increase the model's existing minimum height and overflow vertically.
        var surface = Assert.Single(view.GetVisualDescendants().OfType<BeamEditorSurface>());
        var axis = surface.FindControl<CoordinateAxisPane>("CoordinateAxis")!;
        Assert.True(editorScroll.Extent.Height > editorScroll.Viewport.Height);
        Assert.True(canvas.Bounds.Height >= canvas.Scene!.MinimumPaneHeight);
        Assert.Equal(canvas.Bounds.Bottom, axis.Bounds.Top);

        Assert.True(model.Editor.Session.Commit(new(model.Editor.Document.WithSupports([]), model.Editor.EditorPresentation)));
        Layout(window, width, height);
        Assert.False(overview.FindControl<StackPanel>("ReactionsSection")!.IsVisible);
        Assert.False(overview.FindControl<StackPanel>("IndicatorsPanel")!.IsVisible);
        var status = overview.FindControl<TextBlock>("AnalysisStatus")!;
        Assert.True(status.IsVisible);
        Assert.Equal(model.Editor.Presentation.StatusText, status.Text);
        Assert.Contains(overview.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Long committed load name 35");
    }

    [Theory]
    [InlineData("de-DE", 1100, 650)]
    [InlineData("en-US", 1250, 800)]
    [InlineData("de-DE", 1600, 900)]
    public void ModelAndAxisStayAtTopWithDiagramsBelowAndNoHeightDrivenGeometryChanges(
        string culture, double width, double height)
    {
        using var environment = new DesktopControlEnvironment();
        using var scope = new UiCultureScope(culture);
        var model = new MainWindowViewModel();
        var window = new MainWindow { DataContext = model };
        model.Setup.ApplyCommand.Execute(null);
        var document = new EditorDocument(Length.FromMillimeters(1000), ProjectTemplates.Material, ProjectTemplates.Section,
            [new(Guid.NewGuid(), Length.FromMillimeters(0), SupportType.Fixed, "A")],
            [new EditorPointForce(Guid.NewGuid(), Length.FromMillimeters(500), Force.FromNewtons(-13300), "F1")]);
        Assert.True(model.Editor!.Session.Commit(new(document, new())));
        Layout(window, width, height);
        var view = Assert.Single(window.GetVisualDescendants().OfType<EditorView>());
        var surface = Assert.Single(view.GetVisualDescendants().OfType<BeamEditorSurface>());
        var pane = surface.FindControl<Grid>("BeamPane")!;
        var canvas = surface.FindControl<BeamCanvas>("TechnicalCanvas")!;
        var axis = surface.FindControl<CoordinateAxisPane>("CoordinateAxis")!;
        var diagrams = view.FindControl<ScrollViewer>("DiagramScroll")!;
        var scroll = view.FindControl<ScrollViewer>("EditorScroll")!;
        Assert.Equal(0, surface.Bounds.Top);
        Assert.Equal(pane.Bounds.Bottom, axis.Bounds.Top);
        Assert.Equal(scroll.Bounds.Bottom, diagrams.Bounds.Top);
        Assert.Equal(SchematicMetrics.MinimumBeamPaneHeight, pane.Bounds.Height);
        Assert.Equal(124, canvas.Scene!.Frame.Viewport.BeamY);
        Assert.Equal(SchematicMetrics.BelowBeamSpace, pane.Bounds.Height - canvas.Scene.Frame.Viewport.BeamY);
        Assert.Equal(8, CoordinateAxisLayout.AxisY);
        double supportToAxisGap = axis.Bounds.Top + CoordinateAxisLayout.AxisY -
            canvas.Scene.Annotations.Where(a => a.IsSupport).Max(a => a.Bounds.Bottom);
        Assert.InRange(supportToAxisGap, 8, 12);
        Assert.All(canvas.Scene.Annotations, label =>
        {
            Assert.True(label.Bounds.Top >= 0);
            Assert.True(label.Bounds.Bottom <= pane.Bounds.Height);
        });
        Assert.True(axis.Bounds.Bottom <= scroll.Viewport.Height);
        Assert.True(diagrams.Bounds.Height > 200);
        Assert.Equal(3, view.GetVisualDescendants().OfType<ResultDiagram>().Count());
        Assert.Equal(ScrollBarVisibility.Disabled, scroll.HorizontalScrollBarVisibility);
        var frame = canvas.Scene.Frame;
        Assert.Equal(culture == "de-DE" ? "F1 = -13,3·10³ N" : "F1 = -13.3·10³ N",
            canvas.Scene.Annotations.Single(a => !a.IsSupport).Text);
        double axisTop = axis.Bounds.Top;
        double diagramHeight = diagrams.Bounds.Height;
        Layout(window, width, height + 200);
        Assert.Same(frame.Layout, canvas.Scene.Frame.Layout);
        Assert.Equal(frame.Viewport, canvas.Scene.Frame.Viewport);
        Assert.Equal(axisTop, axis.Bounds.Top);
        Assert.Equal(diagramHeight + 200, diagrams.Bounds.Height);

        // The existing length editor reserves only axis space, without moving the model.
        model.Editor.DimensionLength.Begin();
        Layout(window, width, height + 200);
        Assert.Same(frame.Layout, axis.StationLayout);
        Assert.Equal(frame.Viewport, canvas.Scene.Frame.Viewport);
        Assert.Equal(axisTop, axis.Bounds.Top);
        model.Editor.DimensionLength.Text = "invalid";
        Assert.False(model.Editor.DimensionLength.Confirm());
        Layout(window, width, height + 200);
        Assert.Equal(frame.Viewport, canvas.Scene.Frame.Viewport);
        Assert.Equal(axisTop, axis.Bounds.Top);
    }

    [Fact]
    public void ManualAnnotationOverflowRemainsVisibleAndResetRestoresTheFixedLowerSpace()
    {
        using var environment = new DesktopControlEnvironment();
        var model = new MainWindowViewModel();
        var window = new MainWindow { DataContext = model };
        model.Setup.ApplyCommand.Execute(null);
        var load = new EditorPointForce(Guid.NewGuid(), Length.FromMillimeters(500), Force.FromNewtons(-1000), "F1");
        var document = new EditorDocument(Length.FromMillimeters(1000), ProjectTemplates.Material, ProjectTemplates.Section,
            [new(Guid.NewGuid(), Length.FromMillimeters(0), SupportType.Fixed, "A")], [load]);
        Assert.True(model.Editor!.Session.Commit(new(document, new())));
        Layout(window);
        var canvas = Assert.Single(window.GetVisualDescendants().OfType<BeamCanvas>());
        var initial = canvas.Scene!.Frame;
        var overview = model.Editor.Overview;
        foreach (double dy in new[] { -600d, 600d })
        {
            model.Editor.SetAnnotationOffset(load.Id, new(0, dy));
            Layout(window);
            Assert.Same(initial.Layout, canvas.Scene!.Frame.Layout);
            Assert.Same(overview, model.Editor.Overview);
            Assert.Equal(dy, model.Editor.EditorPresentation.AnnotationOffsets[load.Id].Dy);
            Assert.All(canvas.Scene.Annotations, label =>
            {
                Assert.True(label.Bounds.Top >= 0);
                Assert.True(label.Bounds.Bottom <= canvas.Scene.Frame.Viewport.Height);
            });
            if (dy < 0)
                Assert.Equal(SchematicMetrics.BelowBeamSpace, canvas.Scene.Frame.Viewport.BelowBeamSpace);
            else
                Assert.True(canvas.Scene.Frame.Viewport.BelowBeamSpace > SchematicMetrics.BelowBeamSpace);
        }
        model.Editor.SetAnnotationOffset(load.Id, new(0, 0));
        Layout(window);
        Assert.Same(initial.Layout, canvas.Scene!.Frame.Layout);
        Assert.Equal(initial.Viewport, canvas.Scene.Frame.Viewport);
    }

    [Fact]
    public void VerticalOverflowAndScrollingPreserveHorizontalGeometryAndSidebarOffset()
    {
        using var environment = new DesktopControlEnvironment();
        var model = new MainWindowViewModel();
        var window = new MainWindow { DataContext = model };
        model.Setup.ApplyCommand.Execute(null);
        Layout(window);
        var view = Assert.Single(window.GetVisualDescendants().OfType<EditorView>());
        var editorScroll = view.FindControl<ScrollViewer>("EditorScroll")!;
        var scroll = view.FindControl<ScrollViewer>("DiagramScroll")!;
        var canvas = Assert.Single(view.GetVisualDescendants().OfType<BeamCanvas>());
        var axis = Assert.Single(view.GetVisualDescendants().OfType<CoordinateAxisPane>());
        var overview = Assert.Single(view.GetVisualDescendants().OfType<ProjectOverviewView>());
        var sidebarScroll = overview.FindControl<ScrollViewer>("OverviewScroll")!;
        var frame = canvas.Scene!.Frame;
        double axisTop = axis.Bounds.Top;
        Assert.True(editorScroll.Extent.Height <= editorScroll.Viewport.Height);
        Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
        Assert.Equal(frame.Viewport, canvas.Scene.Frame.Viewport);
        Assert.Same(frame.Layout, canvas.Scene.Frame.Layout);
        Assert.Same(frame.Layout, axis.StationLayout);
        Assert.Equal(axisTop, axis.Bounds.Top);
        var sidebarOffset = sidebarScroll.Offset;
        var editorOffset = editorScroll.Offset;
        double requestedOffset = Math.Min(100, scroll.Extent.Height - scroll.Viewport.Height);
        scroll.Offset = new Vector(0, requestedOffset);
        Layout(window);
        Assert.Equal(requestedOffset, scroll.Offset.Y);
        Assert.Equal(editorOffset, editorScroll.Offset);
        Assert.Equal(sidebarOffset, sidebarScroll.Offset);
        Assert.Equal(frame.Viewport, canvas.Scene.Frame.Viewport);
        Assert.Same(frame.Layout, canvas.Scene.Frame.Layout);
        Assert.Equal(axisTop, axis.Bounds.Top);
    }

    [Fact]
    public void EmptyEntityTablesHideTheirHeadersAndKeepTheExistingAnalysisStatus()
    {
        using var environment = new DesktopControlEnvironment();
        var model = new MainWindowViewModel();
        var window = new MainWindow { DataContext = model };
        model.Setup.ApplyCommand.Execute(null);
        Layout(window);
        var overview = Assert.Single(window.GetVisualDescendants().OfType<ProjectOverviewView>());
        foreach (string tableName in new[] { "SupportTable", "LoadTable" })
        {
            var table = overview.FindControl<ItemsControl>(tableName)!;
            Assert.Equal(0, table.ItemCount);
            Assert.False(((Control)table.Parent!).IsVisible);
        }
        Assert.False(overview.FindControl<StackPanel>("ReactionsSection")!.IsVisible);
        Assert.True(overview.FindControl<TextBlock>("AnalysisStatus")!.IsVisible);
        Assert.Equal(model.Editor!.Presentation.StatusText, overview.FindControl<TextBlock>("AnalysisStatus")!.Text);
        Assert.Single(overview.GetVisualDescendants().OfType<Button>());
    }

    private static void AssertTables(ProjectOverviewView overview)
    {
        foreach (var (name, columns) in new[] { ("SupportTable", 3), ("LoadTable", 3), ("ReactionTable", 4) })
        {
            var table = overview.FindControl<ItemsControl>(name)!;
            var header = ((StackPanel)table.Parent!).Children.OfType<Grid>().Single(g => g.Classes.Contains("tableHeader"));
            var rows = table.GetVisualDescendants().OfType<Grid>().Where(g => g.Classes.Contains("overviewRow")).ToArray();
            Assert.Equal(table.ItemCount, rows.Length);
            Assert.NotEmpty(rows);
            Assert.Equal(TextAlignment.Center, header.Children.OfType<TextBlock>().First().TextAlignment);
            if (name == "SupportTable")
            {
                Assert.Equal(Strings.OverviewPosition, header.Children.OfType<TextBlock>().Last().Text);
                Assert.True(header.ColumnDefinitions[0].ActualWidth > 48);
                Assert.Equal(TextAlignment.Center, header.Children.OfType<TextBlock>().ElementAt(1).TextAlignment);
            }
            if (name == "LoadTable")
            {
                // More space for positions/ranges moves the value column left.
                Assert.True(header.ColumnDefinitions[1].ActualWidth < header.ColumnDefinitions[2].ActualWidth);
                double previousValueRight = 48 + 6 + (header.Bounds.Width - 48 - 12) / 2;
                Assert.True(header.ColumnDefinitions[0].ActualWidth + 6 + header.ColumnDefinitions[1].ActualWidth < previousValueRight);
                Assert.Equal(new[] { Strings.OverviewName, Strings.OverviewValue, "Position [mm]" },
                    header.Children.OfType<TextBlock>().Select(t => t.Text));
                foreach (var row in rows)
                {
                    var load = Assert.IsType<ProjectOverviewItem>(row.DataContext);
                    Assert.Equal(new[] { load.Name, load.Value, load.Position },
                        row.Children.Select(c => c is Button button ? ((TextBlock)button.Content!).Text : ((TextBlock)c).Text));
                }
            }
            foreach (var row in rows)
            {
                Assert.Equal(columns, row.ColumnDefinitions.Count);
                var action = Assert.Single(row.Children.OfType<Button>());
                Assert.Contains("overviewEntityAction", action.Classes);
                Assert.True(action.Focusable);
                Assert.Equal(Avalonia.Layout.HorizontalAlignment.Center, action.HorizontalContentAlignment);
                var caption = Assert.IsType<TextBlock>(action.Content);
                Assert.Equal(TextAlignment.Center, caption.TextAlignment);
                // Check the arranged caption, not just its text-alignment property.
                // The header's bounds exclude the grid spacing included in ActualWidth.
                var nameHeader = header.Children.OfType<TextBlock>().First();
                Assert.Equal(nameHeader.Bounds.Left, action.Bounds.Left);
                Assert.Equal(nameHeader.Bounds.Width, action.Bounds.Width);
                var center = caption.TranslatePoint(new Point(caption.Bounds.Width / 2, caption.Bounds.Height / 2), row)!.Value;
                Assert.InRange(Math.Abs(center.X - nameHeader.Bounds.Center.X), 0, 0.5);
                Assert.InRange(Math.Abs(center.Y - row.Bounds.Height / 2), 0, 0.5);
                Assert.True(caption.Bounds.Width <= caption.DesiredSize.Width + 1);
                Assert.Equal(header.Bounds.Width, row.Bounds.Width);
                for (int i = 0; i < columns; i++)
                    Assert.Equal(header.ColumnDefinitions[i].ActualWidth, row.ColumnDefinitions[i].ActualWidth);
                foreach (var number in row.Children.OfType<TextBlock>().Where(t => t.Classes.Contains("number")))
                    Assert.Equal(TextAlignment.Right, number.TextAlignment);
            }
        }
    }

    private static void Layout(Window window, double width = 1250, double height = 800)
    {
        window.ApplyTemplate();
        window.Measure(new Size(width, height));
        window.Arrange(new Rect(0, 0, width, height));
        // The inert window is never shown; perform its content layout directly.
        var content = (Control)window.Content!;
        // Its hidden root does not schedule layout for replacement item containers.
        foreach (var child in content.GetVisualDescendants().OfType<Control>())
        {
            child.InvalidateMeasure();
            child.InvalidateArrange();
        }
        content.InvalidateMeasure();
        content.Measure(new Size(width, height));
        content.InvalidateArrange();
        content.Arrange(new Rect(0, 0, width, height));
        if (window.GetVisualDescendants().OfType<EditorView>().SingleOrDefault() is { } editor)
        {
            // The inert platform reports a fixed native client size; set the tested editor size explicitly.
            editor.Width = width;
            editor.Height = height - 32;
            // A viewport change updates content MinHeight during arrange; settle its follow-up measure too.
            for (int pass = 0; pass < 2; pass++)
            {
                foreach (var child in editor.GetVisualDescendants().OfType<Control>())
                {
                    child.InvalidateMeasure();
                    child.InvalidateArrange();
                }
                editor.InvalidateMeasure();
                editor.Measure(new Size(width, height - 32));
                editor.InvalidateArrange();
                editor.Arrange(new Rect(0, 0, width, height - 32));
            }
        }
    }

}
