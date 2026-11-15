using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Controls;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;
using SpanDraft.Desktop.Views;
using Xunit;

namespace SpanDraft.Tests;

[Collection("Avalonia")]
public sealed class DesktopSurfaceSynchronizationTests
{
    private static void Layout(Window window, int width, int height)
    {
        window.ApplyTemplate(); window.Measure(new(width, height)); window.Arrange(new Rect(0, 0, width, height));
        var content = (Control)window.Content!;
        foreach (var child in content.GetVisualDescendants().OfType<Control>())
        { child.InvalidateMeasure(); child.InvalidateArrange(); }
        content.InvalidateMeasure(); content.Measure(new(width, height));
        content.InvalidateArrange(); content.Arrange(new Rect(0, 0, width, height));
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public void SeveralLabelsOfOneEntityAreOneCandidate()
    {
        var document = SelfWeightTestProjects.Document(true);
        var frame = new BeamLayoutState().Update(document, 1100, 400)!;
        var scene = BeamRenderState.Create(document, frame, DesktopLayoutFixture.Measure);
        var id = document.SelfWeight!.Id;
        var label = Assert.Single(scene.Annotations, a => a.Id == id);
        // One self-weight label per segment share the entity id; stack two of them on one point.
        var stacked = scene with { Annotations = [label, label with { }] };
        var point = label.Bounds.Center;
        Assert.Equal(2, stacked.HitTestLabels(point.X, point.Y).Count);
        Assert.Equal(new[] { id }, stacked.HitTestLabelEntities(point.X, point.Y));
        Assert.Equal(new[] { id }, stacked.HitTestEntities(point.X, point.Y));
    }

    [Fact]
    public void NotificationRaisedDuringSynchronizationIsNotLost()
    {
        using var environment = new DesktopControlEnvironment();
        var editor = new EditorViewModel(DesktopLayoutFixture.Document(), () => { });
        var view = new EditorView { DataContext = editor };
        var window = new Window { Content = view };
        Layout(window, 1250, 800);
        var surface = Assert.Single(view.GetVisualDescendants().OfType<BeamEditorSurface>());
        var canvas = surface.FindControl<BeamCanvas>("TechnicalCanvas")!;
        Assert.DoesNotContain(canvas.Scene!.Supports, s => s.IsPreview);

        editor.ToggleSupportTool(SupportType.Pinned);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(SupportInteraction.Placement, editor.Interaction);
        Assert.DoesNotContain(canvas.Scene!.Supports, s => s.IsPreview);

        // The editor changes while a synchronization pass is running (here: right after the scene was assigned),
        // as happens when a popup closes inside the pass and the editor cancels its interaction.
        bool raised = false;
        canvas.PropertyChanged += (_, e) =>
        {
            if (e.Property != BeamCanvas.SceneProperty || raised) return;
            raised = true;
            editor.HoverPlacement(Length.FromMeters(.5));
        };
        // One notification starts exactly one pass: the editor's own state changes raise a batch of
        // notifications, and a later one of the batch would mask a lost nested one.
        typeof(SpanDraft.Desktop.ViewModels.ObservableObject)
            .GetMethod("Notify", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(editor, [nameof(EditorViewModel.HoveredSupportId)]);
        Dispatcher.UIThread.RunJobs();

        Assert.True(raised);
        Assert.NotNull(editor.Preview);
        Assert.Contains(canvas.Scene!.Supports, s => s.IsPreview);
    }
}
