using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SkiaSharp;
using SpanDraft.Analysis;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Sections.Parametric;
using SpanDraft.Core.Units;
using SpanDraft.Desktop;
using SpanDraft.Desktop.Controls;
using SpanDraft.Desktop.Layout;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Reporting;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;
using SpanDraft.Reporting;
using Xunit;

// Control rendering must stay on the owning Avalonia dispatcher thread.
#pragma warning disable xUnit1031
namespace SpanDraft.Tests;

[Collection("Schematic text")]
public sealed class ProjectReportTests : IDisposable
{
    private readonly CultureInfo originalCulture = CultureInfo.CurrentCulture, originalUi = CultureInfo.CurrentUICulture;
    private readonly DesktopControlEnvironment environment;
    public ProjectReportTests()
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");
        environment = new();
        Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
    }
    public void Dispose() { environment.Dispose(); CultureInfo.CurrentCulture = originalCulture; CultureInfo.CurrentUICulture = originalUi; }
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 12, 34, 56, TimeSpan.FromHours(2));

    private static (MainWindowViewModel Main, Files Files) Open(ProjectState state,
        ResultPresentationOptions? presentation = null, Action? analyzed = null, Func<EditorViewModel, Task>? show = null)
    {
        var files = new Files();
        string path = Path.GetFullPath("report-input.spandraft"); files.Data[path] = ProjectFileCodec.Serialize(state);
        var main = new MainWindowViewModel(files: files, settings: UserSettings.Default with
            { Profile = presentation?.Profile ?? UnitProfile.Default, Mode = presentation?.Mode ?? PresentationMode.Standard },
            analyze: beam => { analyzed?.Invoke(); return BeamAnalysis.Analyze(beam); }, showReport: show);
        Assert.True(main.OpenAsync(path).GetAwaiter().GetResult());
        return (main, files);
    }

    [Theory]
    [InlineData("de-DE", "de-DE", 0, false)]
    [InlineData("en-US", "en-US", 1, false)]
    [InlineData("en-US", "de-DE", 2, true)]
    [InlineData("de-DE", "en-US", 3, true)]
    public void CapturesExistingResultsUnitsCultureAndConfirmedStateWithoutAnalysisOrMutation(string ui, string culture, int units, bool detailed)
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(ui); CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        var profile = units switch
        {
            0 => UnitProfile.MechanicalEngineering, 1 => UnitProfile.StructuralEngineering,
            2 => UnitProfile.UnitedStates, _ => UnitProfile.MechanicalEngineering.WithUnit(QuantityKind.BeamLength, UnitCatalog.Meter)
        };
        int analyses = 0;
        var (main, files) = Open(ReportTestProjects.Create(), new(profile, detailed ? PresentationMode.Detailed : PresentationMode.Standard), () => analyses++);
        var session = main.Session!; var revision = session.CurrentRevision; var original = files.Data[session.FilePath!].ToArray();
        var editor = main.Editor!;
        editor.PreviewAnnotationOffset(editor.Document.Loads[0].Id, new(99, 88));
        using var report = ProjectReport.Capture(editor, Now);
        Assert.Equal(1, analyses);
        Assert.Equal(editor.Presentation.Displacement, report.Data.Indicators[0].Value);
        Assert.Equal(editor.Presentation.Moment, report.Data.Indicators[1].Value);
        Assert.Equal(editor.Presentation.Stress, report.Data.Indicators[2].Value);
        Assert.Equal(editor.Presentation.SafetyFactor, report.Data.Indicators[3].Value);
        Assert.Equal(Now, report.Data.CreatedAt);
        Assert.Equal(Now.ToLocalTime().ToString("d", CultureInfo.CurrentCulture), report.Data.Date);
        Assert.Contains(profile[QuantityKind.BeamLength].Symbol, report.Data.Tables[0].Headers[2]);
        Assert.Contains(profile[QuantityKind.TransverseForce].Symbol, report.Data.Tables[2].Headers[2]);
        using var before = new MemoryStream(); report.WritePng(before, 0);
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture == "de-DE" ? "en-US" : "de-DE");
        Assert.True(main.SetResultPresentation(UnitProfile.UnitedStates, PresentationMode.Detailed));
        using var after = new MemoryStream(); report.WritePng(after, 0);
        Assert.Equal(before.ToArray(), after.ToArray());
        using var backgroundPdf = new MemoryStream();
        Task.Run(() => report.WritePdf(backgroundPdf), TestContext.Current.CancellationToken).GetAwaiter().GetResult();
        Assert.True(backgroundPdf.Length > 0);
        Assert.Same(revision, session.CurrentRevision); Assert.False(session.IsDirty); Assert.False(session.CanUndo);
        Assert.False(session.CanRedo); Assert.Empty(files.Writes); Assert.Equal(original, files.Data[session.FilePath!]); Assert.Equal(1, analyses);
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
            Assert.All(projection.Ticks, tick => Assert.True(SchematicText.Measure(control.TickLabel(tick), Typeface.Default, 12).Width + 16 <= stations.Stations[0].ScreenX + 1e-6));
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

    [Fact]
    public void StandardProjectRetainsBalancedReactionsAndEngineeringReferenceValues()
    {
        var (main, _) = Open(ReportTestProjects.Create());
        using var report = ProjectReport.Capture(main.Editor!, Now);
        var result = main.Editor!.Presentation.Result!;
        SolverTestSupport.ForceClose(1000, result.Solution.Nodes[0].ReactionY);
        SolverTestSupport.ForceClose(800, result.Solution.Nodes[^1].ReactionY);
        SolverTestSupport.Close(710, result.Engineering.BendingMomentMagnitude.NewtonMeters, 1e-7);
        Assert.Equal(new[] { "0,78 mm", "710 N·m", "23 MPa", "10" }, report.Data.Indicators.Select(i => i.Value));
    }

    [Fact]
    public void MissingOptionalProjectInformationLeavesNoEmptyHeaderRows()
    {
        var state = ReportTestProjects.Create();
        var (main, _) = Open(state); using var full = ProjectReport.Capture(main.Editor!, Now);
        var (compactMain, _) = Open(state with { Info = new(state.Info!.CalculationTitle, null, null, state.Info.Author) });
        using var compact = ProjectReport.Capture(compactMain.Editor!, Now);
        Assert.Equal(full.Data.Metadata.Count - 2, compact.Data.Metadata.Count);
        Assert.All(compact.Data.Metadata, row => Assert.False(string.IsNullOrWhiteSpace(row.Value)));
        Assert.True(compact.Pages[0].BodyTop < full.Pages[0].BodyTop);
        Assert.DoesNotContain(compact.Data.Metadata, row => row.Value is "Neutrales Beispiel" or "BEISPIEL-01");
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
        Assert.Equal(overflow ? "7000" : "2000", end.Text);
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

    [Fact]
    public void EmptyTablesAndManyRowsPaginateWithNoLostContentOrFooterOverlap()
    {
        var (main, _) = Open(ReportTestProjects.Create(true)); using var source = ProjectReport.Capture(main.Editor!, Now);
        var tables = source.Data.Tables.Select(t => t with { Rows = Enumerable.Range(0, 15).SelectMany(_ => t.Rows).ToArray() }).ToArray();
        using var report = new ReportDocument(source.Data with { Tables = [tables[0], new("Empty", ["Name"], [1], []), tables[1], tables[2]] },
            new(190, (_, _, _) => { }));
        Assert.True(report.Pages.Count > 2);
        for (int table = 0; table < report.Data.Tables.Count; table++)
        {
            var rows = report.Pages.SelectMany(p => p.Tables).Where(f => f.Table == table).SelectMany(f => f.Rows).Select(r => r.Row);
            Assert.Equal(Enumerable.Range(0, report.Data.Tables[table].Rows.Count), rows);
        }
        Assert.Single(report.Pages, p => p.SummaryTop is not null);
        for (int page = 0; page < report.Pages.Count; page++)
        { using var bitmap = report.RenderPage(page, 96); AssertTextContained(report); AssertTableHeadersAndBounds(report, page); }
    }

    [Theory]
    [InlineData(80)] [InlineData(150)]
    public void RealProjectWithManyLoadsCanProduceMoreThanTwoPages(int count)
    {
        var state = ReportTestProjects.Create(true);
        var loads = Enumerable.Range(0, count).Select(i => new EditorPointForce(Guid.NewGuid(),
            Length.FromMeters(.05 + i * 6.9 / (count - 1)), Force.FromNewtons(-100), "F" + (i + 1)));
        state = state with { Document = state.Document.WithLoads(loads), Presentation = new() };
        var (main, _) = Open(state); using var report = ProjectReport.Capture(main.Editor!, Now);
        Assert.True(report.Pages.Count > 2);
        Assert.Equal(count + 2, report.Data.Tables[1].Rows.Count);
        Assert.Equal(count + 2, report.Pages.SelectMany(p => p.Tables).Where(t => t.Table == 1).Sum(t => t.Rows.Count));
        string directory = Path.Combine(RepositoryRoot(), "output", "pdf", "production"); Directory.CreateDirectory(directory);
        using (var pdf = File.Create(Path.Combine(directory, $"viele-lasten-{count}.pdf"))) report.WritePdf(pdf);
        for (int i = 0; i < report.Pages.Count; i++)
        {
            using var png = File.Create(Path.Combine(directory, $"viele-lasten-{count}-{i + 1}.png")); report.WritePng(png, i);
            AssertTextContained(report);
        }
    }

    [Fact]
    public void LongTextAndOversizedRowsAreWrappedAndCompletelyConsumed()
    {
        var (main, _) = Open(ReportTestProjects.Create()); using var source = ProjectReport.Capture(main.Editor!, Now);
        string name = string.Concat(Enumerable.Repeat("UngetrennterSehrLangerName", 200));
        var data = source.Data with
        {
            Metadata = [("Titel", string.Join(" ", Enumerable.Repeat("Sehr langer Berechnungstitel", 80)))],
            Tables = [new("Long table", ["Name", "Value"], [.5f, .5f], [new[] { name, "42" }, new[] { "Last row", "7" }])]
        };
        using var report = new ReportDocument(data, new(130, (_, _, _) => { }));
        var slices = report.Pages.SelectMany(p => p.Tables).SelectMany(f => f.Rows).Where(r => r.Row == 0).ToArray();
        Assert.True(slices.Length > 1);
        Assert.Equal(name, string.Concat(slices.SelectMany(r => r.Cells[0].Skip(r.FirstLine).Take(r.LineCount))));
        for (int page = 0; page < report.Pages.Count; page++)
        { using var bitmap = report.RenderPage(page, 96); AssertTextContained(report); }
    }

    [Fact]
    public void AllSectionFormsAndAxesUseAuthoritativeProperties()
    {
        var fixture = ReportTestProjects.Create(); var mm = Length.FromMillimeters;
        ISectionDefinition[] sections = [new RectangleSectionGeometry(mm(60), mm(100)),
            new RectangularHollowSectionGeometry(mm(60), mm(100), mm(4), mm(6)), new CircleSectionGeometry(mm(50)),
            new CircularHollowSectionGeometry(mm(60), mm(4)), new ISectionGeometry(mm(100), mm(60), mm(4), mm(6), mm(3)),
            new USectionGeometry(mm(100), mm(60), mm(4), mm(6), mm(3)), new TSectionGeometry(mm(100), mm(60), mm(4), mm(6), mm(3)),
            new AngleSectionGeometry(mm(60), mm(100), mm(6), mm(3)),
            new ManualSectionDefinition(Area.FromSquareMeters(.001), new(SectionAxisDesignation.U, SecondMomentOfArea.FromMetersToTheFourth(1e-6), SectionModulus.FromCubicMeters(2e-5)))];
        foreach (var section in sections)
        foreach (var axis in section.Axes)
        {
            var state = fixture with { Document = fixture.Document.WithSection(section, axis.AxisDesignation) };
            var (main, _) = Open(state); using var report = ProjectReport.Capture(main.Editor!, Now);
            Assert.Contains(report.Data.SectionProperties, p => p.StartsWith("I" + axis.AxisDesignation.ToString().ToLowerInvariant() + " = "));
            bool sameDisplay = QuantityFormatter.Format(axis.PositiveSectionModulus.CubicMeters, QuantityKind.SectionModulus,
                main.Editor!.ResultPresentation.Profile, references: main.Editor.Presentation.References) ==
                QuantityFormatter.Format(axis.NegativeSectionModulus.CubicMeters, QuantityKind.SectionModulus,
                main.Editor.ResultPresentation.Profile, references: main.Editor.Presentation.References);
            Assert.Equal(sameDisplay ? 2 : 3, report.Data.SectionProperties.Count);
            using var bitmap = report.RenderPage(0, 96); AssertTextContained(report);
        }
    }

    [Fact]
    public void LegacyAndUnloadedProjectsNeedNoMetadataOrInventedResults()
    {
        var state = ReportTestProjects.Create(); state = state with { Info = null, Document = state.Document.WithLoads([]).WithDistributedLoads([]) };
        state = state with { Presentation = state.Presentation.RetainEntities(state.Document) };
        var (main, _) = Open(state); using var report = ProjectReport.Capture(main.Editor!, Now);
        Assert.Empty(report.Data.Metadata); Assert.Empty(report.Data.Author);
        Assert.Contains("∞", report.Data.Indicators[3].Value);
        Assert.Empty(report.Data.Tables[1].Rows);
        using var bitmap = report.RenderPage(0, 96); AssertTextContained(report);
    }

    [Fact]
    public void ShellGuardsReportAndPreservesDirtyStateAndDraftsThroughoutCallback()
    {
        var state = ReportTestProjects.Create(); int calls = 0;
        var (main, files) = Open(state, show: editor =>
        {
            calls++; Assert.True(editor.IsBusy); Assert.True(editor.PreserveDrafts);
            editor.DimensionLength.LoseFocus();
            Assert.True(editor.DimensionLength.IsEditing); Assert.Equal("9000", editor.DimensionLength.Text);
            using var report = ProjectReport.Capture(editor, Now); return Task.CompletedTask;
        });
        main.Editor!.SetAnnotationOffset(state.Document.Loads[0].Id, new(20, 10));
        main.Editor.DimensionLength.Begin(); main.Editor.DimensionLength.Text = "9000";
        var revision = main.Session!.CurrentRevision; Assert.True(main.Session.IsDirty);
        Assert.True(main.ReportAsync().GetAwaiter().GetResult()); Assert.Equal(1, calls);
        Assert.Same(revision, main.Session.CurrentRevision); Assert.True(main.Session.IsDirty); Assert.Empty(files.Writes);
        Assert.Equal(state.Document.Length, main.Editor.Document.Length);
        Assert.True(main.Editor.DimensionLength.IsEditing); Assert.Equal("9000", main.Editor.DimensionLength.Text);
        var unavailable = state with { Document = state.Document.WithSupports([]) };
        var (invalid, _) = Open(unavailable); Assert.False(invalid.CanReport); Assert.False(invalid.ReportAsync().GetAwaiter().GetResult());
        Assert.Throws<InvalidOperationException>(() => ProjectReport.Capture(invalid.Editor!, Now));
    }

    [Fact]
    public void PreviewNavigationAndPdfExportDoNotTouchProjectAndProtectProjectTargets()
    {
        var (main, files) = Open(ReportTestProjects.Create(true)); using var report = ProjectReport.Capture(main.Editor!, Now);
        var window = new ReportPreviewWindow(report, main.Session!.FilePath, files);
        window.ShowPage(1);
        Assert.Equal("Seite 2 von 2", window.FindControl<TextBlock>("PageLabel")!.Text);
        Assert.False(window.FindControl<Button>("NextButton")!.IsEnabled);
        string destination = Path.GetFullPath("export.pdf");
        window.SavePdfAsync(destination).GetAwaiter().GetResult(); Assert.True(files.Data.ContainsKey(destination));
        Assert.Equal(new[] { destination }, files.Writes);
        Assert.Throws<IOException>(() => window.SavePdfAsync(main.Session.FilePath!).GetAwaiter().GetResult());
        Assert.Throws<IOException>(() => window.SavePdfAsync(Path.GetFullPath("export.txt")).GetAwaiter().GetResult());
        Assert.Equal(new[] { destination }, files.Writes);
        byte[] exported = files.Data[destination].ToArray(); files.FailWrites = true;
        Assert.Throws<IOException>(() => window.SavePdfAsync(destination).GetAwaiter().GetResult());
        Assert.Equal(exported, files.Data[destination]);
        Assert.False(main.Session.IsDirty); window.Close();
    }

    [Theory]
    [InlineData("de-DE", 740, 520)] [InlineData("en-US", 960, 820)]
    public void PreviewControlsFitAndRenderAtMinimumAndNormalWindowSizes(string language, int width, int height)
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
        var (main, files) = Open(ReportTestProjects.Create(true)); using var report = ProjectReport.Capture(main.Editor!, Now);
        var window = new ReportPreviewWindow(report, main.Session!.FilePath, files) { Width = width, Height = height, WindowStartupLocation = WindowStartupLocation.Manual };
        window.Show();
        window.Measure(new(width, height)); window.Arrange(new Rect(0, 0, width, height)); Dispatcher.UIThread.RunJobs();
        var buttons = new[] { "PreviousButton", "NextButton", "SavePdfButton", "PrintButton", "CloseButton" }
            .Select(n => window.FindControl<Button>(n)!).ToArray();
        Assert.All(buttons, button =>
        {
            var top = button.TranslatePoint(default, window)!.Value;
            Assert.True(new Rect(0, 0, width, height).Contains(new Rect(top, button.Bounds.Size)), button.Name);
        });
        var save = buttons[2]; var zoom = window.FindControl<ComboBox>("ZoomPicker")!;
        Assert.True(zoom.TranslatePoint(new(zoom.Bounds.Width, 0), window)!.Value.X
            <= save.TranslatePoint(default, window)!.Value.X);
        Assert.True(window.FindControl<Image>("PageImage")!.Bounds.Height <= window.FindControl<ScrollViewer>("PageScroll")!.Bounds.Height);
        Assert.Equal(OperatingSystem.IsWindows(), buttons[3].IsEnabled);
        string directory = Path.Combine(RepositoryRoot(), "output", "pdf", "production"); Directory.CreateDirectory(directory);
        using var image = new RenderTargetBitmap(new(width, height), new Vector(96, 96));
        image.Render(window);
        image.Save(Path.Combine(directory, $"preview-{language}.png"), PngBitmapEncoderOptions.Default);
        window.Close();
    }

    [Fact]
    public void LongMaterialAndSectionNamesStayCompleteWithoutOverlappingOtherBlocks()
    {
        var (main, _) = Open(ReportTestProjects.Create()); using var original = ProjectReport.Capture(main.Editor!, Now);
        string name = string.Join(" ", Enumerable.Repeat("Very long custom library name", 150));
        using var report = new ReportDocument(original.Data with { Section = name, Material = name }, new(130, (_, _, _) => { }));
        Assert.Contains(report.Data.Metadata, m => m.Label == report.Data.Labels.Section && m.Value == name);
        Assert.Contains(report.Data.Metadata, m => m.Label == report.Data.Labels.Material && m.Value == name);
        Assert.True(report.Pages.Count > 1);
        for (int page = 0; page < report.Pages.Count; page++)
        { using var bitmap = report.RenderPage(page, 96); AssertTextContained(report); }
    }

    [Theory]
    [InlineData(2400, 3200, 300, 300)] [InlineData(4800, 3200, 600, 300)] [InlineData(3200, 2400, 300, 300)]
    public void PrintPlacementFitsAndPreservesPhysicalAspect(int width, int height, int dpiX, int dpiY)
    {
        var bounds = WindowsReportPrinter.Destination(width, height, dpiX, dpiY);
        Assert.True(bounds.Left >= 0 && bounds.Top >= 0 && bounds.Right <= width && bounds.Bottom <= height);
        Assert.Equal(ReportDocument.PageWidth / ReportDocument.PageHeight, bounds.Width / dpiX / (bounds.Height / dpiY), 3);
    }

    private static void AssertInformationBlocksSeparated(ReportDocument report, int page)
    {
        AssertTableHeadersAndBounds(report, page);
        if (!report.Pages[page].HasGraphics) return;
        var sectionArea = report.TechnicalAreas(page).Single(a => a.Kind == ReportGraphic.Section).Bounds;
        var sectionProperties = Assert.Single(report.TextRuns, r => r.Text.Contains(report.Data.SectionProperties[0]));
        var materialProperties = Assert.Single(report.TextRuns, r => r.Text.Contains(report.Data.MaterialProperties[0]));
        Assert.All(report.Data.SectionProperties, p => Assert.Contains(p, sectionProperties.Text));
        Assert.All(report.Data.MaterialProperties, p => Assert.Contains(p, materialProperties.Text));
        var materialHeading = Assert.Single(report.TextRuns, r => r.Text == report.Data.Labels.Material);
        Assert.True(sectionArea.Bottom <= sectionProperties.Bounds.Top);
        Assert.True(sectionProperties.Bounds.Bottom < materialHeading.Bounds.Top);
        Assert.True(materialHeading.Bounds.Bottom < materialProperties.Bounds.Top);
        foreach (var run in new[] { sectionProperties, materialHeading, materialProperties })
        {
            Assert.True(run.Bounds.Left >= sectionArea.Left);
            Assert.True(run.Bounds.Right <= sectionArea.Right + .01f);
        }
        var tables = report.Pages[page].Tables;
        if (tables.Count > 0) Assert.True(materialProperties.Bounds.Bottom < tables[0].Bounds.Top);
    }

    private static void AssertTableHeadersAndBounds(ReportDocument report, int page)
    {
        var fragments = report.Pages[page].Tables;
        foreach (var fragment in fragments)
        {
            Assert.NotEmpty(fragment.Rows);
            Assert.True(fragment.HeadingHeight > 0 && fragment.Bounds.Height > fragment.HeadingHeight);
            Assert.True(fragment.Bounds.Top >= report.Pages[page].BodyTop);
            Assert.True(fragment.Bounds.Bottom < (report.Pages[page].SummaryTop ?? ReportDocument.FooterTop));
            Assert.True(fragment.Bounds.Left >= ReportDocument.Margin);
            Assert.True(fragment.Bounds.Right <= ReportDocument.PageWidth - ReportDocument.Margin + .01f);
            var headings = report.TextRuns.Where(r => r.Bounds.Left >= fragment.Bounds.Left - .01f
                && r.Bounds.Right <= fragment.Bounds.Right + .01f && r.Bounds.Top >= fragment.Bounds.Top
                && r.Bounds.Bottom <= fragment.Bounds.Top + fragment.HeadingHeight).ToArray();
            string text = string.Concat(headings.Select(r => r.Text)).Replace(" ", "");
            foreach (string header in report.Data.Tables[fragment.Table].Headers)
                Assert.Contains(header.Replace(" ", ""), text);
            if (fragment.Rows[0].Row > 0 || fragment.Rows[0].FirstLine > 0)
                Assert.Contains(report.Data.Labels.Continuation.Replace(" ", ""), text);
        }
        Assert.All(fragments.Zip(fragments.Skip(1)), pair => Assert.False(pair.First.Bounds.IntersectsWith(pair.Second.Bounds)));
    }

    private static SKBitmap RenderBeam(ReportVisuals visuals)
    {
        const float scale = 96f / 72;
        using var surface = SKSurface.Create(new SKImageInfo((int)Math.Ceiling(ReportDocument.LeftWidth * scale),
            (int)Math.Ceiling(visuals.Graphics.BeamHeight * scale)));
        surface.Canvas.Clear(SKColors.White); surface.Canvas.Scale(scale);
        visuals.Graphics.Draw(surface.Canvas, ReportGraphic.Beam, SKRect.Create(ReportDocument.LeftWidth, visuals.Graphics.BeamHeight));
        var bitmap = new SKBitmap(surface.Canvas.DeviceClipBounds.Width, surface.Canvas.DeviceClipBounds.Height);
        Assert.True(surface.ReadPixels(bitmap.Info, bitmap.GetPixels(), bitmap.RowBytes, 0, 0));
        return bitmap;
    }

    private static int CountPixels(SKBitmap bitmap, Rect area, Func<int, int, bool> include)
    {
        int count = 0;
        for (int y = Math.Max(0, (int)area.Top); y < Math.Min(bitmap.Height, (int)Math.Ceiling(area.Bottom)); y++)
            for (int x = Math.Max(0, (int)area.Left); x < Math.Min(bitmap.Width, (int)Math.Ceiling(area.Right)); x++)
                if (include(x, y)) count++;
        return count;
    }

    private static void AssertTextContained(ReportDocument report)
    {
        Assert.All(report.TextRuns, run =>
        {
            Assert.True(run.Bounds.Left >= ReportDocument.Margin - .1f, run.Text);
            Assert.True(run.Bounds.Right <= ReportDocument.PageWidth - ReportDocument.Margin + .1f, run.Text);
            Assert.True(run.Bounds.Top >= ReportDocument.Margin - .1f, run.Text);
            Assert.True(run.Bounds.Bottom < ReportDocument.PageHeight - ReportDocument.Margin + 1, run.Text);
        });
        Assert.All(report.TextRuns.Where(r => r.Bounds.Top < ReportDocument.FooterTop), run =>
            Assert.True(run.Bounds.Bottom < ReportDocument.FooterTop, run.Text));
    }
    private static string RepositoryRoot([System.Runtime.CompilerServices.CallerFilePath] string source = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "../.."));
    private sealed class Files : IProjectFileStore
    {
        internal readonly Dictionary<string, byte[]> Data = []; internal readonly List<string> Writes = [];
        internal bool FailWrites;
        public Task<byte[]?> ReadAsync(string path) => Task.FromResult(Data.GetValueOrDefault(path));
        public Task WriteAtomicAsync(string path, byte[] bytes, CancellationToken cancellationToken = default)
        { if (FailWrites) throw new IOException("Injected write failure"); Writes.Add(path); Data[path] = bytes; return Task.CompletedTask; }
        public Task DeleteAsync(string path) { Data.Remove(path); return Task.CompletedTask; }
    }
}
