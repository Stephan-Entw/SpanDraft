using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using SpanDraft.Core.Units;
using SpanDraft.Core.Supports;
using SpanDraft.Desktop.Controls;
using SpanDraft.Desktop.Layout;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;
using SpanDraft.Desktop.Views;
using Xunit;

namespace SpanDraft.Tests;

[Collection("Schematic text")]
public sealed class DesktopResultProbeControlTests
{
    [Theory]
    [InlineData("de-DE", "Probe")]
    [InlineData("en-US", "Probe")]
    public void ToolbarAvailabilityTracksAnalysisAndBusyState(string culture, string caption)
    {
        using var environment = new DesktopControlEnvironment();
        using var scope = new ResultCultureScope(culture, culture);
        var editor = new EditorViewModel(DesktopLayoutFixture.Document(), () => { });
        var view = new EditorView { DataContext = editor };
        var window = new Window { Content = view };
        Layout(window);
        var button = view.FindControl<ToggleButton>("ResultProbeToolButton")!;
        Assert.Equal(caption, Assert.Single(button.GetVisualDescendants().OfType<TextBlock>()).Text);
        Assert.Equal(caption, AutomationProperties.GetName(button));
        var icon = Assert.Single(button.GetVisualDescendants().OfType<Image>());
        Assert.Same(Application.Current!.FindResource("ToolbarIcon.Probe"), icon.Source);
        Assert.Equal(caption, Strings.ResultProbe);
        Assert.False(button.IsEffectivelyEnabled);
        editor.Session.Commit(editor.Session.CurrentRevision.State with { Document = DesktopResultProbeTests.Document() });
        Assert.True(button.IsEffectivelyEnabled);
        button.Command!.Execute(null);
        Assert.True(button.IsChecked);
        editor.Session.SetBusy(true);
        Assert.False(button.IsEffectivelyEnabled);
        editor.Session.SetBusy(false);
        Assert.True(button.IsEffectivelyEnabled);
        view.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
        Assert.False(button.IsChecked);
        Assert.False(editor.IsResultProbeTool);
    }

