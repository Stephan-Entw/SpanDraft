using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using SpanDraft.Analysis;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Controls;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;
using SpanDraft.Desktop.Views;
using Xunit;

namespace SpanDraft.Tests;

[Collection("Avalonia")]
public sealed class ResultDiagramControlTests : DesktopResultDiagramControlTestSupport
{
    [Theory]
    [InlineData("de-DE", 0)]
    [InlineData("en-US", -600)]
    public void ResultsOwnOneStatusAndRestoreDiagramLayoutAfterFailure(string culture, double annotationOffset)
    {
        using var environment = new DesktopControlEnvironment();
        using var cultures = new UiCultureScope(culture);
        var document = Document();
        var editor = new EditorViewModel(document.WithSupports([]), () => { });
        editor.SetAnnotationOffset(document.Loads[0].Id, new(0, annotationOffset));
        var view = new EditorView { DataContext = editor };
        var window = new Window { Content = view };
        var status = view.FindControl<Grid>("ResultStatus")!;
        var statusText = view.FindControl<TextBlock>("AnalysisStatus")!;
        var statusTitle = view.FindControl<TextBlock>("AnalysisStatusTitle")!;
        var stack = view.FindControl<StackPanel>("ResultDiagrams")!;
        var upper = view.FindControl<ScrollViewer>("EditorScroll")!;
        var lower = view.FindControl<ScrollViewer>("DiagramScroll")!;
        var surface = view.FindControl<BeamEditorSurface>("BeamSurface")!;
        Arrange(window, 1250, 618);
        var overview = view.GetVisualDescendants().OfType<ProjectOverviewView>().Single();
        AssertFailure();
        Assert.True(editor.Session.Commit(new(document, editor.EditorPresentation)));
        Arrange(window, 1250, 618);
        AssertSuccess();
        double diagramExtent = lower.Extent.Height;
        double diagramWidth = Diagrams(view)[0].Bounds.Width;
        var diagramPositions = Diagrams(view).Select(d => d.Bounds).ToArray();
        var projections = Diagrams(view).Select(d => d.Projection).ToArray();
        lower.Offset = new(0, 60);
        Assert.True(lower.Offset.Y > 0);

        Assert.True(editor.Session.Commit(new(document.WithSupports([]), editor.EditorPresentation)));
        Arrange(window, 1250, 618);
        AssertFailure();
        Assert.True(lower.Extent.Height < diagramExtent);
        Assert.Equal(0, lower.Offset.Y);
        Assert.Equal(annotationOffset != 0, upper.ScrollBarMaximum.Y > 0);
        Assert.Equal(annotationOffset != 0 ? ScrollBarVisibility.Visible : ScrollBarVisibility.Hidden,
            lower.VerticalScrollBarVisibility);

        Assert.True(editor.Undo());
        Arrange(window, 1250, 618);
        AssertSuccess();
        Assert.Equal(diagramExtent, lower.Extent.Height);
        Assert.Equal(diagramWidth, Diagrams(view)[0].Bounds.Width);
        Assert.Equal(diagramPositions, Diagrams(view).Select(d => d.Bounds));
        for (int i = 0; i < projections.Length; i++)
            Assert.NotSame(projections[i], Diagrams(view)[i].Projection);

        // The inert test window is never shown; check visibility through the workspace ancestors.
        bool VisibleInWorkspace(Control control) => control.IsVisible && control.GetVisualAncestors()
            .OfType<Control>().TakeWhile(c => c is not Window).All(c => c.IsVisible);

        void AssertFailure()
        {
            Assert.False(editor.Presentation.IsSuccess);
            Assert.True(VisibleInWorkspace(status));
            Assert.Equal(editor.Presentation.StatusTitle, statusTitle.Text);
            Assert.Equal(editor.Presentation.StatusText, statusText.Text);
            Assert.Empty(status.GetVisualDescendants().OfType<PathIcon>());
            double center = status.TranslatePoint(default, lower)!.Value.X + status.Bounds.Width / 2;
            Assert.InRange(Math.Abs(center - lower.Viewport.Width / 2), 0, .5);
            Assert.True(status.Bounds.Top > 0);
            Assert.True(statusTitle.Bounds.Bottom < statusText.Bounds.Top);
            foreach (var text in new[] { statusTitle, statusText })
            {
                Assert.Equal(Avalonia.Media.TextAlignment.Center, text.TextAlignment);
                Assert.All(text.TextLayout.TextLines, line => Assert.False(line.HasCollapsed));
                Assert.True(text.Bounds.Height >= text.TextLayout.Height);
                Assert.True(new Rect(status.Bounds.Size).Contains(new Rect(text.Bounds.Position, text.Bounds.Size)));
            }
            Assert.Same(statusText, Assert.Single(view.GetVisualDescendants().OfType<TextBlock>(),
                t => VisibleInWorkspace(t) && t.Text == editor.Presentation.StatusText));
            Assert.False(stack.IsVisible);
            Assert.Equal(default, stack.DesiredSize);
            Assert.All(Diagrams(view), d =>
            {
                Assert.False(VisibleInWorkspace(d));
                Assert.Null(d.Projection);
                Assert.Empty(d.MarkerLabels);
            });
            Assert.Null(overview.FindControl<TextBlock>("AnalysisStatus"));
            Assert.False(overview.FindControl<StackPanel>("IndicatorsSection")!.IsVisible);
            Assert.False(VisibleInWorkspace(overview.FindControl<StackPanel>("IndicatorsPanel")!));
            Assert.Equal(upper.Viewport.Width, lower.Viewport.Width);
            Assert.True(lower.Extent.Width <= lower.Viewport.Width);
        }

        void AssertSuccess()
        {
            Assert.True(editor.Presentation.IsSuccess);
            Assert.False(status.IsVisible);
            Assert.Equal(default, status.DesiredSize);
            Assert.True(stack.IsVisible);
            Assert.Equal(3, Diagrams(view).Length);
            Assert.All(Diagrams(view), d =>
            {
                Assert.True(VisibleInWorkspace(d));
                Assert.Equal(180, d.Bounds.Height);
                Assert.Same(editor.Presentation, d.Presentation);
                Assert.Same(surface.StationLayout, d.Projection!.StationLayout);
                Assert.Equal(surface.Bounds.Width, d.Bounds.Width);
                Assert.Equal(surface.TranslatePoint(default, view)!.Value.X, d.TranslatePoint(default, view)!.Value.X);
            });
            Assert.Equal(0, Diagrams(view)[0].Bounds.Top);
            Assert.True(overview.FindControl<StackPanel>("IndicatorsSection")!.IsVisible);
            Assert.True(VisibleInWorkspace(overview.FindControl<StackPanel>("IndicatorsPanel")!));
            Assert.Equal(upper.Viewport.Width, lower.Viewport.Width);
            Assert.Equal(ScrollBarVisibility.Disabled, lower.HorizontalScrollBarVisibility);
            Assert.True(lower.Extent.Width <= lower.Viewport.Width);
        }
    }

