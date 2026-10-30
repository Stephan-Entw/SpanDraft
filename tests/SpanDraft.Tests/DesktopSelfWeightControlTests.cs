using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SpanDraft.Analysis;
using SpanDraft.Desktop;
using SpanDraft.Desktop.Controls;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Reporting;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;
using SpanDraft.Desktop.Views;
using Xunit;
using static SpanDraft.Tests.ProjectTestSupport;

namespace SpanDraft.Tests;

[Collection("Schematic text")]
public sealed class DesktopSelfWeightControlTests
{
    [Fact]
    public void SharedSceneHitTestsReadOnlyWeightAndUsesNormalAnnotationGestures()
    {
        var document = DesktopSelfWeightTests.Document(true);
        int calls = 0;
        var editor = new EditorViewModel(document, () => { }, b => { calls++; return BeamAnalysis.Analyze(b); });
        var frame = new BeamLayoutState().Update(document, 1100, 400)!;
        var scene = BeamRenderState.Create(document, frame, DesktopLayoutFixture.Measure);
        var visual = Assert.Single(scene.DistributedLoads);
        var id = document.SelfWeight!.Id;
        Assert.Equal(id, visual.Id); Assert.Equal("g", visual.Name); Assert.False(visual.CanDragEndpoints);
        Assert.True(visual.IsSelfWeight);
        Assert.Equal(28, visual.BottomY - visual.TopY, 6);
        Assert.Equal(frame.Layout.Transform.PhysicalToScreen(0), visual.StartX);
        Assert.Equal(frame.Layout.Transform.PhysicalToScreen(3), visual.EndX);
        Assert.Equal(new[] { id }, scene.HitTestEntities(visual.CenterX, visual.TopY));
        foreach (double x in new[] { visual.StartX, visual.EndX })
        {
            Assert.Null(DistributedLoadSymbol.HitEndpoint(visual, x, visual.BottomY));
            Assert.Contains(id, scene.HitTestEntities(x, visual.BottomY));
        }
        Assert.False(editor.BeginDistributedLoadDrag(id, DistributedLoadEndpoint.Start));
        Assert.False(editor.BeginDistributedLoadDrag(id, DistributedLoadEndpoint.End));
        Assert.False(editor.EditDistributedLoad(id));
        var label = Assert.Single(scene.Annotations, a => a.Id == id);
        var click = new LabelDragGesture(label, label.Bounds.Center, null);
        Assert.True(click.OpenOnClick(editor)); Assert.True(editor.IsSelfWeightFlyoutVisible);
        editor.CancelEditorInteraction();
        var drag = new LabelDragGesture(label, label.Bounds.Center, null);
        drag.Apply(editor, label.Bounds.Center + new Vector(25, -15));
        Assert.Equal(new AnnotationOffset(25, -15), editor.RenderPresentation.AnnotationOffsets[id]);
        Assert.Empty(editor.EditorPresentation.AnnotationOffsets);
        drag.Complete(editor); Assert.False(drag.OpenOnClick(editor));
        var moved = BeamRenderState.Create(document, frame, DesktopLayoutFixture.Measure, presentation: editor.EditorPresentation);
        Assert.Equal(label.AutoBounds.Translate(new Vector(25, -15)), moved.Annotations.Single(a => a.Id == id).Bounds);
        Assert.Equal(1, calls);
        editor.ToggleSelfWeightVisibility();
        var hidden = BeamRenderState.Create(document, frame, DesktopLayoutFixture.Measure, presentation: editor.EditorPresentation);
        Assert.Empty(hidden.DistributedLoads); Assert.DoesNotContain(hidden.Annotations, a => a.Id == id);
        Assert.DoesNotContain(id, hidden.HitTestEntities(visual.CenterX, visual.TopY));
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData("de-DE", false)] [InlineData("en-US", true)]
    public void TableOpensSameNonEditableFlyoutWhenGraphicIsHidden(string culture, bool keyboard)
    {
        using var environment = new DesktopControlEnvironment(); using var scope = new UiCultureScope(culture);
        var editor = new EditorViewModel(DesktopSelfWeightTests.Document(true), () => { });
        var view = new EditorView { DataContext = editor };
        var window = new Window { Content = view };
        Layout(window);
        var surface = Assert.Single(view.GetVisualDescendants().OfType<BeamEditorSurface>());
        var canvas = surface.FindControl<BeamCanvas>("TechnicalCanvas")!;
        Assert.Same(Application.Current!.FindResource("SelfWeightStroke"), canvas.SelfWeightBrush);
        Assert.Equal(Color.Parse("#666666"), Assert.IsAssignableFrom<ISolidColorBrush>(canvas.SelfWeightBrush).Color);
        Assert.NotEqual(Assert.IsAssignableFrom<ISolidColorBrush>(canvas.BeamBrush).Color,
            Assert.IsAssignableFrom<ISolidColorBrush>(canvas.SelfWeightBrush).Color);
        var weight = Assert.Single(canvas.Scene!.DistributedLoads);
        var point = surface.FindControl<Control>("BeamPane")!.TranslatePoint(new(weight.CenterX, weight.TopY), window)!.Value;
        var pointer = new Pointer(1, PointerType.Mouse, true);
        surface.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, surface, pointer, window, point, 0,
            new PointerPointProperties(), KeyModifiers.None));
        Assert.Equal(editor.Document.SelfWeight!.Id, editor.HoveredDistributedLoadId);
        surface.RaiseEvent(new PointerPressedEventArgs(surface, pointer, window, point, 0,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None, 1));
        Dispatcher.UIThread.RunJobs();
        Assert.True(editor.IsSelfWeightFlyoutVisible);
        var popup = surface.FindControl<Popup>("SelfWeightPopup")!;
        Assert.True(popup.IsOpen);
        var flyout = surface.FindControl<SelfWeightFlyout>("SelfWeightEditor")!;
        flyout.ApplyTemplate(); flyout.Measure(new(320, 300)); flyout.Arrange(new Rect(0, 0, 320, 300));
        Assert.Empty(flyout.GetVisualDescendants().OfType<TextBox>());
        Assert.Equal(3, flyout.GetVisualDescendants().OfType<Button>().Count());
        var title = flyout.FindControl<TextBlock>("WeightTitle")!;
        var value = flyout.FindControl<TextBlock>("WeightValue")!;
        var areaTitle = flyout.FindControl<TextBlock>("AreaTitle")!;
        var areaValue = flyout.FindControl<TextBlock>("AreaValue")!;
        var densityTitle = flyout.FindControl<TextBlock>("DensityTitle")!;
        var densityValue = flyout.FindControl<TextBlock>("DensityValue")!;
        var delete = flyout.FindControl<Button>("DeleteAction")!;
        var close = flyout.FindControl<Button>("CloseAction")!;
        var visibility = flyout.FindControl<Button>("VisibilityAction")!;
        var icons = visibility.GetVisualDescendants().OfType<PathIcon>().ToArray();
        Assert.Equal(2, icons.Length); Assert.True(icons[0].IsVisible); Assert.False(icons[1].IsVisible);
        var titlePoint = title.TranslatePoint(new Point(), flyout)!.Value;
        var valuePoint = value.TranslatePoint(new Point(), flyout)!.Value;
        var areaTitlePoint = areaTitle.TranslatePoint(new Point(), flyout)!.Value;
        var areaValuePoint = areaValue.TranslatePoint(new Point(), flyout)!.Value;
        var densityTitlePoint = densityTitle.TranslatePoint(new Point(), flyout)!.Value;
        var densityValuePoint = densityValue.TranslatePoint(new Point(), flyout)!.Value;
        var deletePoint = delete.TranslatePoint(new Point(), flyout)!.Value;
        var closePoint = close.TranslatePoint(new Point(), flyout)!.Value;
        var visibilityPoint = visibility.TranslatePoint(new Point(), flyout)!.Value;
        Assert.Equal(titlePoint.Y, valuePoint.Y, 1);
        Assert.True(titlePoint.X + title.Bounds.Width <= valuePoint.X);
        Assert.Equal(titlePoint.X, areaTitlePoint.X, 1); Assert.Equal(titlePoint.X, densityTitlePoint.X, 1);
        Assert.Equal(valuePoint.X, areaValuePoint.X, 1); Assert.Equal(valuePoint.X, densityValuePoint.X, 1);
        Assert.True(titlePoint.Y < areaTitlePoint.Y && areaTitlePoint.Y < densityTitlePoint.Y);
        Assert.Equal(title.FontWeight, areaTitle.FontWeight); Assert.Equal(title.FontWeight, densityTitle.FontWeight);
        Assert.Equal(Strings.SectionDefinitionArea, areaTitle.Text); Assert.Equal(Strings.MaterialDensity, densityTitle.Text);
        Assert.Equal(editor.SelfWeightLabel, TechnicalTextPresenter.GetContent(value)!.PlainText);
        Assert.Equal(editor.SelfWeightArea.PlainText, TechnicalTextPresenter.GetContent(areaValue)!.PlainText);
        Assert.Equal(editor.SelfWeightDensity.PlainText, TechnicalTextPresenter.GetContent(densityValue)!.PlainText);
        Assert.True(deletePoint.Y - densityValuePoint.Y - densityValue.Bounds.Height
            > areaTitlePoint.Y - titlePoint.Y - title.Bounds.Height);
        Assert.All(new[] { title, value, areaTitle, areaValue, densityTitle, densityValue },
            text => Assert.True(text.TextLayout.TextLines[0].Width <= text.Bounds.Width));
        Assert.True(deletePoint.X + delete.Bounds.Width <= closePoint.X);
        Assert.True(closePoint.X + close.Bounds.Width <= visibilityPoint.X);
        Assert.True(visibilityPoint.X + visibility.Bounds.Width <= flyout.Bounds.Width);
        Assert.True(value.DesiredSize.Width <= value.Bounds.Width);
        var revision = editor.Session.CurrentRevision;
        var analysis = editor.Presentation;
        flyout.FindControl<Button>("VisibilityAction")!.Command!.Execute(null); Dispatcher.UIThread.RunJobs();
        Assert.True(popup.IsOpen); Assert.False(editor.IsSelfWeightVisible);
        Assert.False(icons[0].IsVisible); Assert.True(icons[1].IsVisible);
        Assert.Empty(canvas.Scene!.DistributedLoads); Assert.Single(editor.Overview.DistributedLoads);
        Assert.Same(analysis, editor.Presentation);
        var hiddenRevision = editor.Session.CurrentRevision;
        flyout.FindControl<Button>("CloseAction")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.False(editor.IsSelfWeightFlyoutVisible); Assert.Same(hiddenRevision, editor.Session.CurrentRevision);
        Assert.Same(analysis, editor.Presentation); Layout(window);
        var overview = Assert.Single(view.GetVisualDescendants().OfType<ProjectOverviewView>());
        var button = overview.FindControl<ItemsControl>("DistributedLoadTable")!.GetVisualDescendants().OfType<Button>()
            .Single(b => b.DataContext is ProjectOverviewItem { Name: "g" });
        if (keyboard) button.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        else button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.True(editor.IsSelfWeightFlyoutVisible); Assert.True(popup.IsOpen);
        Assert.True(double.IsFinite(Canvas.GetLeft(surface.FindControl<Border>("SelfWeightAnchor")!)));
        Assert.Equal(Strings.ShowSelfWeight, flyout.FindControl<TextBlock>("VisibilityText")!.Text);
        flyout.FindControl<Button>("VisibilityAction")!.Command!.Execute(null); Dispatcher.UIThread.RunJobs();
        Assert.True(popup.IsOpen); Assert.Single(canvas.Scene!.DistributedLoads);
        Assert.Same(analysis, editor.Presentation);
        flyout.FindControl<Button>("DeleteAction")!.Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.False(popup.IsOpen); Assert.False(editor.IsSelfWeightEnabled);
        Assert.False(view.FindControl<ToggleButton>("SelfWeightButton")!.IsChecked);
        Assert.Empty(editor.Overview.DistributedLoads);
        Assert.NotSame(revision, editor.Session.CurrentRevision);
        window.Close();
    }

    [Fact]
    public void SharedSymbolSelectionCanOpenSelfWeightAndEscapeDismissesIt()
    {
        using var environment = new DesktopControlEnvironment();
        var document = DesktopSelfWeightTests.Document(true).WithDistributedLoads([
            new(Guid.NewGuid(), M(0), M(3), SpanDraft.Core.Units.ForcePerLength.FromNewtonsPerMeter(-500), "q1")]);
        var editor = new EditorViewModel(document, () => { });
        var view = new EditorView { DataContext = editor }; var window = new Window { Content = view };
        Layout(window);
        var surface = Assert.Single(view.GetVisualDescendants().OfType<BeamEditorSurface>());
        var scene = surface.FindControl<BeamCanvas>("TechnicalCanvas")!.Scene!;
        var visual = scene.DistributedLoads[0];
        Assert.Equal(2, scene.HitTestEntities(visual.CenterX, visual.TopY).Count);
        var point = surface.FindControl<Control>("BeamPane")!.TranslatePoint(new(visual.CenterX, visual.TopY), window)!.Value;
        surface.RaiseEvent(new PointerPressedEventArgs(surface, new Pointer(1, PointerType.Mouse, true), window, point, 0,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None, 1));
        Assert.True(surface.FindControl<Popup>("SelectionPopup")!.IsOpen);
        var button = surface.FindControl<StackPanel>("SelectionEntries")!.Children.OfType<Button>()
            .Single(b => b.Content is string text && text.StartsWith("g = ", StringComparison.Ordinal));
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
        Assert.True(editor.IsSelfWeightFlyoutVisible); Assert.Null(editor.DistributedLoadDraft);
        var flyout = surface.FindControl<SelfWeightFlyout>("SelfWeightEditor")!;
        flyout.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
        Assert.False(editor.IsSelfWeightFlyoutVisible);
        Assert.True(editor.IsSelfWeightEnabled); Assert.Empty(editor.Session.UndoHistory);
        window.Close();
    }

    [Fact]
    public void ToolbarRejectsMissingDensityAndShowsTheExistingFeedbackOverlay()
    {
        using var environment = new DesktopControlEnvironment(); using var culture = new UiCultureScope("de-DE");
        var editor = new EditorViewModel(DesktopSelfWeightTests.Document() with { Material = DesktopSelfWeightTests.Steel(null) }, () => { });
        var view = new EditorView { DataContext = editor }; var window = new Window { Content = view };
        Layout(window);
        var button = view.FindControl<ToggleButton>("SelfWeightButton")!;
        typeof(Button).GetMethod("OnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(button, null);
        Dispatcher.UIThread.RunJobs();
        Assert.False(button.IsChecked); Assert.False(editor.IsSelfWeightEnabled);
        var feedback = Assert.Single(view.GetVisualDescendants().OfType<BeamEditorSurface>()).FindControl<TextBlock>("FeedbackOverlay")!;
        Assert.True(feedback.IsVisible); Assert.Equal(Strings.SelfWeightMissingDensity, feedback.Text);
        Assert.Empty(editor.Session.UndoHistory);
        window.Close();
    }

    [Theory]
    [InlineData("de-DE", false)] [InlineData("en-US", true)]
    public void ToolbarPlacesToggleBeforeSupportSeparatorAndRemainsContained(string culture, bool dark)
    {
        using var environment = new DesktopControlEnvironment(); using var scope = new UiCultureScope(culture);
        Application.Current!.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        var main = new MainWindowViewModel(files: new Files());
        main.NewCommand.Execute(null); CompleteSetup(main); main.Setup.ApplyCommand.Execute(null);
        main.Session!.Commit(new(DesktopSelfWeightTests.Document(), new()));
        var window = new MainWindow { DataContext = main };
        Layout(window, 1100, 650);
        var view = Assert.Single(window.GetVisualDescendants().OfType<EditorView>());
        var toolbar = view.FindControl<Border>("EditorToolbar")!;
        var grid = Assert.IsType<Grid>(toolbar.Child);
        var tools = Assert.IsType<StackPanel>(grid.Children[0]);
        var buttons = tools.Children.OfType<ToggleButton>().ToArray();
        var weight = view.FindControl<ToggleButton>("SelfWeightButton")!;
        Assert.Same(main.Editor!.ToggleSelfWeightCommand, weight.Command);
        Assert.Equal(3, Array.IndexOf(buttons, weight));
        Assert.Same(main.Editor.DistributedLoadToolCommand, buttons[2].Command);
        Assert.IsType<Avalonia.Controls.Shapes.Rectangle>(tools.Children[tools.Children.IndexOf(weight) + 1]);
        var image = Assert.Single(weight.GetVisualDescendants().OfType<Image>());
        Assert.Same(Application.Current.FindResource("ToolbarIcon.SelfWeight"), image.Source);
        foreach (int width in new[] { 1100, 1250, 1600 })
        {
            Layout(window, width, 800);
            var right = grid.Children[1];
            var toolsBounds = new Rect(tools.TranslatePoint(default, grid)!.Value, tools.Bounds.Size);
            var rightBounds = new Rect(right.TranslatePoint(default, grid)!.Value, right.Bounds.Size);
            Assert.True(new Rect(grid.Bounds.Size).Contains(toolsBounds));
            Assert.True(new Rect(grid.Bounds.Size).Contains(rightBounds));
            Assert.True(toolsBounds.Right + grid.ColumnSpacing <= rightBounds.Left);
        }
        main.Editor.ToggleSelfWeight(); Dispatcher.UIThread.RunJobs(); Assert.True(weight.IsChecked);
        main.Editor.Undo(); Dispatcher.UIThread.RunJobs(); Assert.False(weight.IsChecked);
        main.Editor.Redo(); Dispatcher.UIThread.RunJobs(); Assert.True(weight.IsChecked);
        window.Close();
    }

    [Theory]
    [InlineData("de-DE", true)] [InlineData("de-DE", false)]
    [InlineData("en-US", true)] [InlineData("en-US", false)]
    public void ReportUsesOverviewTableAndSavedSceneVisibilityAndOffset(string culture, bool visible)
    {
        using var environment = new DesktopControlEnvironment(); using var scope = new UiCultureScope(culture);
        var document = DesktopSelfWeightTests.Document(true).WithDistributedLoads([
            new(Guid.NewGuid(), M(.5), M(2), SpanDraft.Core.Units.ForcePerLength.FromNewtonsPerMeter(-500), "q1")]);
        var state = new ProjectState(document, new([new(document.SelfWeight!.Id, new(18, -12))], visible));
        var editor = EditorViewModel.ForSession(ProjectSession.Create(state), () => { }, resultPresentation:
            new ResultPresentationOptions(significantDigits: 5, notation: EngineeringNotation.Off));
        editor.PreviewAnnotationOffset(document.SelfWeight.Id, new(99, 88));
        using var report = ProjectReport.Capture(editor, Now);
        var table = Assert.Single(report.Data.Tables, t => t.Title == Strings.DistributedLoads);
        Assert.Equal(new[] { "g", "q1" }, table.Rows.Select(r => r[0]));
        Assert.Equal(editor.Overview.DistributedLoads.Select(l => l.Value), table.Rows.Select(r => r[1]));
        var visuals = new ReportVisuals(state, editor.Presentation, report.Data);
        var scene = visuals.Beam.Scene!;
        Assert.Equal(visible, scene.DistributedLoads.Any(l => l.Id == document.SelfWeight.Id));
        var normal = Assert.Single(scene.DistributedLoads, l => l.Id == document.DistributedLoads[0].Id);
        Assert.False(normal.IsSelfWeight);
        Assert.Equal(.85, normal.SymbolScale);
        Assert.Same(Application.Current!.FindResource("SelfWeightStroke"), visuals.Beam.SelfWeightBrush);
        Assert.Equal(Color.Parse("#666666"), Assert.IsAssignableFrom<ISolidColorBrush>(visuals.Beam.SelfWeightBrush).Color);
        var drawing = new DrawingGroup();
        using (var context = drawing.Open()) visuals.Beam.Render(context);
        var geometry = Geometry(drawing).ToArray();
        var fill = Assert.Single(geometry, g => g.Brush is not null && g.Pen is null);
        Assert.Equal(normal.FillBounds, fill.Geometry!.Bounds);
        Assert.Equal(.06, Assert.IsType<DrawingGroup>(drawing.Children[0]).Opacity);
        var glyphs = DistributedLoadSymbol.InnerGlyphs(scene.DistributedLoads);
        var shafts = geometry.Where(g => g.Brush is null
            && Math.Abs(g.Geometry!.Bounds.Height - 28 * .85) < 1e-4).ToArray();
        var normalShafts = shafts.Where(g => g.Pen!.Brush == visuals.Beam.BeamBrush).ToArray();
        Assert.Equal(glyphs.Count(g => g.Entities.Any(e => !e.IsSelfWeight)) + 2, normalShafts.Length);
        var weightShafts = shafts.Where(g => g.Pen!.Brush == visuals.Beam.SelfWeightBrush).ToArray();
        Assert.Equal(visible ? glyphs.Count(g => g.Entities.All(e => e.IsSelfWeight)) + 2 : 0, weightShafts.Length);
        Assert.Equal(shafts.Length, normalShafts.Length + weightShafts.Length);
        Assert.All(shafts, g =>
        {
            Assert.Equal(1.5 * .85, g.Pen!.Thickness, 6);
            Assert.Equal(normal.BottomY, g.Geometry!.Bounds.Bottom, 4);
        });
        var heads = geometry.Where(g => g.Brush is not null && g.Pen is not null).ToArray();
        Assert.NotEmpty(heads);
        Assert.All(heads, g =>
        {
            Assert.Equal(7 * .85, g.Geometry!.Bounds.Width, 4);
            Assert.Equal(7 * .85, g.Geometry.Bounds.Height, 4);
            Assert.True(g.Brush == visuals.Beam.BeamBrush || g.Brush == visuals.Beam.SelfWeightBrush);
            Assert.Same(g.Pen!.Brush, g.Brush);
        });
        if (visible)
        {
            var label = Assert.Single(scene.Annotations, a => a.Id == document.SelfWeight.Id);
            Assert.Equal(label.AutoBounds.Translate(new Vector(18, -12)), label.Bounds);
            Assert.Equal(editor.SelfWeightLabel, label.Text);
            Assert.False(scene.DistributedLoads[0].CanDragEndpoints);
            Assert.True(scene.DistributedLoads[0].IsSelfWeight);
            Assert.Equal(.85, scene.DistributedLoads[0].SymbolScale);
            Assert.Equal(normal.TopY, scene.DistributedLoads[0].TopY);
            Assert.Equal(normal.BottomY, scene.DistributedLoads[0].BottomY);
            Assert.Equal(new[] { scene.DistributedLoads[0].StartX, scene.DistributedLoads[0].EndX },
                weightShafts.TakeLast(2).Select(g => g.Geometry!.Bounds.X));
        }
        else Assert.DoesNotContain(scene.Annotations, a => a.Id == document.SelfWeight.Id);
        using var page = report.RenderPage(0);
        Assert.Contains(report.TextRuns, r => r.Text == "g");
    }

    private static IEnumerable<GeometryDrawing> Geometry(Drawing drawing) => drawing is DrawingGroup group
        ? group.Children.SelectMany(Geometry) : drawing is GeometryDrawing geometry ? [geometry] : [];

    private static void Layout(Window window, int width = 1250, int height = 800)
    {
        window.ApplyTemplate(); window.Measure(new(width, height)); window.Arrange(new Rect(0, 0, width, height));
        var content = (Control)window.Content!;
        foreach (var child in content.GetVisualDescendants().OfType<Control>())
        { child.InvalidateMeasure(); child.InvalidateArrange(); }
        content.InvalidateMeasure(); content.Measure(new(width, height));
        content.InvalidateArrange(); content.Arrange(new Rect(0, 0, width, height));
        Dispatcher.UIThread.RunJobs();
    }
}
