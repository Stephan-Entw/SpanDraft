using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using SpanDraft.Core.Materials;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Sections.Parametric;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Controls;
using SpanDraft.Desktop.Layout;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;
using SpanDraft.Desktop.Views;
using Xunit;

namespace SpanDraft.Tests;

/// <summary>The segment row of the editor: a band of its own between the beam and the x axis, aligned with the stations.</summary>
[Collection("Avalonia")]
public sealed class DesktopSegmentRowControlTests
{
    private static Length M(double value) => Length.FromMeters(value);
    private static readonly Material Steel = new("S235JR", Pressure.FromPascals(210e9), Pressure.FromPascals(235e6),
        MaterialStrengthType.Re);
    private static EditorSegment Segment(double length) =>
        new(Steel, new RectangleSectionGeometry(M(0.1), M(0.2)), SectionAxisDesignation.Y) { Length = M(length) };

    private static (EditorViewModel Editor, EditorView View, Window Window) Open(params double[] lengths)
    {
        var document = new EditorDocument(lengths.Select(Segment).ToArray(),
            [new(Guid.NewGuid(), M(0), SupportType.Fixed, "A")], [new EditorPointForce(Guid.NewGuid(), M(lengths.Sum() / 2), Force.FromNewtons(-1000), "F1")]);
        var editor = new EditorViewModel(document, () => { });
        var view = new EditorView { DataContext = editor };
        var window = new Window { Content = view, Width = 1250, Height = 900 };
        window.Show();
        Arrange(window);
        return (editor, view, window);
    }

    private static void Arrange(Window window)
    {
        window.Measure(new Size(1250, 900));
        window.Arrange(new Rect(0, 0, 1250, 900));
        window.UpdateLayout();
    }

    private static void Capture(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("SPANDRAFT_VISUAL_TEST_OUTPUT") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory);
        using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(1250, 900), new Vector(96, 96));
        bitmap.Render(window);
        bitmap.Save(Path.Combine(directory, $"segment-row-{name}.png"));
    }

    private static double Top(Control control, Control relativeTo) => control.TranslatePoint(default, relativeTo)!.Value.Y;

    [Fact]
    public void SeveralSegmentsGetAFixedBandBetweenBeamAndAxisWithoutTakingRoomFromTheBeam()
    {
        using var environment = new DesktopControlEnvironment();
        var (_, oneView, oneWindow) = Open(3);
        var (editor, view, window) = Open(1, 1, 1);

        var one = oneView.GetVisualDescendants().OfType<BeamEditorSurface>().Single();
        var surface = view.GetVisualDescendants().OfType<BeamEditorSurface>().Single();
        var row = surface.FindControl<SegmentRowPane>("SegmentRow")!;
        var axis = surface.FindControl<CoordinateAxisPane>("CoordinateAxis")!;
        var pane = surface.FindControl<Grid>("BeamPane")!;

        // One segment: no row, the axis follows the beam directly.
        Assert.False(one.FindControl<SegmentRowPane>("SegmentRow")!.IsEffectivelyVisible);
        var onePane = one.FindControl<Grid>("BeamPane")!;
        Assert.Equal(Top(onePane, one) + onePane.Bounds.Height, Top(one.FindControl<CoordinateAxisPane>("CoordinateAxis")!, one), 0.5);

        // Several segments: a band of its own with a fixed height, between the beam and the axis.
        Assert.True(row.IsEffectivelyVisible);
        Assert.Equal(SchematicMetrics.SegmentRowHeight, row.Bounds.Height, 0.5);
        Assert.Equal(Top(pane, surface) + pane.Bounds.Height, Top(row, surface), 0.5);
        Assert.Equal(Top(row, surface) + row.Bounds.Height, Top(axis, surface), 0.5);
        // The beam keeps its room: its pane is as high as without a row, and the surface is taller by the band.
        Assert.Equal(onePane.Bounds.Height, pane.Bounds.Height, 0.5);
        Assert.Equal(one.Bounds.Height + SchematicMetrics.SegmentRowHeight, surface.Bounds.Height, 0.5);
        oneWindow.Close();
        window.Close();
    }

    [Fact]
    public void TheBoundariesOfTheRowStandOnTheStationsAndOnTheTicksOfTheAxis()
    {
        using var environment = new DesktopControlEnvironment();
        var (editor, view, window) = Open(1, 2, 1.5);
        var surface = view.GetVisualDescendants().OfType<BeamEditorSurface>().Single();
        var row = surface.FindControl<SegmentRowPane>("SegmentRow")!;
        var stations = surface.StationLayout!.Stations;

        Assert.Equal(3, row.Spans.Count);
        Assert.Equal(stations[0].ScreenX, row.Spans[0].StartX, 3);
        Assert.Equal(stations[^1].ScreenX, row.Spans[^1].EndX, 3);
        for (int i = 0; i < 2; i++)
        {
            double boundary = editor.Document.SegmentRange(i).End.Meters;
            double x = stations.Single(s => s.PhysicalX == boundary).ScreenX;
            Assert.Equal(x, row.Spans[i].EndX, 3);
            Assert.Equal(x, row.Spans[i + 1].StartX, 3);
        }
        // The row and the pane of the beam share their left edge, so the screen x values are the same in both.
        Assert.Equal(0, row.TranslatePoint(default, surface.FindControl<Grid>("BeamPane")!)!.Value.X, 0.5);
        Capture(window, "three");
        window.Close();
    }

    [Fact]
    public void EachSegmentShowsTheMostDetailedLabelThatFitsAndATinyOneShowsNone()
    {
        using var environment = new DesktopControlEnvironment();
        var (editor, view, window) = Open(0.1, 5, 4.9);
        var row = view.GetVisualDescendants().OfType<BeamEditorSurface>().Single().FindControl<SegmentRowPane>("SegmentRow")!;

        // 0.1 m of 10 m is a few pixels wide: not even the number fits and no label is drawn.
        Assert.Null(row.Spans[0].Text);
        // The wide segments have room for the complete description with the material.
        Assert.Equal(SegmentLabels.Description(editor.Document.Segments[1], editor.ResultPresentation), row.Spans[1].Text);
        Assert.Equal(SegmentLabels.Description(editor.Document.Segments[2], editor.ResultPresentation), row.Spans[2].Text);
        Capture(window, "tiny");
        window.Close();
    }
}
