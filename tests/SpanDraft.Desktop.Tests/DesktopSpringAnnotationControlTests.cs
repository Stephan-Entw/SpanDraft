using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SpanDraft.Analysis;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Controls;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;
using SpanDraft.Desktop.Views;
using Xunit;

namespace SpanDraft.Tests;

[Collection("Avalonia")]
public sealed class DesktopSpringAnnotationControlTests
{
    private static EditorDocument Document() => DesktopLayoutFixture.Document().WithSupports([
        new(Guid.NewGuid(), Length.FromMeters(.5), SupportType.Spring, "B", new(TranslationalStiffness.FromNewtonsPerMeter(1e5), null)),
        new(Guid.NewGuid(), Length.FromMeters(.5), SupportType.Spring, "C", new(null, RotationalStiffness.FromNewtonMetersPerRadian(1e4)))]);

    private static void Layout(Window window)
    {
        window.ApplyTemplate(); window.Measure(new(1250, 800)); window.Arrange(new Rect(0, 0, 1250, 800));
        var content = (Control)window.Content!;
        foreach (var child in content.GetVisualDescendants().OfType<Control>())
        { child.InvalidateMeasure(); child.InvalidateArrange(); }
        content.InvalidateMeasure(); content.Measure(new(1250, 800));
        content.InvalidateArrange(); content.Arrange(new Rect(0, 0, 1250, 800));
        Dispatcher.UIThread.RunJobs();
    }

    private static void Press(BeamEditorSurface surface, Pointer pointer, Window window, Point point) =>
        surface.RaiseEvent(new PointerPressedEventArgs(surface, pointer, window, point, 0,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None, 1));

    private static void Move(BeamEditorSurface surface, Pointer pointer, Window window, Point point) =>
        surface.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, surface, pointer, window, point, 1,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.Other), KeyModifiers.None));

    private static void Release(BeamEditorSurface surface, Pointer pointer, Window window, Point point) =>
        surface.RaiseEvent(new PointerReleasedEventArgs(surface, pointer, window, point, 2,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased), KeyModifiers.None, MouseButton.Left));

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void SharedLabelClickOffersBothNamesAndDraftNotificationsRefreshTheActualScene(int selected)
    {
        using var environment = new DesktopControlEnvironment();
        var editor = new EditorViewModel(Document(), () => { });
        var view = new EditorView { DataContext = editor };
        var window = new Window { Content = view };
        Layout(window);
        var surface = Assert.Single(view.GetVisualDescendants().OfType<BeamEditorSurface>());
        var canvas = surface.FindControl<BeamCanvas>("TechnicalCanvas")!;
        var label = Assert.Single(canvas.Scene!.Annotations);
        var point = surface.FindControl<Control>("BeamPane")!.TranslatePoint(label.Bounds.Center, window)!.Value;
        var pointer = new Pointer(1, PointerType.Mouse, true);
        var popup = surface.FindControl<Popup>("SelectionPopup")!;
        Press(surface, pointer, window, point);
        Assert.Same(surface, pointer.Captured);
        Assert.False(popup.IsOpen);
        Assert.Null(editor.SupportDraft);
        Release(surface, pointer, window, point);
        Assert.Null(pointer.Captured);
        Assert.True(popup.IsOpen);
        Assert.Null(editor.SupportDraft);
        var entries = surface.FindControl<StackPanel>("SelectionEntries")!.Children.OfType<Button>().ToArray();
        Assert.Equal(new[] { "B", "C" }, entries.Select(b => b.Content));
        entries[selected].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.False(popup.IsOpen);
        var id = editor.Document.Supports[selected].Id;
        Assert.Equal(id, editor.SupportDraft!.OriginalId);
        editor.SupportDraft.NameText = "Draft";
        Assert.Equal(selected == 0 ? "Draft + C" : "B + Draft", Assert.Single(canvas.Scene!.Annotations).Text);
        Assert.True(editor.BeginSupportDrag(id));
        editor.UpdateSupportDrag(Length.FromMeters(.7));
        Assert.Equal(2, canvas.Scene!.Annotations.Count);
        Assert.Equal("Draft", canvas.Scene.Annotations.Single(a => a.Id == id).Text);
        editor.UpdateSupportDrag(Length.FromMeters(.5));
        Assert.Equal(selected == 0 ? "Draft + C" : "B + Draft", Assert.Single(canvas.Scene!.Annotations).Text);
        editor.CancelSupportInteraction();
        Assert.Equal("B + C", Assert.Single(canvas.Scene!.Annotations).Text);
        Assert.Empty(editor.Session.UndoHistory);
        window.Close();
    }

    [Fact]
    public void SharedLabelPointerDragCanCancelAndCompletesAsOnePresentationChangeWithoutSelection()
    {
        using var environment = new DesktopControlEnvironment();
        int analyses = 0;
        var editor = new EditorViewModel(Document(), () => { }, beam => { analyses++; return BeamAnalysis.Analyze(beam); });
        var view = new EditorView { DataContext = editor };
        var window = new Window { Content = view };
        Layout(window);
        var surface = Assert.Single(view.GetVisualDescendants().OfType<BeamEditorSurface>());
        var canvas = surface.FindControl<BeamCanvas>("TechnicalCanvas")!;
        var label = Assert.Single(canvas.Scene!.Annotations);
        var point = surface.FindControl<Control>("BeamPane")!.TranslatePoint(label.Bounds.Center, window)!.Value;
        var pointer = new Pointer(1, PointerType.Mouse, true);
        var delta = new Vector(25, -15);
        foreach (bool escape in new[] { true, false })
        {
            Press(surface, pointer, window, point);
            Move(surface, pointer, window, point + delta);
            Assert.Equal(2, editor.RenderPresentation.AnnotationOffsets.Count);
            if (escape) surface.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
            else pointer.Capture(null);
            Assert.Null(pointer.Captured);
            Assert.Empty(editor.RenderPresentation.AnnotationOffsets);
            Assert.Empty(editor.Session.UndoHistory);
        }
        Press(surface, pointer, window, point);
        Move(surface, pointer, window, point + delta);
        Assert.Equal(label.Bounds.Translate(delta), Assert.Single(canvas.Scene!.Annotations).Bounds);
        Release(surface, pointer, window, point + delta);
        Assert.Single(editor.Session.UndoHistory);
        Assert.All(editor.Document.Supports, s => Assert.Equal(new AnnotationOffset(25, -15), editor.EditorPresentation.AnnotationOffsets[s.Id]));
        Assert.False(surface.FindControl<Popup>("SelectionPopup")!.IsOpen);
        Assert.Null(editor.SupportDraft);
        Assert.Null(pointer.Captured);
        Assert.Equal(1, analyses);
        window.Close();
    }
}