    [Fact]
    public void LongStatusHintWrapsWithoutClippingInTheCompactResultsArea()
    {
        using var environment = new DesktopControlEnvironment();
        using var culture = new UiCultureScope("de-DE");
        var view = new EditorView { DataContext = new EditorViewModel(Document().WithSupports([]), () => { }) };
        var window = new Window { Content = view };
        Arrange(window, 1100, 618);
        var state = new AnalysisPresentationState(AnalysisPresentationKind.IllConditionedSystem, null);
        var status = view.FindControl<Grid>("ResultStatus")!;
        var title = view.FindControl<TextBlock>("AnalysisStatusTitle")!;
        var hint = view.FindControl<TextBlock>("AnalysisStatus")!;
        // Exercise the longest explanation without requiring a numerical failure fixture in a layout test.
        title.Text = state.StatusTitle;
        hint.Text = state.StatusText;
        Arrange(window, 1100, 618);

        Assert.True(hint.TextLayout.TextLines.Count > 1);
        Assert.All(hint.TextLayout.TextLines, line => Assert.False(line.HasCollapsed));
        Assert.True(hint.Bounds.Height >= hint.TextLayout.Height);
        Assert.True(new Rect(status.Bounds.Size).Contains(new Rect(hint.Bounds.Position, hint.Bounds.Size)));
        Assert.True(title.Bounds.Bottom < hint.Bounds.Top);
        var scroll = view.FindControl<ScrollViewer>("DiagramScroll")!;
        Assert.True(scroll.Extent.Height <= scroll.Viewport.Height);
        Assert.True(scroll.Extent.Width <= scroll.Viewport.Width);
    }

