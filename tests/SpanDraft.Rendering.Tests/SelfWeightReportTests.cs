using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using SpanDraft.Desktop.Controls;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Reporting;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;
using Xunit;
using static SpanDraft.Tests.ProjectTestSupport;

namespace SpanDraft.Tests;

[Collection("Avalonia")]
public sealed class SelfWeightReportTests
{

    private static IEnumerable<GeometryDrawing> Geometry(Drawing drawing) => drawing is DrawingGroup group
        ? group.Children.SelectMany(Geometry) : drawing is GeometryDrawing geometry ? [geometry] : [];

    [Theory]
    [InlineData("de-DE", true)] [InlineData("de-DE", false)]
    [InlineData("en-US", true)] [InlineData("en-US", false)]
    public void ReportUsesOverviewTableAndSavedSceneVisibilityAndOffset(string culture, bool visible)
    {
        using var environment = new DesktopControlEnvironment(); using var scope = new UiCultureScope(culture);
        var document = SelfWeightTestProjects.Document(true).WithDistributedLoads([
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
}