    [Theory]
    [InlineData(.725)]
    [InlineData(.5)]
    public void PointerMovementSnapsClickTogglesPinningAndEscapeEndsTheProbe(double releasePosition)
    {
        using var environment = new DesktopControlEnvironment();
        var editor = new EditorViewModel(DesktopResultProbeTests.Document(), () => { });
        var view = new EditorView { DataContext = editor };
        var window = new Window { Content = view };
        Layout(window);
        var surface = view.FindControl<BeamEditorSurface>("BeamSurface")!;
        var canvas = surface.FindControl<BeamCanvas>("TechnicalCanvas")!;
        var pane = surface.FindControl<Grid>("BeamPane")!;
        var marker = surface.FindControl<Border>("ProbeMarker")!;
        var dot = surface.FindControl<Ellipse>("ProbePoint")!;
        var box = surface.FindControl<Border>("ProbeBox")!;
        var frame = canvas.Scene!.Frame;
        var revision = editor.Session.CurrentRevision;
        var pointer = new Pointer(21, PointerType.Mouse, true);
        editor.ToggleResultProbeTool();
        Point At(double x) => new(frame.Layout.Transform.PhysicalToScreen(x), frame.Viewport.BeamY);
        Move(canvas, window, pointer, At(.725318));
        Layout(window);
        Assert.Equal(Length.FromMillimeters(725), editor.ResultProbePosition);
        Assert.True(box.IsVisible);
        Assert.True(marker.IsVisible);
        Assert.True(dot.IsVisible);
        Assert.Equal(frame.Layout.Transform.PhysicalToScreen(.725), Canvas.GetLeft(marker), 9);
        Assert.Equal(Canvas.GetLeft(marker), Canvas.GetLeft(dot) + dot.Width / 2, 9);
        Assert.Equal(frame.Viewport.BeamY, Canvas.GetTop(dot) + dot.Height / 2, 9);
        Assert.False(box.IsHitTestVisible);
        Assert.False(marker.IsHitTestVisible);
        Assert.False(dot.IsHitTestVisible);
        Move(canvas, window, pointer, At(.725) + new Vector(0, SupportSnap.BeamHitRadius + 1));
        Assert.Null(editor.ResultProbePosition);
        Assert.False(box.IsVisible);
        Assert.False(dot.IsVisible);
        Press(canvas, window, pointer, At(.725318));
        Assert.True(editor.IsResultProbeFixed);
        Move(canvas, window, pointer, At(.5));
        Exit(surface, window, pointer);
        Assert.Equal(Length.FromMillimeters(725), editor.ResultProbePosition);
        Assert.True(box.IsVisible);
        Assert.True(dot.IsVisible);
        Press(canvas, window, pointer, At(.5) + new Vector(0, SupportSnap.BeamHitRadius + 1));
        Assert.True(editor.IsResultProbeFixed);
        Press(canvas, window, pointer, At(releasePosition));
        Assert.False(editor.IsResultProbeFixed);
        Assert.Equal(Length.FromMeters(releasePosition), editor.ResultProbePosition);
        Move(canvas, window, pointer, At(.6));
        Assert.Equal(Length.FromMillimeters(600), editor.ResultProbePosition);
        var station = editor.Document.Loads[0].Position;
        Press(canvas, window, pointer, At(station.Meters));
        Assert.True(editor.IsResultProbeFixed);
        Assert.Equal(station, editor.ResultProbePosition);
        Assert.Equal(frame.Layout.Transform.PhysicalToScreen(station.Meters), Canvas.GetLeft(dot) + dot.Width / 2, 9);
        Assert.Contains(editor.ResultProbeRows, r => r.Label == "V−");
        Assert.Contains(editor.ResultProbeRows, r => r.Label == "M+");
        Assert.Null(editor.LoadDraft);
        Assert.Null(editor.SupportDraft);
        Assert.Empty(editor.EditorPresentation.AnnotationOffsets);
        var axis = surface.FindControl<CoordinateAxisPane>("CoordinateAxis")!;
        Move(axis, window, pointer, new Point(100, 10));
        Assert.Equal(station, editor.ResultProbePosition);
        var escape = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape };
        surface.RaiseEvent(escape);
        Assert.True(escape.Handled);
        Assert.False(editor.IsResultProbeTool);
        Assert.False(box.IsVisible);
        Assert.False(dot.IsVisible);
        Assert.Same(revision, editor.Session.CurrentRevision);
        editor.ToggleResultProbeTool();
        Move(canvas, window, pointer, At(.5));
        Move(axis, window, pointer, new Point(100, 10));
        Assert.Null(editor.ResultProbePosition);
        Move(canvas, window, pointer, At(.5));
        Exit(surface, window, pointer);
        Assert.Null(editor.ResultProbePosition);
        Assert.True(pane.Bounds.Width > 0);
    }

    [Theory]
    [InlineData(900)]
    [InlineData(1250)]
    public void OverlayStaysInsideBeamPaneWithoutChangingBeamOrDiagramLayout(double width)
    {
        using var environment = new DesktopControlEnvironment();
        var editor = new EditorViewModel(DesktopResultProbeTests.Document(), () => { }, resultPresentation:
            new(UnitProfile.StructuralEngineering, 15, EngineeringNotation.Off));
        var view = new EditorView { DataContext = editor };
        var window = new Window { Content = view };
        Layout(window, width);
        var surface = view.FindControl<BeamEditorSurface>("BeamSurface")!;
        var pane = surface.FindControl<Grid>("BeamPane")!;
        var canvas = surface.FindControl<BeamCanvas>("TechnicalCanvas")!;
        var box = surface.FindControl<Border>("ProbeBox")!;
        var layout = surface.StationLayout;
        var paneSize = pane.Bounds.Size;
        var diagrams = view.GetVisualDescendants().OfType<ResultDiagram>().ToArray();
        var projections = diagrams.Select(d => d.Projection).ToArray();
        var bounds = diagrams.Select(d => d.Bounds).ToArray();
        editor.ToggleResultProbeTool();
        foreach (double x in new[] { 0, .33337, 1 })
        {
            if (editor.IsResultProbeFixed) editor.ToggleResultProbeFixed(editor.ResultProbePosition!.Value);
            editor.ToggleResultProbeFixed(Length.FromMeters(x), atStation: true);
            Layout(window, width);
            Assert.True(box.IsVisible);
            var rect = new Rect(Canvas.GetLeft(box), Canvas.GetTop(box), box.Bounds.Width, box.Bounds.Height);
            Assert.True(new Rect(pane.Bounds.Size).Contains(rect), $"{rect} outside {pane.Bounds.Size}");
            if (x == 1) Assert.True(rect.Right < layout!.Transform.PhysicalToScreen(x));
            Assert.Same(layout, surface.StationLayout);
            Assert.Equal(paneSize, pane.Bounds.Size);
            Assert.Equal(projections, diagrams.Select(d => d.Projection));
            Assert.Equal(bounds, diagrams.Select(d => d.Bounds));
            Assert.Equal(layout, canvas.Scene!.Frame.Layout);
        }
    }

    [Fact]
    public void RebindingAndLeavingForAReportDiscardTheProbeAndPresentationChangesKeepItsPosition()
    {
        using var environment = new DesktopControlEnvironment();
        var editor = new EditorViewModel(DesktopResultProbeTests.Document(), () => { });
        var view = new EditorView { DataContext = editor };
        var window = new Window { Content = view };
        Layout(window);
        editor.ToggleResultProbeTool();
        editor.ToggleResultProbeFixed(editor.Document.Loads[0].Position, atStation: true);
        var solution = editor.Presentation.Result;
        var position = editor.ResultProbePosition;
        var before = editor.ResultProbeRows;
        editor.ApplyResultPresentation(new(UnitProfile.UnitedStates, 6));
        Layout(window);
        Assert.Equal(position, editor.ResultProbePosition);
        Assert.True(editor.IsResultProbeFixed);
        Assert.Same(solution, editor.Presentation.Result);
        Assert.NotEqual(before[0].Value, editor.ResultProbeRows[0].Value);
        Assert.EndsWith(" ft", editor.ResultProbeRows[0].Value);
        var next = new EditorViewModel(DesktopResultProbeTests.Document(), () => { });
        view.DataContext = next;
        Assert.False(editor.IsResultProbeTool);
        Assert.Null(editor.ResultProbePosition);
        next.ToggleResultProbeTool();
        next.ToggleResultProbeFixed(Length.FromMillimeters(700));
        next.IsReportOpen = true;
        window.Content = new TextBlock { Text = "Report" };
        Assert.False(next.IsResultProbeTool);
        Assert.Null(next.ResultProbePosition);
    }

    [Theory]
    [InlineData(1100, 650, 1)]
    [InlineData(1100, 650, 1.25)]
    [InlineData(1250, 800, 1)]
    [InlineData(1250, 800, 1.5)]
    [InlineData(1250, 800, 2)]
    public void SupportJumpBoxClearsTheAxisAfterLiveRowChangesAndWindowResizing(double width, double height, double scaling)
    {
        using var environment = new DesktopControlEnvironment(scaling);
        var document = DesktopLayoutFixture.Document(1.6).WithSupports([
            new(Guid.NewGuid(), Length.FromMeters(0), SupportType.Fixed, "A"),
            new(Guid.NewGuid(), Length.FromMillimeters(1050), SupportType.Pinned, "B")]).WithLoads([
            new EditorPointForce(Guid.NewGuid(), Length.FromMillimeters(1300), Force.FromNewtons(-1500), "F1"),
            new EditorPointForce(Guid.NewGuid(), Length.FromMillimeters(1600), Force.FromNewtons(-530), "F2")]);
        var editor = new EditorViewModel(document, () => { });
        var view = new EditorView { DataContext = editor };
        var window = new Window { Content = view };
        Layout(window, width, height);
        var surface = view.FindControl<BeamEditorSurface>("BeamSurface")!;
        var canvas = surface.FindControl<BeamCanvas>("TechnicalCanvas")!;
        var box = surface.FindControl<Border>("ProbeBox")!;
        var axis = surface.FindControl<CoordinateAxisPane>("CoordinateAxis")!;
        var pointer = new Pointer(25, PointerType.Mouse, true);
        editor.ToggleResultProbeTool();
        foreach (double x in new[] { .7, 1.05, 1.3, .7, 1.05 })
        {
            var frame = canvas.Scene!.Frame;
            Move(canvas, window, pointer, new(frame.Layout.Transform.PhysicalToScreen(x), frame.Viewport.BeamY));
            Layout(window, width, height);
            if (x == 1.05) Assert.Equal(6, editor.ResultProbeRows.Count);
            AssertAboveAxis();
        }
        editor.ToggleResultProbeFixed(Length.FromMillimeters(1050), atStation: true);
        Layout(window, width + 150, height + 100);
        AssertAboveAxis();
        void AssertAboveAxis()
        {
            var bottom = box.TranslatePoint(new(0, box.Bounds.Height), surface)!.Value.Y;
            var axisTop = axis.TranslatePoint(default, surface)!.Value.Y;
            Assert.True(bottom < axisTop, $"Probe bottom {bottom} overlaps axis at {axisTop}");
            Assert.All(box.GetVisualDescendants().OfType<TextBlock>(), text =>
            {
                var bounds = new Rect(text.TranslatePoint(default, box)!.Value, text.Bounds.Size);
                Assert.True(new Rect(box.Bounds.Size).Contains(bounds), $"Clipped probe row: {text.Text}");
            });
        }
    }

    private static void Move(Control source, Window window, Pointer pointer, Point point)
    {
        var root = source.TranslatePoint(point, window)!.Value;
        source.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, source, pointer, window, root, 0,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.Other), KeyModifiers.None));
    }

    private static void Exit(Control source, Window window, Pointer pointer) =>
        source.RaiseEvent(new PointerEventArgs(InputElement.PointerExitedEvent, source, pointer, window, new(-1, -1), 0,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.Other), KeyModifiers.None));

    private static void Press(Control source, Window window, Pointer pointer, Point point)
    {
        var root = source.TranslatePoint(point, window)!.Value;
        source.RaiseEvent(new PointerPressedEventArgs(source, pointer, window, root, 0,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None, 1));
        source.RaiseEvent(new PointerReleasedEventArgs(source, pointer, window, root, 1,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased), KeyModifiers.None, MouseButton.Left));
        Assert.Null(pointer.Captured);
    }

    private static void Layout(Window window, double width = 1250, double height = 800)
    {
        window.ApplyTemplate();
        window.Measure(new Size(width, height));
        window.Arrange(new Rect(0, 0, width, height));
        var view = (EditorView)window.Content!;
        view.Width = width; view.Height = height;
        for (int pass = 0; pass < 3; pass++)
        {
            foreach (var control in view.GetVisualDescendants().OfType<Control>())
            { control.InvalidateMeasure(); control.InvalidateArrange(); }
            view.InvalidateMeasure(); view.Measure(new Size(width, height));
            view.InvalidateArrange(); view.Arrange(new Rect(0, 0, width, height));
        }
    }
}