    [Fact]
    public void HeightChangesReclaimGuttersAndRestoreIndependentScrollOffsets()
    {
        using var environment = new DesktopControlEnvironment();
        var editor = new EditorViewModel(Document(), () => { });
        var view = new EditorView { DataContext = editor };
        var window = new Window { Content = view };
        var upper = view.FindControl<ScrollViewer>("EditorScroll")!;
        var lower = view.FindControl<ScrollViewer>("DiagramScroll")!;
        var surface = view.FindControl<BeamEditorSurface>("BeamSurface")!;
        Arrange(window, 1250, 1300);
        double fullWidth = upper.Viewport.Width;
        Assert.Equal(upper.Bounds.Width, fullWidth);
        Assert.Equal(fullWidth, lower.Viewport.Width);

        for (int cycle = 0; cycle < 2; cycle++)
        {
            Arrange(window, 1250, 618);
            double narrowWidth = upper.Viewport.Width;
            Assert.True(narrowWidth < fullWidth);
            Assert.Equal(narrowWidth, lower.Viewport.Width);
            lower.Offset = new(0, 60);
            var diagramOffset = lower.Offset;
            editor.SetAnnotationOffset(editor.Document.Loads[0].Id, new(0, -600));
            Arrange(window, 1250, 618);
            upper.Offset = new(0, upper.ScrollBarMaximum.Y);
            Assert.True(upper.Offset.Y > 0);
            Assert.Equal(diagramOffset, lower.Offset);

            editor.SetAnnotationOffset(editor.Document.Loads[0].Id, null);
            Arrange(window, 1250, 618);
            Assert.Equal(0, upper.Offset.Y);
            Assert.Equal(diagramOffset, lower.Offset);
            Assert.Equal(narrowWidth, upper.Viewport.Width);
            Arrange(window, 1250, 1300);
            Assert.Equal(fullWidth, upper.Viewport.Width);
            Assert.Equal(fullWidth, lower.Viewport.Width);
            Assert.Equal(0, lower.Offset.Y);
            Assert.All(Diagrams(view), d => Assert.Same(surface.StationLayout, d.Projection!.StationLayout));
        }
    }

    [Fact]
    public void ADividerSeparatesBeamAndResultsAndTheLastDiagramHasAsMuchRoomBelowAsBetweenDiagrams()
    {
        using var environment = new DesktopControlEnvironment();
        var view = new EditorView { DataContext = new EditorViewModel(Document(), () => { }) };
        var window = new Window { Content = view };
        Arrange(window, 1250, 1600);
        var upper = view.FindControl<ScrollViewer>("EditorScroll")!;
        var lower = view.FindControl<ScrollViewer>("DiagramScroll")!;
        var divider = view.FindControl<Border>("ResultsDivider")!;
        var diagrams = Diagrams(view).ToList();

        // The hairline lies between the two viewports, spans their width and does not overlap either.
        Assert.Equal(upper.Bounds.Bottom, divider.Bounds.Top, 0.5);
        Assert.Equal(divider.Bounds.Bottom, lower.Bounds.Top, 0.5);
        Assert.Equal(upper.Bounds.Width, divider.Bounds.Width, 0.5);

        double between = diagrams[1].TranslatePoint(default, view)!.Value.Y - diagrams[0].TranslatePoint(default, view)!.Value.Y - diagrams[0].Bounds.Height;
        var stack = view.FindControl<StackPanel>("ResultDiagrams")!;
        double below = stack.Margin.Bottom;
        Assert.True(between > 0);
        Assert.Equal(between, below, 0.5);
    }

