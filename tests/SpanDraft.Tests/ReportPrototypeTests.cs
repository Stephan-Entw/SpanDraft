using System.Runtime.CompilerServices;
using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using SkiaSharp;
using SpanDraft.Desktop.Controls;
using SpanDraft.Desktop.Layout;
using SpanDraft.Desktop.State;
using SpanDraft.Reporting.Prototype;
using Xunit;

namespace SpanDraft.Tests;

[Collection("Schematic text")]
public sealed class ReportPrototypeTests : IDisposable
{
    private readonly CultureInfo previousCulture = CultureInfo.CurrentCulture;
    private readonly CultureInfo previousUiCulture = CultureInfo.CurrentUICulture;
    private readonly DesktopControlEnvironment environment;

    public ReportPrototypeTests()
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");
        environment = new DesktopControlEnvironment();
        Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
    }

    public void Dispose()
    {
        environment.Dispose();
        CultureInfo.CurrentCulture = previousCulture;
        CultureInfo.CurrentUICulture = previousUiCulture;
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 2)]
    public void RenderReviewArtifactsWithCompleteRowsAndUnclippedText(bool overflow, int expectedPages)
    {
        var fixture = ReportPrototypeFixtures.Create(overflow);
        var visuals = new ReportPrototypeVisuals(fixture);
        using var report = new PrototypeReport(fixture.Data, visuals.Graphics);
        Assert.Equal(expectedPages, report.Pages.Count);
        Assert.True(report.Pages[0].HasGraphics);
        Assert.All(report.Pages.Skip(1), page => Assert.False(page.HasGraphics));
        Assert.Equal(!overflow, report.Pages[0].SummaryTop.HasValue);
        Assert.Single(report.Pages, page => page.SummaryTop.HasValue);
        for (int table = 0; table < fixture.Data.Tables.Count; table++)
        {
            var fragments = report.Pages.SelectMany(p => p.Tables).Where(f => f.Table == table).ToArray();
            var rows = fragments.SelectMany(f => Enumerable.Range(f.Start, f.Count));
            Assert.Equal(Enumerable.Range(0, fixture.Data.Tables[table].Rows.Length), rows);
            Assert.All(fragments, f =>
            {
                Assert.True(f.Count > 0);
                Assert.Equal(PrototypeReport.TableHeadingHeight + f.Count * PrototypeReport.RowHeight, f.Bounds.Height, 3);
                Assert.InRange(f.Bounds.Right, f.Bounds.Left, PrototypeReport.PageWidth - PrototypeReport.Margin + .01f);
            });
        }
        string directory = Path.Combine(RepositoryRoot(), "output", "pdf");
        Directory.CreateDirectory(directory);
        string name = overflow ? "spandraft-ueberlauf" : "spandraft-standard";
        using (var pdf = File.Create(Path.Combine(directory, name + ".pdf"))) report.WritePdf(pdf);
        string pdfContent = System.Text.Encoding.Latin1.GetString(File.ReadAllBytes(Path.Combine(directory, name + ".pdf")));
        Assert.DoesNotMatch(@"/Subtype\s*/Image\b", pdfContent);
        Assert.Equal(expectedPages, Regex.Matches(pdfContent, @"/Type\s*/Page\b").Count);
        var mediaBoxes = Regex.Matches(pdfContent, @"/MediaBox\s*\[([^]]+)\]");
        Assert.Equal(expectedPages, mediaBoxes.Count);
        Assert.All(mediaBoxes.Cast<Match>(), box =>
            Assert.Equal(new[] { "0", "0", "595", "842" }, box.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)));
        var areas = report.TechnicalAreas(0);
        Assert.Equal(4, areas.Count);
        Assert.All(areas, area => Assert.Equal(PrototypeReport.LeftWidth, area.Bounds.Width, 3));
        Assert.All(areas.Zip(areas.Skip(1)), pair => Assert.True(pair.First.Bounds.Bottom <= pair.Second.Bounds.Top));
        Assert.True(areas[^1].Bounds.Bottom < (report.Pages[0].SummaryTop ?? PrototypeReport.FooterTop));
        for (int page = 0; page < report.Pages.Count; page++)
        {
            using var png = File.Create(Path.Combine(directory, $"{name}-seite-{page + 1}.png"));
            report.WritePng(png, page);
            Assert.All(report.TextRuns, run =>
            {
                Assert.True(run.Bounds.Left >= PrototypeReport.Margin - .1f, run.Text);
                Assert.True(run.Bounds.Right <= PrototypeReport.PageWidth - PrototypeReport.Margin + .1f, run.Text);
                Assert.True(run.Bounds.Top >= PrototypeReport.Margin - .1f, run.Text);
                Assert.True(run.Bounds.Bottom < PrototypeReport.PageHeight - PrototypeReport.Margin + 1, run.Text);
            });
            Assert.Contains(report.TextRuns, run => run.Text == $"Seite {page + 1} von {expectedPages}");
            Assert.Contains(report.TextRuns, run => run.Text == fixture.Data.Date);
            if (report.Pages[page].SummaryTop is { } top)
                Assert.True(top + PrototypeReport.SummaryHeight < PrototypeReport.FooterTop);
        }
        using var bitmap = SKBitmap.Decode(Path.Combine(directory, name + "-seite-1.png"));
        Assert.Equal(2480, bitmap.Width);
        Assert.Equal(3508, bitmap.Height);
        WriteDesktopComparisons(visuals, report, directory, name);
    }

    [Fact]
    public void ActualControlsShareStationsAndProjectTheOriginalAnalysis()
    {
        foreach (bool overflow in new[] { false, true })
        {
            var fixture = ReportPrototypeFixtures.Create(overflow);
            var visuals = new ReportPrototypeVisuals(fixture);
            using var report = new PrototypeReport(fixture.Data, visuals.Graphics);
            using var png = new MemoryStream();
            report.WritePng(png, 0);
            var stations = visuals.Beam.Scene!.Frame.Layout;
            Assert.Same(stations, visuals.Axis.AxisLayout!.StationLayout);
            for (int diagram = 0; diagram < visuals.Diagrams.Length; diagram++)
            {
                var control = visuals.Diagrams[diagram];
                Assert.Same(fixture.Presentation, control.Presentation);
                Assert.Same(fixture.Analysis, control.Presentation!.Result);
                Assert.Same(stations, control.StationLayout);
                var projection = Assert.IsType<ResultDiagramProjection>(control.Projection);
                Assert.Same(stations, projection.StationLayout);
                Assert.NotEmpty(projection.Sections);
                foreach (var point in projection.Sections.SelectMany(s => s))
                {
                    var actual = fixture.Analysis.Solution.EvaluateAt(point.Position, point.Side);
                    double si = diagram switch
                    {
                        0 => actual.TransverseDisplacement.Meters, 1 => actual.ShearForce.Newtons,
                        _ => actual.BendingMoment.NewtonMeters
                    };
                    Assert.Equal(si, point.SiValue);
                    Assert.Equal(stations.Transform.PhysicalToScreen(point.Position.Meters), point.Screen.X, 6);
                }
                Assert.Equal(projection.Markers.Count, control.MarkerLabels.Count);
                Assert.All(control.MarkerLabels, label => Assert.True(new Rect(control.Bounds.Size).Contains(label.Bounds)));
                var area = report.TechnicalAreas(0)[diagram + 1].Bounds;
                Assert.Equal(area.Height / ReportPrototypeVisuals.PointsPerDip, control.Bounds.Height, 5);
                Assert.Contains("[" + projection.Unit.Symbol + "]", control.Title);
            }
            Assert.Equal(fixture.Analysis.Beam.BendingAxisProperties.SecondMomentOfArea,
                fixture.Data.Section.GetAxis(fixture.Analysis.Beam.BendingAxis).SecondMomentOfArea);
        }
    }

    [Fact]
    public void MissingOptionalProjectInformationLeavesNoEmptyHeaderRows()
    {
        var data = ReportPrototypeFixtures.Create(false).Data;
        var visuals = new ReportPrototypeVisuals(ReportPrototypeFixtures.Create(false));
        using var full = new PrototypeReport(data, visuals.Graphics);
        using var compact = new PrototypeReport(data with { Project = null, Reference = "" }, visuals.Graphics);
        Assert.Equal(full.Pages[0].BodyTop - 24, compact.Pages[0].BodyTop);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SmallerVisualsUseWiderStationsAndGiveEachDiagramMoreHeight(bool overflow)
    {
        var fixture = ReportPrototypeFixtures.Create(overflow);
        var visuals = new ReportPrototypeVisuals(fixture);
        using var report = new PrototypeReport(fixture.Data, visuals.Graphics);
        const double originalPointsPerDip = 72d / 96;
        double originalWidth = PrototypeReport.LeftWidth / originalPointsPerDip;
        var state = new BeamLayoutState();
        Size Measure(string text) => SchematicText.Measure(text, Typeface.Default, 13);
        var initial = state.Update(fixture.Document, originalWidth, SchematicMetrics.MinimumBeamPaneHeight)!;
        var request = BeamRenderState.Create(fixture.Document, initial, Measure,
            presentation: fixture.EditorPresentation, profile: fixture.Presentation.Options.Profile);
        var original = state.Update(fixture.Document, originalWidth, request.MinimumPaneHeight, request.RequiredBelowBeamSpace)!;
        var originalAxis = CoordinateAxisLayout.Create(original.Layout, originalWidth, Measure,
            unit: fixture.Presentation.Options.Profile[SpanDraft.Desktop.Presentation.QuantityKind.BeamLength]);
        using var originalReport = new PrototypeReport(fixture.Data,
            new((float)((original.Viewport.Height + originalAxis.PaneHeight) * originalPointsPerDip), (_, _, _) => { }));

        var areas = report.TechnicalAreas(0);
        var originalAreas = originalReport.TechnicalAreas(0);
        var stations = visuals.Beam.Scene!.Frame.Layout.Stations;
        Assert.Equal(.8, ReportPrototypeVisuals.PointsPerDip / originalPointsPerDip, 12);
        Assert.Equal(7, visuals.Beam.LabelFontSize * ReportPrototypeVisuals.PointsPerDip, 10);
        Assert.Equal(visuals.Beam.LabelFontSize, visuals.Axis.LabelFontSize);
        Assert.Equal(.75, visuals.Beam.SupportSymbolScale);
        Assert.All(visuals.Beam.Scene!.Loads, load => Assert.Equal(.85, load.SymbolScale));
        Assert.All(visuals.Beam.Scene.DistributedLoads, load => Assert.Equal(.85, load.SymbolScale));
        Assert.InRange(visuals.MeasurementPasses, 1, 8);
        Assert.True((stations[^1].ScreenX - stations[0].ScreenX) * ReportPrototypeVisuals.PointsPerDip
            > (original.Layout.Stations[^1].ScreenX - original.Layout.Stations[0].ScreenX) * originalPointsPerDip);
        Assert.True(areas[0].Bounds.Height < originalAreas[0].Bounds.Height);
        for (int i = 1; i < areas.Count; i++)
            Assert.True(areas[i].Bounds.Height > originalAreas[i].Bounds.Height);
        Assert.Equal(originalAreas[^1].Bounds.Bottom, areas[^1].Bounds.Bottom, 3);
        Assert.Equal(originalReport.Pages.Count, report.Pages.Count);
        foreach (var (before, after) in originalReport.Pages.Zip(report.Pages))
        {
            Assert.Equal(before.BodyTop, after.BodyTop);
            Assert.Equal(before.SummaryTop, after.SummaryTop);
            Assert.Equal(before.HasGraphics, after.HasGraphics);
            Assert.Equal(before.Tables, after.Tables);
        }

        var width = visuals.Beam.Bounds.Width;
        var axis = visuals.Axis.AxisLayout!;
        Assert.All(axis.Labels, label => Assert.True(new Rect(visuals.Axis.Bounds.Size).Contains(label.Bounds)));
        var unit = SchematicText.Measure("x [" + axis.UnitSymbol + "]", Typeface.Default, visuals.Axis.LabelFontSize);
        Assert.True(stations[^1].ScreenX + 8 + unit.Width <= width - 8 + 1e-6);
        Assert.All(visuals.Beam.Scene.Annotations, label => Assert.True(new Rect(visuals.Beam.Bounds.Size).Contains(label.Bounds)));
        Assert.True(stations[0].ScreenX >= stations[0].LeftExtent + SchematicMetrics.OuterPadding);
        Assert.True(stations[^1].ScreenX + stations[^1].RightExtent <= width - SchematicMetrics.OuterPadding);
        foreach (var diagram in visuals.Diagrams)
            foreach (var tick in diagram.Projection!.Ticks)
                Assert.True(SchematicText.Measure(diagram.TickLabel(tick), Typeface.Default, 12).Width + 16
                    <= stations[0].ScreenX + 1e-6);

        using var surface = SKSurface.Create(new SKImageInfo(596, 842));
        report.DrawPage(surface.Canvas, 0);
        foreach (var control in new[] { visuals.BeamVisual }.Concat(visuals.Diagrams))
        {
            var transform = control.RenderTransform!.Value;
            Assert.Equal(ReportPrototypeVisuals.PointsPerDip, transform.M11, 12);
            Assert.Equal(transform.M11, transform.M22);
            Assert.Equal(0, transform.M12);
            Assert.Equal(0, transform.M21);
            Assert.Equal(PrototypeReport.LeftWidth, control.Bounds.Width * transform.M11, 4);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ApprovedLayoutKeepsInlinePropertiesAndEqualInformationBlockGaps(bool overflow)
    {
        var fixture = ReportPrototypeFixtures.Create(overflow);
        var visuals = new ReportPrototypeVisuals(fixture);
        using var report = new PrototypeReport(fixture.Data, visuals.Graphics);
        using var surface = SKSurface.Create(new SKImageInfo(596, 842));
        report.DrawPage(surface.Canvas, 0);
        Assert.Equal(128 * PrototypeReport.Mm, PrototypeReport.LeftWidth);
        Assert.Equal(50 * PrototypeReport.Mm, PrototypeReport.RightWidth);
        var title = Assert.Single(report.TextRuns, r => r.Text == "Berechnungsbericht");
        Assert.Equal(PrototypeReport.PageWidth / 2, title.Bounds.MidX, 3);
        Assert.DoesNotContain(report.TextRuns, r => r.Text is "Balkenmodell" or "Querschnitt" || r.Text.StartsWith("L = "));

        var iy = Assert.Single(report.TextRuns, r => r.Text == "Iy = " + fixture.Data.Inertia).Bounds;
        var wy = Assert.Single(report.TextRuns, r => r.Text == "Wy = " + fixture.Data.Modulus).Bounds;
        var e = Assert.Single(report.TextRuns, r => r.Text == "E = 210000 MPa").Bounds;
        var re = Assert.Single(report.TextRuns, r => r.Text == "Re = 235 MPa").Bounds;
        Assert.Equal(iy.Top, wy.Top);
        Assert.Equal(e.Top, re.Top);
        Assert.True(iy.Right < wy.Left);
        Assert.True(e.Right < re.Left);
        Assert.All(new[] { iy, wy, e, re }, bounds =>
        {
            Assert.True(bounds.Left >= PrototypeReport.Margin + PrototypeReport.LeftWidth + PrototypeReport.Gutter);
            Assert.True(bounds.Right <= PrototypeReport.PageWidth - PrototypeReport.Margin + .01f);
        });

        var tables = report.Pages[0].Tables;
        var ends = new[] { Math.Max(iy.Bottom, wy.Bottom), Math.Max(e.Bottom, re.Bottom) }
            .Concat(tables.Select(table => report.TextRuns.Where(r =>
                r.Bounds.Left >= table.Bounds.Left - .01f && r.Bounds.Top >= table.Bounds.Top
                && r.Bounds.Top < table.Bounds.Bottom).Max(r => r.Bounds.Bottom))).ToArray();
        var starts = new[] { Assert.Single(report.TextRuns, r => r.Text == "Material").Bounds.Top }
            .Concat(tables.Select(table => table.Bounds.Top)).ToArray();
        var gaps = starts.Select((start, i) => start - ends[i]).ToArray();
        Assert.All(gaps, gap => { Assert.True(gap > 0); Assert.Equal(gaps[^1], gap, 3); });
    }

    [Fact]
    public void StandardFixtureReportsTheBalancedSystemAndExistingEngineeringValues()
    {
        var fixture = ReportPrototypeFixtures.Create(false);
        var nodes = fixture.Analysis.Solution.Nodes;
        SolverTestSupport.ForceClose(1000, nodes[0].ReactionY);
        SolverTestSupport.ForceClose(800, nodes[^1].ReactionY);
        SolverTestSupport.Close(710, fixture.Analysis.Engineering.BendingMomentMagnitude.NewtonMeters, 1e-7);
        Assert.Equal(new[] { "0,78 mm", "710 N·m", "23 MPa", "10" }, fixture.Data.Indicators.Select(i => i.Value));
    }

    [Fact]
    public void ContinuationCanExtendBeyondTwoPagesWithoutLostOrSplitRows()
    {
        var data = ReportPrototypeFixtures.Create(true).Data;
        var visuals = new ReportPrototypeVisuals(ReportPrototypeFixtures.Create(true));
        var tables = data.Tables.Select(t => t with { Rows = Enumerable.Range(0, 8).SelectMany(_ => t.Rows).ToArray() }).ToArray();
        using var report = new PrototypeReport(data with { Tables = tables }, visuals.Graphics);
        Assert.True(report.Pages.Count > 2);
        for (int table = 0; table < tables.Length; table++)
            Assert.Equal(Enumerable.Range(0, tables[table].Rows.Length), report.Pages.SelectMany(p => p.Tables)
                .Where(f => f.Table == table).SelectMany(f => Enumerable.Range(f.Start, f.Count)));
        Assert.True(report.Pages[1].SummaryTop.HasValue);
    }

    [Fact]
    public void RenderThreeColumnContinuationForVisualComparison()
    {
        var fixture = ReportPrototypeFixtures.Create(true);
        var visuals = new ReportPrototypeVisuals(fixture);
        using var report = new PrototypeReport(fixture.Data, visuals.Graphics, continuationColumns: 3);
        string directory = Path.Combine(RepositoryRoot(), "output", "pdf", "comparison");
        Directory.CreateDirectory(directory);
        using var png = File.Create(Path.Combine(directory, "ueberlauf-drei-spalten.png"));
        report.WritePng(png, 1);
    }

    [Fact]
    public void CommittedOffsetsUseNewAutomaticAnchorsAndNeutralControls()
    {
        var fixture = ReportPrototypeFixtures.Create(false);
        var id = fixture.Document.Loads[0].Id;
        var offset = new AnnotationOffset(18, 12);
        var manual = fixture with { EditorPresentation = fixture.EditorPresentation.WithOffset(id, offset) };
        var visuals = new ReportPrototypeVisuals(manual);
        var label = Assert.Single(visuals.Beam.Scene!.Annotations, a => a.Id == id);
        Assert.True(label.IsManual);
        Assert.Equal(label.AutoBounds.Translate(new Vector(offset.Dx, offset.Dy)), label.Bounds);
        Assert.Null(visuals.Beam.HighlightedLoadId);
        Assert.Null(visuals.Beam.HighlightedDistributedLoadId);
        Assert.Null(visuals.Beam.HighlightedSupportId);
        Assert.Null(visuals.Beam.ConflictEntityIds);
        Assert.Null(visuals.Beam.ConstraintConflict);
        Assert.False(visuals.Beam.IsHitTestVisible);
        Assert.False(visuals.Axis.IsHitTestVisible);
        Assert.All(visuals.Diagrams, diagram => Assert.False(diagram.IsHitTestVisible));
        Assert.All(visuals.Beam.Scene.Annotations, a => { Assert.False(a.IsPreview); Assert.False(a.IsInvalid); });
        Assert.All(visuals.Beam.Scene.Supports, s => Assert.False(s.IsPreview));
        Assert.All(visuals.Beam.Scene.Loads, l => Assert.False(l.IsPreview));
        Assert.All(visuals.Beam.Scene.DistributedLoads, l => Assert.False(l.IsPreview));
        string directory = Path.Combine(RepositoryRoot(), "output", "pdf", "comparison");
        Directory.CreateDirectory(directory);
        foreach (var (current, name) in new[] { (fixture, "labels-automatisch"), (manual, "labels-manuell") })
        {
            var currentVisuals = new ReportPrototypeVisuals(current);
            using var report = new PrototypeReport(current.Data, currentVisuals.Graphics);
            using var png = File.Create(Path.Combine(directory, name + ".png"));
            report.WritePng(png, 0);
        }
        using var automaticImage = SKBitmap.Decode(Path.Combine(directory, "labels-automatisch.png"));
        using var manualImage = SKBitmap.Decode(Path.Combine(directory, "labels-manuell.png"));
        Assert.False(automaticImage.Bytes.SequenceEqual(manualImage.Bytes));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StandaloneAxisPaintsEndValueUnitAndDistortionHint(bool overflow)
    {
        Assert.False(new CoordinateAxisCanvas().IsStandalone);
        var visuals = new ReportPrototypeVisuals(ReportPrototypeFixtures.Create(overflow));
        var axis = visuals.Axis.AxisLayout!;
        Assert.Equal("mm", axis.UnitSymbol);
        var end = Assert.Single(axis.Labels, l => l.Role == AxisEndpointRole.End);
        Assert.Equal(overflow ? "7000" : "2000", end.Text);
        using var standalone = RenderBeam(visuals);
        visuals.Axis.IsStandalone = false;
        using var editor = RenderBeam(visuals);
        double axisTop = visuals.Beam.Bounds.Height;
        Assert.True(DifferentPixels(standalone, editor, PreviewBounds(end.Bounds.Translate(new Vector(0, axisTop)))) > 0);
        var unitArea = new Rect(axis.StationLayout.Stations[^1].ScreenX + 8, axisTop, 50, 20);
        Assert.True(InkPixels(standalone, PreviewBounds(unitArea)) > 0);
        if (axis.StationLayout.IsDistorted)
            Assert.True(DifferentPixels(standalone, editor, PreviewBounds(new Rect(0, axisTop + axis.Packing.PaneHeight,
                visuals.Axis.Bounds.Width, axis.LineHeight + 4))) > 0);
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(300f / 72)]
    public void RenderingHonorsPageTransformAndRestoresCanvasState(float scale)
    {
        var visuals = new ReportPrototypeVisuals(ReportPrototypeFixtures.Create(false));
        using var surface = SKSurface.Create(new SKImageInfo(2200, 1800));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Magenta);
        canvas.Translate(17, 23);
        canvas.Scale(scale);
        canvas.ClipRect(new SKRect(10, 15, 480, 380));
        var matrix = canvas.TotalMatrix;
        var clip = canvas.DeviceClipBounds;
        int saves = canvas.SaveCount;
        var bounds = new SKRect(40, 50, 40 + PrototypeReport.LeftWidth, 50 + visuals.Graphics.BeamHeight);
        foreach (var kind in Enum.GetValues<PrototypeGraphic>())
        {
            visuals.Graphics.Draw(canvas, kind, bounds);
            Assert.Equal(matrix, canvas.TotalMatrix);
            Assert.Equal(clip, canvas.DeviceClipBounds);
            Assert.Equal(saves, canvas.SaveCount);
        }
        using var bitmap = new SKBitmap(2200, 1800);
        Assert.True(surface.ReadPixels(bitmap.Info, bitmap.GetPixels(), bitmap.RowBytes, 0, 0));
        var painted = matrix.MapRect(bounds);
        Assert.Equal(SKColors.Magenta, bitmap.GetPixel((int)painted.Left - 2, (int)painted.Top + 10));
        Assert.NotEqual(SKColors.Magenta, bitmap.GetPixel((int)painted.Left + 2, (int)painted.Top + 2));
        Assert.Equal(SKColors.Magenta, bitmap.GetPixel((int)painted.Right + 2, (int)painted.Bottom - 10));
        // A following document element must still land in page coordinates.
        using var paint = new SKPaint { Color = SKColors.Lime };
        canvas.DrawRect(400, 300, 10, 10, paint);
        var next = matrix.MapPoint(405, 305);
        Assert.True(surface.ReadPixels(bitmap.Info, bitmap.GetPixels(), bitmap.RowBytes, 0, 0));
        Assert.Equal(SKColors.Lime, bitmap.GetPixel((int)next.X, (int)next.Y));
    }

    private static SKBitmap RenderBeam(ReportPrototypeVisuals visuals)
    {
        const float scale = 96f / 72;
        var info = new SKImageInfo((int)Math.Ceiling(PrototypeReport.LeftWidth * scale),
            (int)Math.Ceiling(visuals.Graphics.BeamHeight * scale));
        using var surface = SKSurface.Create(info);
        surface.Canvas.Clear(SKColors.White);
        surface.Canvas.Scale(scale);
        visuals.Graphics.Draw(surface.Canvas, PrototypeGraphic.Beam,
            new SKRect(0, 0, PrototypeReport.LeftWidth, visuals.Graphics.BeamHeight));
        var bitmap = new SKBitmap(info);
        Assert.True(surface.ReadPixels(bitmap.Info, bitmap.GetPixels(), bitmap.RowBytes, 0, 0));
        return bitmap;
    }

    private static int DifferentPixels(SKBitmap first, SKBitmap second, Rect area) => CountPixels(first, area,
        (x, y) => first.GetPixel(x, y) != second.GetPixel(x, y));

    private static int InkPixels(SKBitmap bitmap, Rect area) => CountPixels(bitmap, area,
        (x, y) => bitmap.GetPixel(x, y).Red < 200);

    private static Rect PreviewBounds(Rect bounds) => new(bounds.X * ReportPrototypeVisuals.VisualScale,
        bounds.Y * ReportPrototypeVisuals.VisualScale, bounds.Width * ReportPrototypeVisuals.VisualScale,
        bounds.Height * ReportPrototypeVisuals.VisualScale);

    private static int CountPixels(SKBitmap bitmap, Rect area, Func<int, int, bool> include)
    {
        int count = 0;
        for (int y = Math.Max(0, (int)area.Top); y < Math.Min(bitmap.Height, (int)Math.Ceiling(area.Bottom)); y++)
            for (int x = Math.Max(0, (int)area.Left); x < Math.Min(bitmap.Width, (int)Math.Ceiling(area.Right)); x++)
                if (include(x, y)) count++;
        return count;
    }

    private static void WriteDesktopComparisons(ReportPrototypeVisuals visuals, PrototypeReport report, string directory, string name)
    {
        string comparison = Path.Combine(directory, "comparison");
        Directory.CreateDirectory(comparison);
        var controls = new[] { visuals.BeamVisual }.Concat(visuals.Diagrams).ToArray();
        var areas = report.TechnicalAreas(0);
        for (int i = 0; i < controls.Length; i++)
        {
            var control = controls[i];
            var transform = control.RenderTransform;
            control.RenderTransform = new ScaleTransform(ReportPrototypeVisuals.VisualScale, ReportPrototypeVisuals.VisualScale);
            var size = new Size(control.Bounds.Width * ReportPrototypeVisuals.VisualScale,
                control.Bounds.Height * ReportPrototypeVisuals.VisualScale);
            var host = new Canvas { UseLayoutRounding = false, Width = size.Width, Height = size.Height, Children = { control } };
            host.Measure(size); host.Arrange(new Rect(size));
            try
            {
                var pixels = new PixelSize((int)Math.Ceiling(size.Width * 300 / 96),
                    (int)Math.Ceiling(size.Height * 300 / 96));
                // Independent native-control render for review only; never inserted into the PDF.
                using var native = new RenderTargetBitmap(pixels, new Vector(300, 300));
                native.Render(host);
                native.Save(Path.Combine(comparison, $"{name}-desktop-{areas[i].Kind}.png"), PngBitmapEncoderOptions.Default);
            }
            finally { host.Children.Remove(control); control.RenderTransform = transform; }
        }
        var stations = visuals.Beam.Scene!.Frame.Layout.Stations;
        double span = (stations[^1].ScreenX - stations[0].ScreenX) * ReportPrototypeVisuals.PointsPerDip;
        File.WriteAllLines(Path.Combine(comparison, name + "-grafikmasse.txt"), new[]
        {
            $"Maßstab: {ReportPrototypeVisuals.VisualScale:P0}; Messdurchläufe: {visuals.MeasurementPasses}",
            $"Stationsbreite: {span:F3} pt = {span / PrototypeReport.Mm:F3} mm",
            $"Außenränder: {stations[0].ScreenX * ReportPrototypeVisuals.PointsPerDip / PrototypeReport.Mm:F3} / {(visuals.Beam.Bounds.Width - stations[^1].ScreenX) * ReportPrototypeVisuals.PointsPerDip / PrototypeReport.Mm:F3} mm"
        }.Concat(areas.Select(area =>
            $"{area.Kind}: {area.Bounds.Width:F3} × {area.Bounds.Height:F3} pt; {area.Bounds.Width / ReportPrototypeVisuals.PointsPerDip:F3} × {area.Bounds.Height / ReportPrototypeVisuals.PointsPerDip:F3} DIP")));
    }

    private static string RepositoryRoot([CallerFilePath] string source = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "..", ".."));
}
