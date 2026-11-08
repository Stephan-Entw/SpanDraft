using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia;
using SkiaSharp;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Sections.Parametric;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Controls;
using SpanDraft.Desktop.Layout;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Reporting;
using SpanDraft.Desktop.Sections;
using SpanDraft.Desktop.ViewModels;
using SpanDraft.Reporting;
using SpanDraft.Reporting.Notation;
using Xunit;

// Control rendering must stay on the owning Avalonia dispatcher thread.
#pragma warning disable xUnit1031
namespace SpanDraft.Tests;

[Collection("Avalonia")]
public sealed class ReportGraphicsTests : ProjectReportTestSupport
{

    [Theory]
    [InlineData("de-DE", false)] [InlineData("en-US", false)]
    [InlineData("de-DE", true)] [InlineData("en-US", true)]
    public void ISectionReportProducesSquareVectorReviewArtifacts(string culture, bool compactProfile)
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        var state = ReportTestProjects.Create(); var mm = Length.FromMillimeters;
        state = state with { Document = state.Document.WithSection(
            compactProfile ? new ISectionGeometry(mm(100), mm(80), mm(10), mm(12), mm(10))
                : new ISectionGeometry(mm(200), mm(120), mm(5), mm(12), mm(8)), SectionAxisDesignation.Y,
            compactProfile ? "I/H-Profil 100 × 80 × 10 × 12 mm (r = 10 mm)" : null) };
        var (main, _) = Open(state);
        using var report = ProjectReport.Capture(main.Editor!, Now);
        AssertCompleteTablesAndFinalSummary(report);
        var area = report.TechnicalAreas(0).Single(a => a.Kind == ReportGraphic.Section).Bounds;
        Assert.Equal(ReportDocument.RightWidth, area.Width, 4);
        Assert.Equal(area.Width, area.Height, 4); // SKRect subtracts single-precision page coordinates.
        var section = Assert.IsAssignableFrom<IParametricSectionDefinition>(state.Document.Section);
        var geometry = SectionPreviewGeometryResolver.FromSection(SectionEditorType.ISection,
            SectionDefinitionViewModel.PresetValues(section), section);
        var viewport = new Size(area.Width / ReportVisuals.PointsPerDip, area.Height / ReportVisuals.PointsPerDip);
        var layout = SectionPreviewDrawingLayout.Create(geometry, viewport, UnitProfile.Default[QuantityKind.SectionDimension],
            text => SchematicText.Measure(text, AppTypography.Typeface, 12), compact: true);
        Assert.All(layout.Dimensions, d => Assert.True(new Rect(viewport).Contains(d.TextBounds), d.Key));
        Assert.All(layout.Axes, a => Assert.True(new Rect(viewport).Contains(a.LabelBounds), a.Axis.ToString()));
        var y = layout.Axes.Single(a => a.Axis == SectionAxisDesignation.Y);
        Assert.Equal(y.End.X + 10, y.LabelBounds.Center.X, 8);
        Assert.Equal(y.End.Y, y.LabelBounds.Center.Y, 8);
        var flange = layout.Dimensions.Single(d => d.Key == "tf");
        var height = layout.Dimensions.Single(d => d.Key == "h");
        Assert.True(flange.TextBounds.Left > height.Lines[2].Start.X);
        Assert.True(height.TextBounds.Right < height.Lines[2].Start.X);
        Assert.True(flange.TextBounds.Right < flange.Lines[2].Start.X);
        Assert.True(flange.TextBounds.Right < layout.ProfileBounds.Left);
        Assert.All(flange.Arrows, arrow => Assert.True(arrow.Bounds.Left > height.Lines[2].Start.X
            && arrow.Bounds.Right < layout.ProfileBounds.Left));
        Assert.True(flange.TextBounds.Bottom < flange.Arrows.Min(a => a.Tip.Y));
        string directory = Path.Combine(RepositoryRoot(), "output", "pdf", "production");
        Directory.CreateDirectory(directory);
        string name = (compactProfile ? "i-section-100x80-" : "i-section-") + culture;
        File.WriteAllBytes(Path.Combine(directory, name + ".spandraft"), ProjectFileCodec.Serialize(state));
        using var pdf = new MemoryStream(); report.WritePdf(pdf);
        Assert.DoesNotMatch(@"/Subtype\s*/Image\b", Encoding.Latin1.GetString(pdf.ToArray()));
        File.WriteAllBytes(Path.Combine(directory, name + ".pdf"), pdf.ToArray());
        using (var png = File.Create(Path.Combine(directory, name + ".png"))) report.WritePng(png, 0);
        using var bitmap = report.RenderPage(0);
        AssertTextContained(report); AssertInformationBlocksSeparated(report, 0);
        using var detail = new SKBitmap();
        const float scale = 300f / 72;
        Assert.True(bitmap.ExtractSubset(detail, new SKRectI((int)Math.Floor(area.Left * scale),
            (int)Math.Floor(area.Top * scale), (int)Math.Ceiling(area.Right * scale), (int)Math.Ceiling(area.Bottom * scale))));
        using var image = SKImage.FromBitmap(detail);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        using var output = File.Create(Path.Combine(directory, name + "-section.png")); encoded.SaveTo(output);
    }

    [Theory]
    [InlineData("de-DE")] [InlineData("en-US")]
    public void TechnicalNotationSurvivesCaptureWrappingAndVectorOutput(string culture)
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        var fixture = ReportTestProjects.Create(); var mm = Length.FromMillimeters;
        var document = fixture.Document.WithSection(new TSectionGeometry(mm(100), mm(60), mm(4), mm(6), mm(3)), SectionAxisDesignation.Y)
            with { Material = new SpanDraft.Core.Materials.Material("Aluminium", Pressure.FromPascals(70e9),
                Pressure.FromMegapascals(150), SpanDraft.Core.Materials.MaterialStrengthType.Rp02) };
        var state = fixture with { Document = document };
        var (main, _) = Open(state);
        using var report = ProjectReport.Capture(main.Editor!, Now);
        Assert.Contains(report.Data.SectionProperties, text => text.Runs.Any(r => r.Position == TechnicalPosition.Superscript && r.Text == "+"));
        Assert.Contains(report.Data.SectionProperties, text => text.Runs.Any(r => r.Position == TechnicalPosition.Superscript && r.Text == "−"));
        Assert.Contains(report.Data.MaterialProperties, text => text.Runs.Any(r => r.Position == TechnicalPosition.Subscript && r.Text == "p0,2"));
        Assert.Equal(TechnicalSymbols.MaximumStress.Runs, report.Data.Indicators[2].Label.Runs.TakeLast(2));
        string directory = Path.Combine(RepositoryRoot(), "output", "pdf", "production"); Directory.CreateDirectory(directory);
        string name = "technical-notation-" + culture;
        File.WriteAllBytes(Path.Combine(directory, name + ".spandraft"), ProjectFileCodec.Serialize(state));
        using (var pdf = File.Create(Path.Combine(directory, name + ".pdf"))) report.WritePdf(pdf);
        using (var png = File.Create(Path.Combine(directory, name + ".png"))) report.WritePng(png, 0);
        AssertTextContained(report); AssertInformationBlocksSeparated(report, 0);
        Assert.DoesNotMatch(@"/Subtype\s*/Image\b", System.Text.Encoding.Latin1.GetString(File.ReadAllBytes(Path.Combine(directory, name + ".pdf"))));

        var longLabel = string.Concat(Enumerable.Repeat("Long localized description ", 8)) + TechnicalSymbols.MaximumStress;
        using var wrapped = new ReportDocument(report.Data with
            { Indicators = [new(longLabel, "150 MPa", null)] }, new(80, (_, _, _) => { }));
        int summaryPage = wrapped.Pages.Select((page, index) => (page, index)).Single(p => p.page.SummaryTop.HasValue).index;
        using var bitmap = wrapped.RenderPage(summaryPage);
        AssertTextContained(wrapped);
        Assert.Contains(wrapped.TextRuns, r => r.Text.Contains(TechnicalSymbols.MaximumStress.PlainText));
    }

    [Theory]
    [InlineData(false, "de-DE")]
    [InlineData(true, "de-DE")]
    [InlineData(false, "en-US")]
    [InlineData(true, "en-US")]
    public void RealOpenedProjectsProduceReviewArtifactsAndVectorPdf(bool overflow, string language)
    {
        CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(language);
        string root = RepositoryRoot(), directory = Path.Combine(root, "output", "pdf", "production");
        Directory.CreateDirectory(directory);
        string name = (overflow ? "mehrseitig-" : "normal-") + language;
        string projectPath = Path.Combine(directory, name + ".spandraft");
        File.WriteAllBytes(projectPath, ProjectFileCodec.Serialize(ReportTestProjects.Create(overflow)));
        var main = new MainWindowViewModel();
        Assert.True(main.OpenAsync(projectPath).GetAwaiter().GetResult());
        var bytes = File.ReadAllBytes(projectPath); var revision = main.Session!.CurrentRevision;
        using var report = ProjectReport.Capture(main.Editor!, Now);
        Assert.Equal(overflow ? 2 : 1, report.Pages.Count);
        using (var pdf = File.Create(Path.Combine(directory, name + ".pdf"))) report.WritePdf(pdf);
        string pdfText = Encoding.Latin1.GetString(File.ReadAllBytes(Path.Combine(directory, name + ".pdf")));
        Assert.DoesNotMatch(@"/Subtype\s*/Image\b", pdfText);
        Assert.Equal(report.Pages.Count, Regex.Matches(pdfText, @"/Type\s*/Page\b").Count);
        var mediaBoxes = Regex.Matches(pdfText, @"/MediaBox\s*\[([^]]+)\]");
        Assert.Equal(report.Pages.Count, mediaBoxes.Count);
        Assert.All(mediaBoxes.Cast<Match>(), box =>
            Assert.Equal(new[] { "0", "0", "595", "842" }, box.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)));
        Assert.True(report.Pages[0].HasGraphics);
        Assert.All(report.Pages.Skip(1), page => Assert.False(page.HasGraphics));
        Assert.Equal(!overflow, report.Pages[0].SummaryTop.HasValue);
        Assert.Single(report.Pages, page => page.SummaryTop.HasValue);
        for (int table = 0; table < report.Data.Tables.Count; table++)
            Assert.Equal(Enumerable.Range(0, report.Data.Tables[table].Rows.Count), report.Pages.SelectMany(p => p.Tables)
                .Where(f => f.Table == table).SelectMany(f => f.Rows).Select(r => r.Row));
        var areas = report.TechnicalAreas(0);
        Assert.Equal(Enum.GetValues<ReportGraphic>(), areas.Select(a => a.Kind));
        Assert.All(areas.Take(4), a => Assert.Equal(128 * ReportDocument.Mm, a.Bounds.Width, 3));
        Assert.Equal(50 * ReportDocument.Mm, areas[^1].Bounds.Width, 3);
        Assert.Equal(areas[^1].Bounds.Width, areas[^1].Bounds.Height, 4);
        Assert.All(areas.Take(4).Zip(areas.Skip(1).Take(3)), pair => Assert.True(pair.First.Bounds.Bottom <= pair.Second.Bounds.Top));
        Assert.True(areas[3].Bounds.Bottom < (report.Pages[0].SummaryTop ?? ReportDocument.FooterTop));
        for (int page = 0; page < report.Pages.Count; page++)
        {
            using var png = File.Create(Path.Combine(directory, $"{name}-{page + 1}.png")); report.WritePng(png, page);
            AssertTextContained(report);
            var heading = Assert.Single(report.TextRuns, r => r.Text == report.Data.Labels.Heading
                + (page > 0 ? " · " + report.Data.Labels.Continuation : ""));
            Assert.Equal(ReportDocument.PageWidth / 2, heading.Bounds.MidX, 3);
            Assert.Contains(report.TextRuns, r => r.Text == string.Format(CultureInfo.InvariantCulture,
                report.Data.Labels.PageFormat, page + 1, report.Pages.Count) && r.Bounds.Top > ReportDocument.FooterTop);
            Assert.Contains(report.TextRuns, r => r.Text == report.Data.Date && r.Bounds.Top > ReportDocument.FooterTop);
            Assert.Contains(report.TextRuns, r => r.Text == "SpanDraft " + report.Data.Version && r.Bounds.Top > ReportDocument.FooterTop);
            AssertInformationBlocksSeparated(report, page);
        }
        using var bitmap = report.RenderPage(0);
        Assert.Equal(2480, bitmap.Width); Assert.Equal(3508, bitmap.Height);
        Assert.Same(revision, main.Session.CurrentRevision); Assert.False(main.Session.IsDirty);
        Assert.Equal(bytes, File.ReadAllBytes(projectPath));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void TechnicalControlsUseCommittedOffsetsAndSharedStations(bool overflow)
    {
        var state = ReportTestProjects.Create(overflow); var (main, _) = Open(state);
        main.Editor!.PreviewAnnotationOffset(state.Document.Loads[0].Id, new(77, 66));
        using var report = ProjectReport.Capture(main.Editor, Now);
        var visuals = new ReportVisuals(state, main.Editor.Presentation, report.Data);
        var scene = visuals.Beam.Scene!;
        var reserved = BeamRenderState.Create(state.Document, scene.Frame,
            text => SchematicText.Measure(text, AppTypography.Typeface, visuals.Beam.LabelFontSize),
            presentation: state.Presentation, profile: main.Editor.Presentation.Options.Profile, loadSymbolScale: .85,
            minimumPaneHeight: ReportVisuals.MinimumBeamPaneHeight);
        Assert.True(scene.MinimumPaneHeight < reserved.MinimumPaneHeight);
        Assert.True(visuals.Beam.Height >= scene.MinimumPaneHeight);
        var label = Assert.Single(visuals.Beam.Scene!.Annotations, a => a.Id == state.Document.Loads[0].Id);
        Assert.True(label.IsManual); Assert.Equal(label.AutoBounds.Translate(new Vector(18, 12)), label.Bounds);
        Assert.Null(visuals.Beam.HighlightedLoadId); Assert.Null(visuals.Beam.HighlightedDistributedLoadId);
        Assert.Null(visuals.Beam.HighlightedSupportId); Assert.Null(visuals.Beam.ConflictEntityIds); Assert.Null(visuals.Beam.ConstraintConflict);
        Assert.False(visuals.Beam.IsHitTestVisible); Assert.False(visuals.Axis.IsHitTestVisible);
        Assert.All(visuals.Beam.Scene.Annotations, a =>
        { Assert.False(a.IsPreview); Assert.False(a.IsInvalid); Assert.True(new Rect(visuals.Beam.Bounds.Size).Contains(a.Bounds)); });
        Assert.All(visuals.Beam.Scene.Supports, s => Assert.False(s.IsPreview));
        Assert.All(visuals.Beam.Scene.Loads, l => { Assert.False(l.IsPreview); Assert.Equal(.85, l.SymbolScale); });
        Assert.All(visuals.Beam.Scene.DistributedLoads, l => { Assert.False(l.IsPreview); Assert.Equal(.85, l.SymbolScale); });
        Assert.Equal(.8, ReportVisuals.VisualScale); Assert.Equal(.75, visuals.Beam.SupportSymbolScale);
        Assert.Equal(7, visuals.Beam.LabelFontSize * ReportVisuals.PointsPerDip, 10);
        Assert.Equal(visuals.Beam.LabelFontSize, visuals.Axis.LabelFontSize);
        var stations = visuals.Beam.Scene.Frame.Layout;
        Assert.Same(stations, visuals.Axis.AxisLayout!.StationLayout);
        Assert.All(visuals.Axis.AxisLayout.Labels, a => Assert.True(new Rect(visuals.Axis.Bounds.Size).Contains(a.Bounds)));
        Assert.True(stations.Stations[0].ScreenX >= stations.Stations[0].LeftExtent + SchematicMetrics.OuterPadding);
        Assert.True(stations.Stations[^1].ScreenX + stations.Stations[^1].RightExtent <= visuals.Beam.Bounds.Width - SchematicMetrics.OuterPadding);
        for (int diagram = 0; diagram < visuals.Diagrams.Length; diagram++)
        {
            var control = visuals.Diagrams[diagram];
            Assert.False(control.IsHitTestVisible);
            Assert.Same(main.Editor.Presentation.Result, control.Presentation!.Result); Assert.Same(stations, control.StationLayout);
            var projection = Assert.IsType<ResultDiagramProjection>(control.Projection);
            Assert.Same(stations, projection.StationLayout); Assert.NotEmpty(projection.Sections);
            foreach (var point in projection.Sections.SelectMany(s => s))
            {
                var actual = main.Editor.Presentation.Result!.Solution.EvaluateAt(point.Position, point.Side);
                double si = diagram switch { 0 => actual.TransverseDisplacement.Meters, 1 => actual.ShearForce.Newtons, _ => actual.BendingMoment.NewtonMeters };
                Assert.Equal(si, point.SiValue);
                Assert.Equal(stations.Transform.PhysicalToScreen(point.Position.Meters), point.Screen.X, 6);
            }
            Assert.Equal(projection.Markers.Count, control.MarkerLabels.Count);
            Assert.All(control.MarkerLabels, marker => Assert.True(new Rect(control.Bounds.Size).Contains(marker.Bounds)));
            Assert.All(projection.Ticks, tick => Assert.True(SchematicText.Measure(control.TickLabel(tick), AppTypography.Typeface, 12).Width + 16 <= stations.Stations[0].ScreenX + 1e-6));
            Assert.Contains("[" + projection.Unit.Symbol + "]", control.Title);
            Assert.Equal(report.TechnicalAreas(0)[diagram + 1].Bounds.Height / ReportVisuals.PointsPerDip, control.Bounds.Height, 5);
        }
        var automatic = state with { Presentation = new() };
        var (automaticMain, _) = Open(automatic);
        using var automaticReport = ProjectReport.Capture(automaticMain.Editor!, Now);
        using var automaticImage = automaticReport.RenderPage(0, 96);
        using var manualImage = report.RenderPage(0, 96);
        Assert.False(automaticImage.Bytes.SequenceEqual(manualImage.Bytes));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void StandaloneAxisPaintsEndValueUnitAndDistortionHint(bool overflow)
    {
        Assert.False(new CoordinateAxisCanvas().IsStandalone);
        var state = ReportTestProjects.Create(overflow); var (main, _) = Open(state);
        using var report = ProjectReport.Capture(main.Editor!, Now);
        var visuals = new ReportVisuals(state, main.Editor!.Presentation, report.Data);
        var axis = visuals.Axis.AxisLayout!;
        Assert.True(visuals.Axis.IsStandalone); Assert.Equal("mm", axis.UnitSymbol);
        var end = Assert.Single(axis.Labels, l => l.Role == AxisEndpointRole.End);
        Assert.Equal(overflow ? "7\u202f000" : "2\u202f000", end.Text);
        using var standalone = RenderBeam(visuals);
        visuals.Axis.IsStandalone = false;
        using var editor = RenderBeam(visuals);
        double axisTop = visuals.Beam.Bounds.Height;
        Rect Pixels(Rect bounds) => new(bounds.X * ReportVisuals.VisualScale, bounds.Y * ReportVisuals.VisualScale,
            bounds.Width * ReportVisuals.VisualScale, bounds.Height * ReportVisuals.VisualScale);
        Assert.True(CountPixels(standalone, Pixels(end.Bounds.Translate(new Vector(0, axisTop))),
            (x, y) => standalone.GetPixel(x, y) != editor.GetPixel(x, y)) > 0);
        var unitArea = new Rect(axis.StationLayout.Stations[^1].ScreenX + 8, axisTop, 50, 20);
        Assert.True(CountPixels(standalone, Pixels(unitArea), (x, y) => standalone.GetPixel(x, y).Red < 200) > 0);
        if (axis.StationLayout.IsDistorted)
            Assert.True(CountPixels(standalone, Pixels(new Rect(0, axisTop + axis.Packing.PaneHeight,
                visuals.Axis.Bounds.Width, axis.LineHeight + 4)),
                (x, y) => standalone.GetPixel(x, y) != editor.GetPixel(x, y)) > 0);
    }

    [Theory]
    [InlineData(1f, false)] [InlineData(300f / 72, false)]
    [InlineData(1f, true)] [InlineData(300f / 72, true)]
    public void LiveAndFrozenGraphicsHonorPageTransformClipAndCanvasState(float scale, bool frozen)
    {
        var state = ReportTestProjects.Create(); var (main, _) = Open(state);
        using var captured = ProjectReport.Capture(main.Editor!, Now);
        var visuals = new ReportVisuals(state, main.Editor!.Presentation, captured.Data);
        using var report = new ReportDocument(captured.Data, visuals.Graphics);
        if (frozen) visuals.Freeze(report);
        foreach (var area in report.TechnicalAreas(0))
        {
            using var surface = SKSurface.Create(new SKImageInfo(2200, 1800));
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.Magenta); canvas.Translate(17, 23); canvas.Scale(scale);
            canvas.ClipRect(new SKRect(10, 15, 300, 190));
            var matrix = canvas.TotalMatrix; var clip = canvas.DeviceClipBounds; int saves = canvas.SaveCount;
            var bounds = SKRect.Create(40, 50, area.Bounds.Width, area.Bounds.Height);
            visuals.Graphics.Draw(canvas, area.Kind, bounds);
            Assert.Equal(matrix, canvas.TotalMatrix); Assert.Equal(clip, canvas.DeviceClipBounds); Assert.Equal(saves, canvas.SaveCount);
            using var bitmap = new SKBitmap(2200, 1800);
            Assert.True(surface.ReadPixels(bitmap.Info, bitmap.GetPixels(), bitmap.RowBytes, 0, 0));
            var painted = matrix.MapRect(bounds);
            Assert.True(CountPixels(bitmap, new Rect(painted.Left, painted.Top, painted.Width, painted.Height),
                (x, y) => bitmap.GetPixel(x, y) != SKColors.Magenta) > 0, area.Kind.ToString());
            Assert.Equal(0, CountPixels(bitmap, new Rect(0, 0, bitmap.Width, bitmap.Height), (x, y) =>
                (x < Math.Max(painted.Left, clip.Left) - 1 || x > Math.Min(painted.Right, clip.Right) + 1
                 || y < Math.Max(painted.Top, clip.Top) - 1 || y > Math.Min(painted.Bottom, clip.Bottom) + 1)
                && bitmap.GetPixel(x, y) != SKColors.Magenta));
            // A following document element must still use the caller's coordinates and clip.
            using var paint = new SKPaint { Color = SKColors.Lime };
            canvas.DrawRect(250, 160, 10, 10, paint);
            var next = matrix.MapPoint(255, 165);
            Assert.True(surface.ReadPixels(bitmap.Info, bitmap.GetPixels(), bitmap.RowBytes, 0, 0));
            Assert.Equal(SKColors.Lime, bitmap.GetPixel((int)next.X, (int)next.Y));
        }
    }
}
