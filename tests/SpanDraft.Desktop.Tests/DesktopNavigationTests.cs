using System.Globalization;
using Avalonia;
using Avalonia.Automation;
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
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.State;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.ViewModels;
using SpanDraft.Desktop.Views;
using Xunit;

namespace SpanDraft.Tests;

[Collection("Avalonia")]
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
        var model = new MainWindowViewModel(); model.NewCommand.Execute(null);
        var window = new MainWindow { DataContext = model };
        ProjectTestSupport.CompleteSetup(model); model.Setup.ApplyCommand.Execute(null);
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
        var model = new MainWindowViewModel(beam => { analyses++; return BeamAnalysis.Analyze(beam); }); model.NewCommand.Execute(null);
        var window = new MainWindow { DataContext = model };
        ProjectTestSupport.CompleteSetup(model); model.Setup.ApplyCommand.Execute(null);
        var state = ProjectTestSupport.State();
        Assert.True(model.Editor!.Session.Commit(new(state.Document with { Length = Length.FromMeters(1) }, state.Presentation)));
        var editor = model.Editor;
        editor.Session.MarkSaved(editor.Session.CurrentRevision, ProjectTestSupport.TestPath("overview-test.spandraft"));
        Layout(window);
        var overview = Assert.Single(window.GetVisualDescendants().OfType<ProjectOverviewView>());
        var surface = Assert.Single(window.GetVisualDescendants().OfType<BeamEditorSurface>());
        var revision = editor.Session.CurrentRevision;
        var projection = editor.Overview;
        int historyCount = editor.Session.UndoHistory.Count;
        int initialAnalyses = analyses;
        foreach (string tableName in new[] { "SupportTable", "ForceTable", "MomentTable", "DistributedLoadTable", "ReactionTable" })
        {
            var buttons = overview.FindControl<ItemsControl>(tableName)!.GetVisualDescendants().OfType<Button>().ToArray();
            Assert.NotEmpty(buttons);
            foreach (var button in buttons)
            {
                Guid id = button.DataContext is ProjectOverviewItem item ? item.Id : ((ProjectOverviewReaction)button.DataContext!).Id!.Value;
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
                    editor.DistributedLoadDraft.StartIntensityText = "-";
                    Assert.False(editor.ConfirmDistributedLoad());
                }
                ActivateName(button, window, keyboard);
                Assert.Same(draft, (object?)editor.SupportDraft ?? (object?)editor.LoadDraft ?? editor.DistributedLoadDraft);
                Assert.Equal("-", editor.SupportDraft?.PositionText ?? editor.LoadDraft?.ValueText ?? editor.DistributedLoadDraft?.StartIntensityText);
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
        var model = new MainWindowViewModel(); model.NewCommand.Execute(null);
        var window = new MainWindow { DataContext = model };
        ProjectTestSupport.CompleteSetup(model); model.Setup.ApplyCommand.Execute(null);
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
        var other = overview.FindControl<ItemsControl>("ForceTable")!.GetVisualDescendants().OfType<Button>().First();
        editor.IsMainMenuOpen = true;
        other.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Same(draft, editor.SupportDraft);
        editor.IsMainMenuOpen = false;
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

    [Fact]
    public void SharedStationReactionRowIsNotAnEditableEntityWhileSingleRowsStillOpenTheirSupport()
    {
        using var environment = new DesktopControlEnvironment();
        var model = new MainWindowViewModel(); model.NewCommand.Execute(null);
        var window = new MainWindow { DataContext = model };
        ProjectTestSupport.CompleteSetup(model); model.Setup.ApplyCommand.Execute(null);
        var state = ProjectTestSupport.State();
        // Two supports of different type at the same station are valid outside the interactive placement path.
        var supports = state.Document.Supports;
        Assert.True(model.Editor!.Session.Commit(new(
            (state.Document with { Length = Length.FromMeters(1) }).WithSupports(
                [supports[0], supports[1], supports[2] with { Position = supports[1].Position }]), state.Presentation)));
        Layout(window);
        var editor = model.Editor;
        var overview = Assert.Single(window.GetVisualDescendants().OfType<ProjectOverviewView>());
        var buttons = overview.FindControl<ItemsControl>("ReactionTable")!.GetVisualDescendants().OfType<Button>().ToArray();
        Assert.Equal(editor.Document.Supports.Count - 1, buttons.Length);
        var revision = editor.Session.CurrentRevision;

        var shared = Assert.Single(buttons, b => ((ProjectOverviewReaction)b.DataContext!).Id is null);
        var caption = Assert.IsAssignableFrom<TextBlock>(shared.Content);
        Assert.Equal("B + C", caption.Text);
        Assert.False(Assert.Single(caption.TextLayout.TextLines).HasCollapsed);
        Assert.False(shared.IsHitTestVisible);
        Assert.False(shared.Focusable);
        var nameCell = Assert.Single(((Panel)shared.Parent!).Children.OfType<Border>());
        Assert.Null(ToolTip.GetTip(nameCell));
        Assert.Null(ToolTip.GetTip(shared));
        Assert.True(nameCell.IsHitTestVisible);
        Assert.NotNull(nameCell.Background);
        Assert.False(nameCell.Focusable);
        Assert.Equal(OverviewTableRow.GetColumn(shared), OverviewTableRow.GetColumn(nameCell));
        Assert.True(nameCell.Bounds.Contains(shared.Bounds));
        // The hidden native root has no hit testing; the inert button above the cell is what lets it be hit.
        shared.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        shared.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        Assert.Null(editor.SupportDraft);
        Assert.False(editor.IsSupportFlyoutVisible);

        foreach (var single in buttons.Where(b => b != shared))
        {
            var row = (ProjectOverviewReaction)single.DataContext!;
            Assert.True(row.IsEditable);
            Assert.True(single.IsHitTestVisible);
            Assert.True(single.Focusable);
            Assert.Equal(row.Name, AutomationProperties.GetName(single));
            Assert.False(Assert.Single(Assert.IsAssignableFrom<TextBlock>(single.Content).TextLayout.TextLines).HasCollapsed);
            Assert.Null(ToolTip.GetTip(single));
            ActivateName(single, window, true);
            Assert.Equal(row.Id, editor.SupportDraft!.OriginalId);
            editor.CancelEditorInteraction();
        }
        Assert.Same(revision, editor.Session.CurrentRevision);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("en-US")]
    public void OverviewEntryUsesExistingRenameCommitAndHistoryAnalysisSemantics(string culture)
    {
        using var environment = new DesktopControlEnvironment();
        using var scope = new UiCultureScope(culture);
        int analyses = 0;
        var model = new MainWindowViewModel(beam => { analyses++; return BeamAnalysis.Analyze(beam); }); model.NewCommand.Execute(null);
        var window = new MainWindow { DataContext = model };
        ProjectTestSupport.CompleteSetup(model); model.Setup.ApplyCommand.Execute(null);
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
        var force = overview.FindControl<ItemsControl>("ForceTable")!.GetVisualDescendants().OfType<Button>()
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
        var source = Assert.IsAssignableFrom<TextBlock>(button.Content);
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
    public void SetupAllowsClearedDataContextDuringDetachment()
    {
        using var environment = new DesktopControlEnvironment();
        var view = new ProjectSetupView();
        view.DataContext = null;
        Assert.Empty(view.Cards());
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
            var model = new MainWindowViewModel(beam => { analyses++; return BeamAnalysis.Analyze(beam); }); model.NewCommand.Execute(null);
            // Build the real window/XAML, but never show it or run startup recovery.
            var window = new MainWindow { DataContext = model };
            Layout(window);
            Assert.Single(window.GetVisualDescendants().OfType<ProjectSetupView>());
            var host = Assert.Single(((Grid)window.Content!).Children.OfType<ContentControl>());
            Assert.Same(model.Setup, host.Content);

            ProjectTestSupport.CompleteSetup(model); model.Setup.ApplyCommand.Execute(null);
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
            Assert.Equal(11, view.GetVisualDescendants().OfType<ToggleButton>().Count(b => b.Command is not null));
            Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == editor.Presentation.StatusText);
            Assert.Empty(overview.GetVisualDescendants().OfType<Button>());
            var properties = view.FindControl<Button>("ProjectPropertiesToolbarButton")!;
            Assert.Same(editor.ChangeProjectCommand, properties.Command);
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<Button>(), b => !b.IsEnabled);
            Assert.True(editor.Session.IsDirty);
            Assert.Empty(editor.Session.UndoHistory);
            Assert.Equal(Strings.Untitled + "* — " + Strings.ApplicationTitle, window.Title);
            Assert.Equal(1, analyses);

            // Editing the project uses the same setup; cancelling must restore the editor too.
            var revision = editor.Session.CurrentRevision;
            if (culture == "de-DE")
            {
                editor.ForceToolCommand.Execute(null);
                Assert.True(editor.IsForceTool);
            }
            else
            {
                editor.DimensionLength.Begin(); editor.DimensionLength.Text = "9000";
                Assert.True(editor.DimensionLength.IsEditing);
            }
            typeof(Button).GetMethod("OnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(properties, null);
            Assert.False(editor.IsForceTool);
            Assert.Null(editor.LoadDraft);
            Assert.False(editor.DimensionLength.IsEditing);
            Assert.Same(revision, editor.Session.CurrentRevision);
            Layout(window);
            Assert.Single(window.GetVisualDescendants().OfType<ProjectSetupView>());
            Assert.Contains(window.GetVisualDescendants().OfType<Button>(),
                b => Equals(b.Content, culture == "de-DE" ? "Übernehmen" : "Apply"));
            Assert.Equal(culture == "de-DE" ? "Rückgängig" : "Undo", window.FindControl<MenuItem>("UndoMenuItem")!.Header);
            model.Setup.CancelCommand.Execute(null);
            Layout(window);
            Assert.Same(editor, host.Content);
            Assert.Same(revision, editor.Session.CurrentRevision);
            Assert.Single(window.GetVisualDescendants().OfType<BeamCanvas>());
            Assert.Equal(1, analyses);
        }
        finally { CultureInfo.CurrentUICulture = previousCulture; }
    }

    [Theory]
    [InlineData("de-DE", 1100, 650)]
    [InlineData("en-US", 1250, 800)]
    [InlineData("de-DE", 1600, 900)]
    [InlineData("de-DE", 2400, 900)]
    public void WorkspaceKeepsToolbarIndependentAndOverviewScrollableAtSupportedSizes(string culture, double width, double height)
    {
        using var environment = new DesktopControlEnvironment();
        using var scope = new UiCultureScope(culture);
        var model = new MainWindowViewModel(); model.NewCommand.Execute(null);
        var window = new MainWindow { DataContext = model };
        ProjectTestSupport.CompleteSetup(model); model.Setup.ApplyCommand.Execute(null);
        var state = ProjectTestSupport.State();
        var loads = Enumerable.Range(1, 35).Select(i => (EditorPointLoad)new EditorPointForce(Guid.NewGuid(),
            Length.FromMillimeters(500), Force.FromNewtons(-i), "Long committed load name " + i));
        var material = MaterialTestEntries.Entry(new string('W', 120));
        var document = state.Document.WithLoads(loads) with { Length = Length.FromMeters(1), Material = material };
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
        Assert.Equal(340, overview.Bounds.Width);
        Assert.Equal(editorScroll.Bounds.Right, overview.Bounds.Left);
        Assert.True(canvas.Bounds.Width > 0 && canvas.Bounds.Height > 0);
        Assert.Equal(11, toolbar.GetVisualDescendants().OfType<ToggleButton>().Count(b => b.Command is not null));
        Assert.True(overviewScroll.Extent.Height > overviewScroll.Viewport.Height);
        Assert.Equal(ScrollBarVisibility.Disabled, overviewScroll.HorizontalScrollBarVisibility);
        var editorOffset = editorScroll.Offset;
        overviewScroll.Offset = new Vector(0, 100);
        Assert.True(overviewScroll.Offset.Y > 0);
        Assert.Equal(editorOffset, editorScroll.Offset);
        Assert.Contains(overview.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Long committed load name 35");
        Assert.True(overview.FindControl<StackPanel>("ReactionsSection")!.IsVisible);
        Assert.True(overview.FindControl<StackPanel>("IndicatorsPanel")!.IsVisible);
        Assert.Null(overview.FindControl<TextBlock>("AnalysisStatus"));
        Assert.False(view.FindControl<Grid>("ResultStatus")!.IsVisible);
        AssertTables(overview);
        var projectText = overview.FindControl<Grid>("ProjectBlock")!.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text);
        Assert.Contains(culture == "de-DE" ? "Balkenmodell" : "Beam model", projectText);
        // The one segment is a block of its own: its heading and one description that wraps like text and is never shortened.
        var title = overview.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "SegmentBlockTitle");
        var materialDescription = overview.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "SegmentBlockDescription");
        Assert.Equal(Strings.SegmentSelected.Replace("{0}", "1"), title.Text);
        Assert.Equal(material.Name + " (1.4567) · UNS Example · AISI Shop", model.Editor.Overview.Material);
        Assert.Equal(model.Editor.Overview.Segments[0].Section + " · " + model.Editor.Overview.Material, materialDescription.Text);
        Assert.Equal(TextWrapping.Wrap, materialDescription.TextWrapping);
        Assert.Equal(TextTrimming.None, materialDescription.TextTrimming);
        Assert.True(materialDescription.TextLayout.TextLines.Count > 1);
        Assert.All(materialDescription.TextLayout.TextLines, line => Assert.False(line.HasCollapsed));
        Assert.True(materialDescription.Bounds.Height >= materialDescription.TextLayout.Height);
        Assert.True(overviewScroll.Extent.Width <= overviewScroll.Viewport.Width);
        var materialBounds = new Rect(materialDescription.TranslatePoint(default, overview)!.Value, materialDescription.Bounds.Size);
        Assert.True(new Rect(overview.Bounds.Size).Contains(materialBounds));
        var projectBlock = overview.FindControl<Grid>("ProjectBlock")!;
        Assert.Empty(projectBlock.GetVisualDescendants().OfType<Button>());
        Assert.Empty(projectBlock.ColumnDefinitions);
        // The block uses the full width of the side bar: there is no position column.
        Assert.True(materialDescription.TranslatePoint(new Point(materialDescription.Bounds.Width, 0), projectBlock)!.Value.X >= projectBlock.Bounds.Width - 1);
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
        Assert.False(overview.FindControl<StackPanel>("IndicatorsSection")!.IsVisible);
        Assert.Null(overview.FindControl<TextBlock>("AnalysisStatus"));
        var status = view.FindControl<TextBlock>("AnalysisStatus")!;
        Assert.True(view.FindControl<Grid>("ResultStatus")!.IsVisible);
        Assert.True(status.IsVisible);
        Assert.Equal(model.Editor.Presentation.StatusText, status.Text);
        Assert.Contains(overview.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Long committed load name 35");
    }

    [Theory]
    [InlineData("de-DE", 1100, 650)]
    [InlineData("en-US", 1250, 800)]
    [InlineData("de-DE", 1600, 900)]
    public void ModelAndAxisStayAtTopWithDiagramsBelowAndOnlyWidthDrivenGeometryChanges(
        string culture, double width, double height)
    {
        using var environment = new DesktopControlEnvironment();
        using var scope = new UiCultureScope(culture);
        var model = new MainWindowViewModel(); model.NewCommand.Execute(null);
        var window = new MainWindow { DataContext = model };
        ProjectTestSupport.CompleteSetup(model); model.Setup.ApplyCommand.Execute(null);
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
        var divider = view.FindControl<Border>("ResultsDivider")!;
        Assert.Equal(scroll.Bounds.Bottom, divider.Bounds.Top);
        Assert.Equal(divider.Bounds.Bottom, diagrams.Bounds.Top);
        Assert.True(pane.Bounds.Height >= SchematicMetrics.MinimumBeamPaneHeight);
        Assert.Equal(canvas.Scene!.MinimumPaneHeight, pane.Bounds.Height);
        Assert.True(canvas.Scene!.Frame.Viewport.BeamY > 0);
        Assert.Equal(canvas.Scene.RequiredBelowBeamSpace, pane.Bounds.Height - canvas.Scene.Frame.Viewport.BeamY);
        Assert.True(canvas.Scene.RequiredBelowBeamSpace >= SchematicMetrics.BelowBeamSpace);
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
        Assert.Equal(culture == "de-DE" ? "F1 = −13\u202f300 N" : "F1 = −13\u202f300 N",
            canvas.Scene.Annotations.Single(a => !a.IsSupport).Text);
        double axisTop = axis.Bounds.Top;
        double diagramHeight = diagrams.Bounds.Height;
        double viewportWidth = scroll.Viewport.Width;
        Layout(window, width, height + 200);
        if (scroll.Viewport.Width == viewportWidth)
        {
            Assert.Same(frame.Layout, canvas.Scene.Frame.Layout);
            Assert.Equal(frame.Viewport, canvas.Scene.Frame.Viewport);
        }
        else
        {
            // Removing the last scrollbar gutter returns its width to both viewports.
            Assert.True(scroll.Viewport.Width > viewportWidth);
            Assert.Equal(scroll.Bounds.Width, scroll.Viewport.Width);
            Assert.Equal(scroll.Viewport.Width, diagrams.Viewport.Width);
            Assert.Equal(scroll.Viewport.Width, surface.Bounds.Width);
            Assert.All(view.GetVisualDescendants().OfType<ResultDiagram>(), diagram =>
                Assert.Same(surface.StationLayout, diagram.Projection!.StationLayout));
            frame = canvas.Scene.Frame;
        }
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
        var model = new MainWindowViewModel(); model.NewCommand.Execute(null);
        var window = new MainWindow { DataContext = model };
        ProjectTestSupport.CompleteSetup(model); model.Setup.ApplyCommand.Execute(null);
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
                Assert.Equal(initial.Viewport.BelowBeamSpace, canvas.Scene.Frame.Viewport.BelowBeamSpace, 10);
            else
                Assert.True(canvas.Scene.Frame.Viewport.BelowBeamSpace > initial.Viewport.BelowBeamSpace);
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
        var model = new MainWindowViewModel(); model.NewCommand.Execute(null);
        var window = new MainWindow { DataContext = model };
        ProjectTestSupport.CompleteSetup(model); model.Setup.ApplyCommand.Execute(null);
        Assert.True(model.Editor!.Session.Commit(ProjectTestSupport.State()));
        Assert.True(model.Editor.Presentation.IsSuccess, model.Editor.Presentation.StatusText);
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

    [Theory]
    [InlineData("de-DE", 0)] [InlineData("en-US", 0)]
    [InlineData("de-DE", 1)] [InlineData("en-US", 1)]
    [InlineData("de-DE", 2)] [InlineData("en-US", 2)]
    [InlineData("de-DE", 3)] [InlineData("en-US", 3)]
    [InlineData("de-DE", 4)] [InlineData("en-US", 4)]
    public void EmptyLoadSectionsHideTheirEntireLayout(string culture, int group)
    {
        using var environment = new DesktopControlEnvironment();
        using var scope = new UiCultureScope(culture);
        var model = new MainWindowViewModel(); model.NewCommand.Execute(null);
        var window = new MainWindow { DataContext = model };
        ProjectTestSupport.CompleteSetup(model); model.Setup.ApplyCommand.Execute(null);
        var state = ProjectTestSupport.State();
        var doc = state.Document;
        doc = doc.WithLoads(group == 4 ? doc.Loads : doc.Loads.Where(l =>
            group == 1 && l.Kind == PointLoadKind.Force || group == 2 && l.Kind == PointLoadKind.Moment));
        doc = doc.WithDistributedLoads(group is 3 or 4 ? doc.DistributedLoads : []);
        Assert.True(model.Editor!.Session.Commit(new(doc, state.Presentation)));
        Layout(window);
        var overview = Assert.Single(window.GetVisualDescendants().OfType<ProjectOverviewView>());
        var sections = new[] { ("ForceSection", group is 1 or 4), ("MomentSection", group is 2 or 4),
            ("DistributedLoadSection", group is 3 or 4) };
        var visible = new List<StackPanel>();
        foreach (var (name, expected) in sections)
        {
            var section = overview.FindControl<StackPanel>(name)!;
            Assert.Equal(expected, section.IsVisible);
            if (expected) visible.Add(section);
            else Assert.Equal(0, section.Bounds.Height);
        }
        foreach (var pair in visible.Zip(visible.Skip(1)))
            Assert.True(pair.First.Bounds.Bottom <= pair.Second.Bounds.Top);
    }

    [Fact]
    public void EmptyEntityTablesHideTheirHeadersAndLeaveAnalysisStatusToResults()
    {
        using var environment = new DesktopControlEnvironment();
        var model = new MainWindowViewModel(); model.NewCommand.Execute(null);
        var window = new MainWindow { DataContext = model };
        ProjectTestSupport.CompleteSetup(model); model.Setup.ApplyCommand.Execute(null);
        Layout(window);
        var overview = Assert.Single(window.GetVisualDescendants().OfType<ProjectOverviewView>());
        foreach (string tableName in new[] { "SupportTable", "ForceTable", "MomentTable", "DistributedLoadTable" })
        {
            var table = overview.FindControl<ItemsControl>(tableName)!;
            Assert.Equal(0, table.ItemCount);
            Assert.False(((Control)table.Parent!.Parent!).IsVisible);
        }
        Assert.False(overview.FindControl<StackPanel>("ReactionsSection")!.IsVisible);
        Assert.False(overview.FindControl<StackPanel>("IndicatorsSection")!.IsVisible);
        Assert.Null(overview.FindControl<TextBlock>("AnalysisStatus"));
        var view = Assert.Single(window.GetVisualDescendants().OfType<EditorView>());
        var status = view.FindControl<TextBlock>("AnalysisStatus")!;
        Assert.True(view.FindControl<Grid>("ResultStatus")!.IsVisible);
        Assert.True(status.IsVisible);
        Assert.Equal(model.Editor!.Presentation.StatusText, status.Text);
        Assert.Empty(overview.GetVisualDescendants().OfType<Button>());
    }

    [Theory]
    [InlineData("de-DE", 1300, 800)] [InlineData("en-US", 1300, 800)]
    [InlineData("de-DE", 1100, 650)] [InlineData("en-US", 1100, 650)]
    public void SpringDetailsSitBelowTheTypeAndCountForItsColumn(string culture, double width, double height)
    {
        using var environment = new DesktopControlEnvironment();
        using var scope = new UiCultureScope(culture);
        var model = new MainWindowViewModel(); model.NewCommand.Execute(null);
        var window = new MainWindow { DataContext = model };
        ProjectTestSupport.CompleteSetup(model); model.Setup.ApplyCommand.Execute(null);
        var editor = model.Editor!;
        Assert.True(editor.Session.Commit(new(SpringDocument(editor), editor.Session.CurrentRevision.State.Presentation)));
        Layout(window, width, height);
        var overview = Assert.Single(window.GetVisualDescendants().OfType<ProjectOverviewView>());
        Assert.Equal(340, overview.Bounds.Width);
        AssertTables(overview);
        var rows = TableRows(overview, "SupportTable").Where(r => !r.IsHeader).ToArray();
        Assert.Equal(6, rows.Length);
        foreach (var row in rows)
        {
            var item = Assert.IsType<ProjectOverviewItem>(row.DataContext);
            var type = row.Children.OfType<TextBlock>().First(); var position = row.Children.OfType<TextBlock>().Last();
            Assert.Equal(Plain(position.Text), Plain(item.Position));
            Assert.Equal(type.Bounds.Top, position.Bounds.Top, 0.5);
            var details = row.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Classes.Contains("supportDetail")).ToArray();
            Assert.Equal(item.Details.Select(Plain), details.Select(d => Plain(d.Text)));
            foreach (var detail in details)
            {
                // Details start at the type's edge, below it, and never wrap inside a number or unit.
                Assert.Equal(type.TranslatePoint(default, row)!.Value.X, detail.TranslatePoint(default, row)!.Value.X, 0.5);
                Assert.True(detail.TranslatePoint(default, row)!.Value.Y >= type.Bounds.Bottom - 0.5);
                Assert.Single(detail.TextLayout.TextLines);
            }
            // Type and details follow at one line pitch.
            var tops = details.Select(d => d.TranslatePoint(default, row)!.Value.Y).Prepend(type.Bounds.Top).ToArray();
            for (int i = 1; i < tops.Length; i++) Assert.Equal(type.Bounds.Height, tops[i] - tops[i - 1], 0.5);
        }
        var shared = Assert.Single(TableRows(overview, "ReactionTable"),
            row => !row.IsHeader && row.DataContext is ProjectOverviewReaction { Id: null });
        var caption = Assert.IsAssignableFrom<TextBlock>(Assert.Single(shared.Children.OfType<Button>()).Content);
        Assert.Equal("Wegfeder rechts + E", caption.Text);
        Assert.Equal(TextTrimming.CharacterEllipsis, caption.TextTrimming);
        Assert.True(Assert.Single(caption.TextLayout.TextLines).HasCollapsed);
        var nameCell = Assert.Single(shared.Children.OfType<Border>());
        Assert.Equal("Wegfeder rechts + E", ToolTip.GetTip(nameCell));
    }

    [Fact]
    public void OverviewNameTooltipsFollowTheArrangedTextAndKeepFullAutomationNames()
    {
        using var environment = new DesktopControlEnvironment();
        var model = new MainWindowViewModel(); model.NewCommand.Execute(null);
        var window = new MainWindow { DataContext = model };
        ProjectTestSupport.CompleteSetup(model); model.Setup.ApplyCommand.Execute(null);
        var editor = model.Editor!;
        Assert.True(editor.Session.Commit(new(SpringDocument(editor), editor.Session.CurrentRevision.State.Presentation)));
        Layout(window);
        var overview = Assert.Single(window.GetVisualDescendants().OfType<ProjectOverviewView>());
        var buttons = overview.FindControl<ItemsControl>("SupportTable")!.GetVisualDescendants().OfType<Button>().ToArray();

        AssertName("A", trimmed: false);
        AssertName("Kranbahnstütze links außen", trimmed: true);
        window.Close();

        void AssertName(string name, bool trimmed)
        {
            var button = Assert.Single(buttons, b => ((ProjectOverviewItem)b.DataContext!).Name == name);
            var caption = Assert.IsAssignableFrom<TextBlock>(button.Content);
            Assert.Equal(trimmed, Assert.Single(caption.TextLayout.TextLines).HasCollapsed);
            Assert.Equal(trimmed ? name : null, ToolTip.GetTip(button));
            Assert.Equal(name, AutomationProperties.GetName(button));
        }
    }

    [Theory]
    [InlineData("de-DE")] [InlineData("en-US")]
    public void OverviewTablesSettleOnOneLayoutAndWrapALongValueOnlyAfterItsEllipsis(string culture)
    {
        using var environment = new DesktopControlEnvironment();
        using var scope = new UiCultureScope(culture);
        var model = new MainWindowViewModel(); model.NewCommand.Execute(null);
        ProjectTestSupport.CompleteSetup(model); model.Setup.ApplyCommand.Execute(null);
        var editor = model.Editor!;
        Assert.True(editor.Session.Commit(new(SpringDocument(editor, longLinearLoad: true), editor.Session.CurrentRevision.State.Presentation)));
        // An unrealistically long linear load forces the wrapping path in the fixed panel width.
        var view = new ProjectOverviewView { DataContext = editor, Width = 340 };
        var window = new Window { Content = view, Width = 340, Height = 600 };
        window.Show();
        for (int pass = 0; pass < 3; pass++) { window.Measure(new Size(340, 600)); window.Arrange(new Rect(0, 0, 340, 600)); window.UpdateLayout(); }
        var linear = TableRows(view, "DistributedLoadTable").Where(r => !r.IsHeader).Single(r => ((ProjectOverviewItem)r.DataContext!).Name == "q1");
        var value = linear.Children.OfType<TextBlock>().First();
        Assert.Equal(2, value.TextLayout.TextLines.Count);
        Assert.Equal(value.Text!.IndexOf("… ", StringComparison.Ordinal) + 2, value.TextLayout.TextLines[1].FirstTextSourceIndex);
        AssertTables(view, wrapping: true);
        // A layout that a row's own report changes again would never settle: after a full relayout every row is valid
        // and every cell stays where it was.
        var rows = view.GetVisualDescendants().OfType<OverviewTableRow>().Where(r => r.IsEffectivelyVisible).ToArray();
        Assert.True(rows.Length > 10);
        var before = rows.SelectMany(r => r.Children).Select(c => c.Bounds).ToArray();
        foreach (var row in rows) row.InvalidateMeasure();
        window.UpdateLayout();
        Assert.All(rows, row => Assert.True(row.IsMeasureValid && row.IsArrangeValid));
        Assert.Equal(before, rows.SelectMany(r => r.Children).Select(c => c.Bounds).ToArray());
        window.Close();
    }

    private static EditorDocument SpringDocument(EditorViewModel editor, bool longLinearLoad = false)
    {
        var current = editor.Session.CurrentRevision.State; var segment = current.Document.Segments[0];
        Length Mm(double mm) => Length.FromMeters(mm / 1000);
        var rotation = SpanDraft.Core.Units.RotationalStiffness.FromNewtonMetersPerRadian(2.35e7);
        var translation = SpanDraft.Core.Units.TranslationalStiffness.FromNewtonsPerMeter(2.5e6);
        return new EditorDocument(Mm(12000), segment.Material, segment.Section, segment.BendingAxis,
            [new(Guid.NewGuid(), Mm(0), SupportType.Fixed, "A"),
                new(Guid.NewGuid(), Mm(6000), SupportType.Pinned, "B"),
                new(Guid.NewGuid(), Mm(9000), SupportType.Spring, "C", new(null, rotation, true)),
                new(Guid.NewGuid(), Mm(12000), SupportType.Spring, "Wegfeder rechts", new(translation)),
                new(Guid.NewGuid(), Mm(12000), SupportType.Spring, "E", new(null, rotation)),
                new(Guid.NewGuid(), Mm(3000), SupportType.Roller, "Kranbahnstütze links außen")],
            [new EditorPointForce(Guid.NewGuid(), Mm(2000), Force.FromNewtons(-4500), "F1")], current.Document.NamingState,
            [new(Guid.NewGuid(), Mm(10000), Mm(11500), ForcePerLength.FromNewtonsPerMeter(longLinearLoad ? -1234500 : -2000),
                ForcePerLength.FromNewtonsPerMeter(longLinearLoad ? -2345600 : -6000), "q1")]);
    }

    private static string? Plain(string? text) => text?.Replace(TechnicalLineBreaks.NoBreakSpace, ' ').Replace(TechnicalLineBreaks.WordJoiner.ToString(), "");

    private static OverviewTableRow[] TableRows(Control overview, string table)
    {
        var items = overview.FindControl<ItemsControl>(table)!;
        var header = ((StackPanel)items.Parent!).Children.OfType<OverviewTableRow>().Single();
        return items.GetVisualDescendants().OfType<OverviewTableRow>().Prepend(header).ToArray();
    }

    /// <summary>
    /// Every table sizes itself: each column is as wide as its widest data line (headings never count), the rest of the
    /// width goes evenly into the gaps (at least 12), and the outer columns sit flush with the table edges.
    /// </summary>
    private static void AssertTables(Control overview, bool wrapping = false)
    {
        var tables = new[] { ("SupportTable", 3), ("MomentReleaseTable", 3), ("ForceTable", 3), ("MomentTable", 3),
            ("DistributedLoadTable", 3), ("ReactionTable", 4), ("MomentReleaseResultTable", 4) };
        var scopes = new List<OverviewTableScope>();
        foreach (var (name, columns) in tables)
        {
            var items = overview.FindControl<ItemsControl>(name)!;
            var all = TableRows(overview, name);
            var header = all[0]; var rows = all.Skip(1).ToArray();
            Assert.True(header.IsHeader);
            Assert.Equal(items.ItemCount, rows.Length);
            if (items.ItemCount == 0) { Assert.False(((Control)items.Parent!.Parent!).IsVisible); continue; }
            var scope = OverviewTableRow.GetScope(header)!;
            Assert.All(rows, r => Assert.Same(scope, OverviewTableRow.GetScope(r)));
            scopes.Add(scope);
            Assert.Equal(columns, scope.Alignments.Length);
            double tableWidth = header.Bounds.Width;
            var lefts = new double[columns]; var widths = new double[columns]; var widest = new double[columns];
            for (int c = 0; c < columns; c++)
            {
                var cells = rows.SelectMany(r => r.Children.Where(x => x.IsVisible && OverviewTableRow.GetColumn(x) == c)).ToArray();
                Assert.NotEmpty(cells);
                lefts[c] = cells[0].Bounds.X; widths[c] = cells[0].Bounds.Width;
                foreach (var cell in cells)
                {
                    // One column box for the whole table.
                    Assert.Equal(lefts[c], cell.Bounds.X, 0.5);
                    Assert.Equal(widths[c], cell.Bounds.Width, 0.5);
                    foreach (var text in Texts(cell))
                    {
                        // A name longer than the cap is shortened to exactly the cap.
                        bool shortened = text.TextLayout.TextLines.Any(l => l.HasCollapsed);
                        Assert.True(!shortened || c == 0, $"{name}: '{Plain(text.Text)}' is shortened");
                        widest[c] = Math.Max(widest[c], shortened ? OverviewTableRow.NameCap : text.TextLayout.Width);
                        bool mayWrap = wrapping && c == scope.WrapColumn;
                        if (!mayWrap) Assert.True(text.TextLayout.TextLines.Count == 1, $"{name}: '{Plain(text.Text)}' wraps");
                        Assert.True(text.TextLayout.Width <= cell.Bounds.Width + 0.5, $"{name}: '{Plain(text.Text)}' is clipped");
                        // Numbers and units are joined: an ordinary space only follows "=" or "…".
                        var shown = text.Text ?? "";
                        if (scope.Alignments[c] == 'R' || text.Classes.Contains("supportDetail"))
                            Assert.All(Enumerable.Range(0, shown.Length).Where(i => shown[i] == ' '),
                                i => Assert.True(i > 0 && shown[i - 1] is '=' or '…', $"{name}: '{Plain(shown)}' may break at {i}"));
                        if (c > 0) Assert.Equal(scope.Alignments[c] == 'R' ? TextAlignment.Right : TextAlignment.Left,
                            text.TextAlignment == TextAlignment.Right ? TextAlignment.Right : TextAlignment.Left);
                    }
                }
                // The widest data line sets the width; a long name is shortened at the cap.
                Assert.Equal(widest[c], widths[c], 1.0);
            }
            Assert.True(widths[0] <= OverviewTableRow.NameCap + 0.5, $"{name}: name column width {widths[0]}");
            Assert.Equal(0, lefts[0], 0.5);
            Assert.Equal(tableWidth, lefts[^1] + widths[^1], 0.5);
            var gaps = Enumerable.Range(0, columns - 1).Select(c => lefts[c + 1] - lefts[c] - widths[c]).ToArray();
            Assert.All(gaps, g => { Assert.True(g >= OverviewTableScope.MinGap - 0.5, $"{name}: gap {g}"); Assert.Equal(gaps[0], g, 1.0); });
            // Headings follow the columns without widening them: a wider heading reaches into the gap.
            foreach (var cell in header.Children.OfType<TextBlock>())
            {
                int c = OverviewTableRow.GetColumn(cell);
                if (scope.Alignments[c] == 'R') Assert.Equal(lefts[c] + widths[c], cell.Bounds.Right, 0.5);
                else Assert.Equal(lefts[c], cell.Bounds.X, 0.5);
                Assert.Single(cell.TextLayout.TextLines);
            }
            foreach (var row in rows)
            {
                var action = Assert.Single(row.Children.OfType<Button>());
                Assert.Contains("overviewEntityAction", action.Classes);
                var caption = Assert.IsAssignableFrom<TextBlock>(action.Content);
                Assert.Equal(lefts[0], caption.TranslatePoint(default, row)!.Value.X, 0.5);
            }
            if (name is "ForceTable" or "MomentTable" or "DistributedLoadTable")
            {
                Assert.Equal(new[] { Strings.OverviewName, Strings.OverviewValue, "Position [mm]" }, header.Children.OfType<TextBlock>().Select(t => t.Text));
                foreach (var row in rows)
                {
                    var load = Assert.IsType<ProjectOverviewItem>(row.DataContext);
                    Assert.Equal(new[] { load.Name, Plain(load.Value), Plain(load.Position) },
                        row.Children.Select(c => Plain(c is Button button ? ((TextBlock)button.Content!).Text : ((TextBlock)c).Text)));
                }
            }
        }
        Assert.Equal(scopes.Count, scopes.Distinct().Count());
        // Section titles start where their tables start.
        foreach (var title in overview.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Classes.Contains("sectionTitle") && t.IsVisible))
        {
            Assert.NotEqual(TextAlignment.Center, title.TextAlignment);
            Assert.Single(title.TextLayout.TextLines);
        }

        static IEnumerable<TextBlock> Texts(Control cell) => cell switch
        {
            TextBlock text => [text],
            Button button => [(TextBlock)button.Content!],
            _ => cell.GetVisualDescendants().OfType<TextBlock>()
        };
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
        // Shared column sizes settle when the layout manager reports the finished pass.
        window.UpdateLayout();
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
                window.UpdateLayout();
            }
        }
    }

}