    [Theory]
    [InlineData(1100, 618)]
    [InlineData(1250, 768)]
    [InlineData(1599, 1300)]
    [InlineData(1600, 1300)]
    [InlineData(1601, 1300)]
    [InlineData(2400, 1300)]
    [InlineData(2400, 618)]
    public void TechnicalContentFillsAvailableWidthUpToItsSharedCenteredLimit(double width, double height)
    {
        using var environment = new DesktopControlEnvironment();
        var editor = new EditorViewModel(Document(), () => { });
        var view = new EditorView { DataContext = editor };
        var window = new Window { Content = view };
        Arrange(window, width, height);
        var upper = view.FindControl<ScrollViewer>("EditorScroll")!;
        var lower = view.FindControl<ScrollViewer>("DiagramScroll")!;
        var surface = view.FindControl<BeamEditorSurface>("BeamSurface")!;
        var overview = Assert.Single(view.GetVisualDescendants().OfType<ProjectOverviewView>());
        Assert.Equal(320, overview.Bounds.Width);
        Assert.Equal(width - overview.Bounds.Width, upper.Bounds.Width);
        Assert.Equal(upper.Bounds.Right, overview.Bounds.Left);
        Assert.Equal(upper.Viewport.Width, lower.Viewport.Width);
        Assert.Equal(Math.Min(1280, upper.Viewport.Width), surface.Bounds.Width);
        double surfaceLeft = surface.TranslatePoint(default, view)!.Value.X;
        double centeredLeft = upper.TranslatePoint(default, view)!.Value.X + (upper.Viewport.Width - surface.Bounds.Width) / 2;
        Assert.InRange(Math.Abs(surfaceLeft - centeredLeft), 0, .5);
        foreach (var diagram in Diagrams(view))
        {
            Assert.Equal(surface.Bounds.Width, diagram.Bounds.Width);
            Assert.Equal(surfaceLeft, diagram.TranslatePoint(default, view)!.Value.X);
            Assert.Same(surface.StationLayout, diagram.Projection!.StationLayout);
            Assert.Equal(180, diagram.Bounds.Height);
        }
        foreach (var scroll in new[] { upper, lower })
        {
            Assert.Equal(ScrollBarVisibility.Disabled, scroll.HorizontalScrollBarVisibility);
            Assert.Equal(0, scroll.Offset.X);
            Assert.True(scroll.Extent.Width <= scroll.Viewport.Width);
        }
    }

