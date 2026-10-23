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

    private static ProjectState State(bool overflow = false)
    {
        var fixture = ReportPrototypeFixtures.Create(overflow);
        var document = fixture.Document;
        return new(document, new EditorPresentationState().WithOffset(document.Loads[0].Id, new(18, 12)),
            new(overflow ? "Mehrfach gelagerter Balken" : "Einfach gelagerter Balken", "Neutrales Beispiel", "BEISPIEL-01", "Technik"));
    }

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
        var (main, files) = Open(State(), new(profile, detailed ? PresentationMode.Detailed : PresentationMode.Standard), () => analyses++);
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
        File.WriteAllBytes(projectPath, ProjectFileCodec.Serialize(State(overflow)));
        var main = new MainWindowViewModel();
        Assert.True(main.OpenAsync(projectPath).GetAwaiter().GetResult());
        var bytes = File.ReadAllBytes(projectPath); var revision = main.Session!.CurrentRevision;
        using var report = ProjectReport.Capture(main.Editor!, Now);
        Assert.Equal(overflow ? 2 : 1, report.Pages.Count);
        using (var pdf = File.Create(Path.Combine(directory, name + ".pdf"))) report.WritePdf(pdf);
        string pdfText = Encoding.Latin1.GetString(File.ReadAllBytes(Path.Combine(directory, name + ".pdf")));
        Assert.DoesNotMatch(@"/Subtype\s*/Image\b", pdfText);
        Assert.Equal(report.Pages.Count, Regex.Matches(pdfText, @"/Type\s*/Page\b").Count);
        for (int page = 0; page < report.Pages.Count; page++)
        {
            using var png = File.Create(Path.Combine(directory, $"{name}-{page + 1}.png")); report.WritePng(png, page);
            AssertTextContained(report);
        }
        Assert.Same(revision, main.Session.CurrentRevision); Assert.False(main.Session.IsDirty);
        Assert.Equal(bytes, File.ReadAllBytes(projectPath));
    }

    [Fact]
    public void TechnicalControlsUseCommittedOffsetsAndSharedStations()
    {
        var state = State(); var (main, _) = Open(state);
        main.Editor!.PreviewAnnotationOffset(state.Document.Loads[0].Id, new(77, 66));
        using var report = ProjectReport.Capture(main.Editor, Now);
        var visuals = new ReportVisuals(state, main.Editor.Presentation, report.Data);
        var label = Assert.Single(visuals.Beam.Scene!.Annotations, a => a.Id == state.Document.Loads[0].Id);
        Assert.True(label.IsManual); Assert.Equal(label.AutoBounds.Translate(new Vector(18, 12)), label.Bounds);
        Assert.Same(visuals.Beam.Scene.Frame.Layout, visuals.Axis.AxisLayout!.StationLayout);
        Assert.All(visuals.Diagrams, d =>
        { Assert.Same(main.Editor.Presentation.Result, d.Presentation!.Result); Assert.Same(visuals.Axis.AxisLayout.StationLayout, d.StationLayout); });
    }

    [Fact]
    public void EmptyTablesAndManyRowsPaginateWithNoLostContentOrFooterOverlap()
    {
        var (main, _) = Open(State(true)); using var source = ProjectReport.Capture(main.Editor!, Now);
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
        { using var bitmap = report.RenderPage(page, 96); AssertTextContained(report); }
    }

    [Theory]
    [InlineData(80)] [InlineData(150)]
    public void RealProjectWithManyLoadsCanProduceMoreThanTwoPages(int count)
    {
        var state = State(true);
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
        var (main, _) = Open(State()); using var source = ProjectReport.Capture(main.Editor!, Now);
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
        var fixture = State(); var mm = Length.FromMillimeters;
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
        var state = State(); state = state with { Info = null, Document = state.Document.WithLoads([]).WithDistributedLoads([]) };
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
        var state = State(); int calls = 0;
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
        var (main, files) = Open(State(true)); using var report = ProjectReport.Capture(main.Editor!, Now);
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
        var (main, files) = Open(State(true)); using var report = ProjectReport.Capture(main.Editor!, Now);
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
        var (main, _) = Open(State()); using var original = ProjectReport.Capture(main.Editor!, Now);
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