    [Theory]
    [InlineData(1250, 1300, 0, false, false)]
    [InlineData(1250, 618, 0, false, true)]
    [InlineData(1250, 2000, -1400, true, false)]
    [InlineData(1250, 618, -600, true, true)]
    [InlineData(2400, 1300, 0, false, false)]
    [InlineData(2400, 618, 0, false, true)]
    [InlineData(2400, 2000, -1400, true, false)]
    [InlineData(2400, 618, -600, true, true)]
    public void GuttersExistOnlyForActualOverflowAndOnlyScrollableBarsAcceptInput(
        double width, double height, double annotationOffset, bool editorOverflows, bool diagramsOverflow)
    {
        using var environment = new DesktopControlEnvironment();
        var editor = new EditorViewModel(Document(), () => { });
        var view = new EditorView { DataContext = editor };
        var window = new Window { Content = view };
        editor.SetAnnotationOffset(editor.Document.Loads[0].Id, new(0, annotationOffset));
        Arrange(window, width, height);
        var upper = view.FindControl<ScrollViewer>("EditorScroll")!;
        var lower = view.FindControl<ScrollViewer>("DiagramScroll")!;
        bool needsGutter = editorOverflows || diagramsOverflow;
        Assert.Equal(upper.Viewport.Width, lower.Viewport.Width);
        foreach (var (scroll, overflows) in new[] { (upper, editorOverflows), (lower, diagramsOverflow) })
        {
            var bar = VerticalBar(scroll);
            Assert.Equal(overflows, scroll.Extent.Height > scroll.Viewport.Height);
            Assert.Equal(needsGutter ? ScrollBarVisibility.Visible : ScrollBarVisibility.Hidden, scroll.VerticalScrollBarVisibility);
            Assert.Equal(needsGutter, bar.IsVisible);
            Assert.Equal(overflows ? 1 : 0, bar.Opacity);
            Assert.Equal(overflows, bar.IsHitTestVisible);
            Assert.Equal(needsGutter ? bar.Bounds.Width : 0, scroll.Bounds.Width - scroll.Viewport.Width);
            scroll.Offset = new(0, scroll.ScrollBarMaximum.Y);
            Assert.Equal(overflows, scroll.Offset.Y > 0);
        }
        var surface = view.FindControl<BeamEditorSurface>("BeamSurface")!;
        Assert.True(upper.Bounds.Height <= Math.Ceiling(view.FindControl<Grid>("EditorContent")!.Bounds.Height * .6));
        Assert.All(Diagrams(view), d =>
        {
            Assert.Equal(surface.Bounds.Width, d.Bounds.Width);
            Assert.Equal(surface.TranslatePoint(default, view)!.Value.X, d.TranslatePoint(default, view)!.Value.X);
            Assert.Same(surface.StationLayout, d.Projection!.StationLayout);
        });
    }

    [Fact]
    public void CloselySpacedEditorStationsAndResizingKeepAxisAndAllDiagramsAligned()
    {
        using var environment = new DesktopControlEnvironment();
        int calls = 0;
        var document = Document().WithLoads([
            new EditorPointForce(Guid.NewGuid(), Length.FromMeters(.01), Force.FromNewtons(-1000), "F1"),
            new EditorPointMoment(Guid.NewGuid(), Length.FromMeters(.011), Moment.FromNewtonMeters(100), "M1")]);
        var editor = new EditorViewModel(document, () => { }, b => { calls++; return BeamAnalysis.Analyze(b); });
        var view = new EditorView { DataContext = editor };
        var window = new Window { Content = view };
        foreach (double width in new[] { 1100d, 1600d })
        {
            Arrange(window, width);
            var surface = view.FindControl<BeamEditorSurface>("BeamSurface")!;
            var scene = surface.FindControl<BeamCanvas>("TechnicalCanvas")!.Scene!;
            Assert.True(surface.StationLayout!.IsDistorted);
            Assert.Same(scene.Frame.Layout, surface.FindControl<CoordinateAxisPane>("CoordinateAxis")!.StationLayout);
            foreach (var diagram in Diagrams(view))
            {
                Assert.Same(scene.Frame.Layout, diagram.Projection!.StationLayout);
                foreach (var station in scene.Frame.Layout.Stations)
                    Assert.Contains(diagram.Projection.Sections.SelectMany(s => s),
                        p => p.Position.Meters == station.PhysicalX && p.Screen.X == station.ScreenX);
            }
            Snapshot(view, $"distorted-{(int)width}");
        }
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(618, -600)]
    [InlineData(618, 600)]
    [InlineData(260, -600)]
    public void HighAnnotationsAndSmallViewportsKeepIndependentReachableScrollRegions(double height, double offset)
    {
        using var environment = new DesktopControlEnvironment();
        int calls = 0;
        var editor = new EditorViewModel(Document(), () => { }, b => { calls++; return BeamAnalysis.Analyze(b); });
        var view = new EditorView { DataContext = editor };
        var window = new Window { Content = view }; Arrange(window);
        editor.SetAnnotationOffset(editor.Document.Loads[0].Id, new(0, offset)); Arrange(window, 1100, height);
        var upper = view.FindControl<ScrollViewer>("EditorScroll")!;
        var lower = view.FindControl<ScrollViewer>("DiagramScroll")!;
        var sidebar = view.GetVisualDescendants().OfType<ProjectOverviewView>().Single().FindControl<ScrollViewer>("OverviewScroll")!;
        var surface = view.FindControl<BeamEditorSurface>("BeamSurface")!;
        var scene = surface.FindControl<BeamCanvas>("TechnicalCanvas")!.Scene!;
        Assert.True(upper.Extent.Height > upper.Viewport.Height);
        Assert.Equal(1, VerticalBar(upper).Opacity);
        Assert.True(VerticalBar(upper).IsHitTestVisible);
        Assert.InRange(upper.Bounds.Height, 1, Math.Ceiling(view.FindControl<Grid>("EditorContent")!.Bounds.Height * .6));
        Assert.True(lower.Bounds.Height > 0);
        Assert.Equal(upper.Viewport.Width, lower.Viewport.Width);
        Assert.All(scene.Annotations, a => { Assert.True(a.Bounds.Top >= 0); Assert.True(a.Bounds.Bottom <= scene.Frame.Viewport.Height); });
        Assert.Equal(ScrollBarVisibility.Disabled, upper.HorizontalScrollBarVisibility);
        Assert.Equal(ScrollBarVisibility.Disabled, lower.HorizontalScrollBarVisibility);
        var sidebarOffset = sidebar.Offset;
        lower.Offset = new(0, 60); Arrange(window, 1100, height);
        Assert.Equal(0, upper.Offset.Y); Assert.Equal(sidebarOffset, sidebar.Offset);
        var diagramOffset = lower.Offset;
        upper.Offset = new(0, upper.Extent.Height); Arrange(window, 1100, height);
        Assert.True(upper.Offset.Y > 0);
        Assert.Equal(diagramOffset, lower.Offset); Assert.Equal(sidebarOffset, sidebar.Offset);
        Assert.All(Diagrams(view), d => Assert.Same(scene.Frame.Layout, d.Projection!.StationLayout));
        Assert.Equal(1, calls);
        Snapshot(view, $"overflow-{(int)height}-{(int)offset}");
    }

    [Fact]
    public void EditorScrollbarAppearsAndDisappearsWithoutChangingStationCoordinatesOrOtherOffsets()
    {
        using var environment = new DesktopControlEnvironment();
        int calls = 0;
        var editor = new EditorViewModel(Document(), () => { }, b => { calls++; return BeamAnalysis.Analyze(b); });
        var view = new EditorView { DataContext = editor };
        var window = new Window { Content = view };
        Arrange(window, 1100, 618);
        var upper = view.FindControl<ScrollViewer>("EditorScroll")!;
        var lower = view.FindControl<ScrollViewer>("DiagramScroll")!;
        var surface = view.FindControl<BeamEditorSurface>("BeamSurface")!;
        var stations = surface.StationLayout!.Stations.Select(s => s.ScreenX).ToArray();
        double viewportWidth = upper.Viewport.Width;
        lower.Offset = new(0, 60);
        var diagramOffset = lower.Offset;
        var bar = VerticalBar(upper);
        Assert.Equal(0, bar.Maximum);
        Assert.Equal(0, bar.Opacity);
        Assert.False(bar.IsHitTestVisible);

        editor.SetAnnotationOffset(editor.Document.Loads[0].Id, new(0, -600));
        Arrange(window, 1100, 618);
        Assert.True(bar.Maximum > 0);
        Assert.Equal(1, bar.Opacity);
        Assert.True(bar.IsHitTestVisible);
        Assert.Equal(viewportWidth, upper.Viewport.Width);
        Assert.Equal(viewportWidth, lower.Viewport.Width);
        Assert.Equal(stations, surface.StationLayout!.Stations.Select(s => s.ScreenX));
        Assert.Equal(diagramOffset, lower.Offset);
        upper.Offset = new(0, upper.Extent.Height);
        Arrange(window, 1100, 618);
        Assert.True(upper.Offset.Y > 0);

        editor.SetAnnotationOffset(editor.Document.Loads[0].Id, null);
        Arrange(window, 1100, 618);
        Assert.Equal(0, bar.Maximum);
        Assert.Equal(0, bar.Opacity);
        Assert.False(bar.IsHitTestVisible);
        Assert.Equal(0, upper.Offset.Y);
        Assert.Equal(diagramOffset, lower.Offset);
        Assert.Equal(viewportWidth, upper.Viewport.Width);
        Assert.Equal(stations, surface.StationLayout!.Stations.Select(s => s.ScreenX));
        Assert.All(Diagrams(view), d => Assert.Same(surface.StationLayout, d.Projection!.StationLayout));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task OpenNewAndRecoveryReplaceTheDiagramSourceWithoutRetainingOldResults()
    {
        using var environment = new DesktopControlEnvironment();
        var app = new ProjectTestSupport.App();
        var view = new EditorView { DataContext = app.Main.Editor };
        var window = new Window { Content = view }; Arrange(window);
        Assert.All(Diagrams(view), d => Assert.Null(d.Projection));
        app.Dialogs.Leave = LeaveDecision.Discard;
        app.Dialogs.OpenPath = ProjectTestSupport.TestPath("diagrams.spandraft");
        app.Files.Data[app.Dialogs.OpenPath] = ProjectFileCodec.Serialize(new(Document(), new()));
        Assert.True(await app.Main.OpenAsync());
        view.DataContext = app.Main.Editor; Arrange(window);
        Assert.Equal(2, app.Analyses);
        Assert.All(Diagrams(view), d => Assert.Same(app.Main.Editor!.Presentation, d.Presentation));
        Assert.All(Diagrams(view), d => Assert.NotNull(d.Projection));
        Assert.True(await app.Main.NewAsync());
        view.DataContext = app.Main.Editor; Arrange(window);
        Assert.All(Diagrams(view), d => Assert.NotNull(d.Projection));
        ProjectTestSupport.CompleteSetup(app.Main); Assert.True(await app.Main.Setup.ConfirmAsync());
        view.DataContext = app.Main.Editor; Arrange(window);
        Assert.Equal(3, app.Analyses);
        Assert.All(Diagrams(view), d => Assert.Null(d.Projection));

        var restored = new ProjectTestSupport.App(create: false);
        await restored.Recovery.FlushAsync(new(Document(), new()), ProjectTestSupport.TestPath("recovered.spandraft"));
        Assert.True(await restored.Main.InitializeRecoveryAsync());
        view.DataContext = restored.Main.Editor; Arrange(window);
        Assert.Equal(1, restored.Analyses);
        Assert.All(Diagrams(view), d => { Assert.NotNull(d.Projection); Assert.Same(restored.Main.Editor!.Presentation, d.Presentation); });
    }

    [Fact]
    public void DiagramsMarkEverySegmentBoundaryAtItsStationPosition()
    {
        using var environment = new DesktopControlEnvironment();
        var document = Document();
        var first = document.Segments[0];
        document = new EditorDocument(document.Length, [Length.FromMeters(document.Length.Meters / 2)],
            [first, first], document.Supports, document.Loads, document.NamingState,
            document.DistributedLoads, document.SelfWeight);
        var editor = new EditorViewModel(document, () => { });
        var view = new EditorView { DataContext = editor };
        var window = new Window { Content = view };
        Arrange(window, 1250, 1300);

        var diagrams = Diagrams(view);
        Assert.NotEmpty(diagrams);
        var surface = view.FindControl<BeamEditorSurface>("BeamSurface")!;
        double expected = surface.StationLayout!.Transform.PhysicalToScreen(document.Length.Meters / 2);
        Assert.All(diagrams, d => Assert.Equal([expected], d.SegmentBoundaryXs));
    }
}
