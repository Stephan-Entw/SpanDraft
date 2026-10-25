using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.TextFormatting;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia.Helpers;
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
using SpanDraft.Desktop.Sections;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;
using SpanDraft.Desktop.Views;
using SpanDraft.Reporting;
using SpanDraft.Reporting.Notation;
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

    [Theory]
    [InlineData("de-DE")] [InlineData("en-US")]
    public void SummaryUsesFullWidthWithInlinePositionsAndAlignedValues(string culture)
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        var (main, _) = Open(ReportTestProjects.Create());
        using var captured = ProjectReport.Capture(main.Editor!, Now);
        Assert.All(captured.Data.Indicators.Take(2), indicator => Assert.StartsWith("x = ", indicator.Position));
        var indicators = captured.Data.Indicators.Select((indicator, index) => index < 2
            ? indicator with { Position = $"x = 1234567{CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator}890123 mm (x−)" }
            : indicator).ToArray();
        using var report = new ReportDocument(captured.Data with { Indicators = indicators }, new(80, (_, _, _) => { }));
        int page = report.Pages.Select((value, index) => (value, index)).Single(p => p.value.SummaryTop.HasValue).index;
        using var bitmap = report.RenderPage(page);
        float top = report.Pages[page].SummaryTop!.Value + 19;
        var values = indicators.Select(indicator => Assert.Single(report.TextRuns,
            run => run.Text == indicator.Value && run.Bounds.Top >= top).Bounds).ToArray();
        var labels = indicators.Select(indicator => Assert.Single(report.TextRuns,
            run => run.Text == indicator.Label.PlainText && run.Bounds.Top >= top).Bounds).ToArray();
        Assert.Equal(values[0].Right, values[1].Right, 3);
        Assert.Equal(values[2].Right, values[3].Right, 3);
        Assert.Equal(ReportDocument.PageWidth - ReportDocument.Margin, values[2].Right, 3);
        Assert.Equal(ReportDocument.Margin, labels[0].Left, 3);
        Assert.Equal(labels[0].Left, labels[1].Left, 3);
        Assert.Equal(labels[2].Left, labels[3].Left, 3);
        Assert.True(values[0].Right < labels[2].Left);
        Assert.True(values[0].Right - labels[0].Left > values[2].Right - labels[2].Left);
        for (int index = 0; index < indicators.Length; index++)
        {
            Assert.Equal(labels[index].Top, values[index].Top);
            Assert.True(labels[index].Right < values[index].Left);
            if (indicators[index].Position is not { } position) continue;
            var bounds = Assert.Single(report.TextRuns, run => run.Text == position && run.Bounds.Top == labels[index].Top).Bounds;
            Assert.True(labels[index].Right < bounds.Left);
            Assert.True(bounds.Right < values[index].Left);
            Assert.True(bounds.Height < labels[index].Height);
        }
        Assert.Equal(labels[0].Top, labels[2].Top);
        Assert.Equal(labels[1].Top, labels[3].Top);
        Assert.True(labels[0].Bottom < labels[1].Top);
        string directory = Path.Combine(RepositoryRoot(), "output", "pdf", "production");
        Directory.CreateDirectory(directory);
        using var png = File.Create(Path.Combine(directory, "summary-long-positions-" + culture + ".png"));
        report.WritePng(png, page);
    }

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
            text => SchematicText.Measure(text, Typeface.Default, 12), compact: true);
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
    public void SectionDesignationKeepsQuantitiesAndRadiusClausesTogether(string culture)
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        var (main, _) = Open(ReportTestProjects.Create());
        using var original = ProjectReport.Capture(main.Editor!, Now);
        static float Measure(string text) { using var layout = new ReportTextLayout(text, 8.5f); return layout.Width; }
        static string Prefix(string partialGroup, string completeGroup) => Enumerable.Range(1, 30)
            .Select(n => new string('W', n) + " ").First(prefix => Measure(prefix + partialGroup) <= ReportDocument.RightWidth
                && Measure(prefix + completeGroup) > ReportDocument.RightWidth);
        (string Name, string[] Groups)[] cases =
        [
            (Prefix("10", "10 mm") + "10 mm · y-y", ["10 mm"]),
            (Prefix("(r =", "(r = 5 mm)") + "(r = 5 mm) · y-y", ["(r = 5 mm)"]),
            ("I/H-Profil 100 × 80 × 10 mm (r = 5 mm) · y-y", ["10 mm", "(r = 5 mm)"]),
            ("I/H-Profil 100 × 80 × 10,5 mm (r = 5,5 mm) · y-y", ["10,5 mm", "(r = 5,5 mm)"]),
            ("Custom I/H section 4 × 3 × 3/8 in (r = 0.2 in) · y-y", ["3/8 in", "(r = 0.2 in)"]),
            ("I/H-Profil\n100 × 80 × 10 mm (r = 5 mm) · y-y", ["10 mm", "(r = 5 mm)"]),
            ("Custom section with ordinary word boundaries", [])
        ];
        foreach (var (name, groups) in cases)
        {
            using var report = new ReportDocument(original.Data with { Section = name }, new(130, (_, _, _) => { }));
            using var bitmap = report.RenderPage(0, 96);
            var lines = SectionDesignationRuns(report, 0);
            Assert.True(lines.Length > 1, name);
            Assert.Equal(name.Replace('\n', ' '), string.Join(" ", lines.Select(r => r.Text)));
            Assert.Equal(name, report.Data.Section);
            Assert.DoesNotContain(report.Data.Metadata, m => m.Label == report.Data.Labels.Section);
            foreach (var group in groups) Assert.Contains(lines, line => line.Text.Contains(group, StringComparison.Ordinal));
            AssertInformationBlocksSeparated(report, 0); AssertTextContained(report);
        }
    }

    [Theory]
    [InlineData("S235JR (1.0038) · A36", new[] { "S235JR (1.0038) · A36" })]
    [InlineData("S235JR · A36 · SS400", new[] { "S235JR · A36 · SS400" })]
    [InlineData("X5CrNi18-10 (1.4301) · AISI 304 · UNS S30400", new[] { "X5CrNi18-10 (1.4301)", "AISI 304 · UNS S30400" })]
    [InlineData("X5CrNi18-10 (1.4301)", new[] { "X5CrNi18-10 (1.4301)" })]
    public void MaterialDesignationKeepsStandardsTogetherWhenWrapping(string designation, string[] expectedLines)
    {
        var (main, _) = Open(ReportTestProjects.Create());
        using var original = ProjectReport.Capture(main.Editor!, Now);
        using var report = new ReportDocument(original.Data with { Material = designation }, new(130, (_, _, _) => { }));
        using var bitmap = report.RenderPage(0, 96);
        var lines = MaterialDesignationRuns(report);
        Assert.Equal(expectedLines, lines.Select(r => r.Text));
        Assert.Equal(designation, report.Data.Material);
        Assert.DoesNotContain(report.Data.Metadata, m => m.Label == report.Data.Labels.Material);
        Assert.All(lines.Zip(lines.Skip(1)), pair =>
        {
            Assert.Equal(pair.First.Bounds.Left, pair.Second.Bounds.Left);
            Assert.True(pair.First.Bounds.Bottom < pair.Second.Bounds.Top);
        });
        AssertTextContained(report); AssertInformationBlocksSeparated(report, 0);
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void MaterialDesignationWrapsOnlyTheOversizedBlock(bool wideName)
    {
        string wide = new('W', 24);
        string designation = wideName ? wide + " · AISI 304" : "S235JR · " + wide;
        var (main, _) = Open(ReportTestProjects.Create());
        using var original = ProjectReport.Capture(main.Editor!, Now);
        using var report = new ReportDocument(original.Data with { Material = designation }, new(130, (_, _, _) => { }));
        using var bitmap = report.RenderPage(0, 96);
        var lines = MaterialDesignationRuns(report);
        Assert.Equal(3, lines.Length);
        if (wideName)
        {
            Assert.Equal(wide, string.Concat(lines.Take(2).Select(r => r.Text)));
            Assert.Equal("AISI 304", lines[^1].Text);
        }
        else
        {
            Assert.Equal("S235JR", lines[0].Text);
            Assert.Equal(wide, string.Concat(lines.Skip(1).Select(r => r.Text)));
        }
        Assert.DoesNotContain(report.Data.Metadata, m => m.Label == report.Data.Labels.Material);
        AssertTextContained(report); AssertInformationBlocksSeparated(report, 0);
    }

    [Fact]
    public void MaterialDesignationWithoutStandardsStillUsesNormalWrapping()
    {
        string designation = new('W', 24);
        var (main, _) = Open(ReportTestProjects.Create());
        using var original = ProjectReport.Capture(main.Editor!, Now);
        using var report = new ReportDocument(original.Data with { Material = designation }, new(130, (_, _, _) => { }));
        using var bitmap = report.RenderPage(0, 96);
        var lines = MaterialDesignationRuns(report);
        Assert.Equal(2, lines.Length);
        Assert.Equal(designation, string.Concat(lines.Select(r => r.Text)));
        Assert.DoesNotContain(report.Data.Metadata, m => m.Label == report.Data.Labels.Material);
        AssertTextContained(report); AssertInformationBlocksSeparated(report, 0);
    }

    [Theory]
    [InlineData("de-DE")] [InlineData("en-US")]
    public void MaterialMetadataUpdatesUseTheCurrentSnapshotWithoutReanalysis(string culture)
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        var initial = ReportTestProjects.Create();
        var originalMaterial = LibraryTestSupport.WithMetadata(initial.Document.Material, initial.Document.Material.Category, "1.0038", []);
        initial = initial with { Document = initial.Document with { Material = originalMaterial } };
        int analyses = 0;
        var (main, files) = Open(initial, analyzed: () => analyses++);
        var editor = main.Editor!;
        var analysis = editor.Presentation;
        using var original = ProjectReport.Capture(editor, Now);
        Assert.Equal("S235JR (1.0038)", original.Data.Material);
        var updated = new SpanDraft.Core.Materials.Material("Project steel", originalMaterial.YoungsModulus,
            originalMaterial.YieldStrength, originalMaterial.StrengthType!.Value, originalMaterial.Density,
            originalMaterial.PoissonRatio, originalMaterial.Category, "9.8765", ["Standard B", "Standard A"]);
        editor.ApplySetup(editor.Document.Section, editor.Document.BendingAxis, updated);
        AssertCurrent("Project steel (9.8765) · Standard B · Standard A");
        Assert.True(editor.Undo()); AssertCurrent("S235JR (1.0038)");
        Assert.True(editor.Redo()); AssertCurrent("Project steel (9.8765) · Standard B · Standard A");
        Assert.Equal("S235JR (1.0038)", original.Data.Material);
        Assert.Equal(1, analyses); Assert.Empty(files.Writes);

        void AssertCurrent(string expected)
        {
            using var report = ProjectReport.Capture(editor, Now);
            Assert.Equal(expected, report.Data.Material);
            Assert.Equal(expected, editor.Overview.Material);
            Assert.Same(analysis, editor.Presentation);
            Assert.Equal(original.Data.Indicators.Select(i => (i.Label.PlainText, i.Value, i.Position)),
                report.Data.Indicators.Select(i => (i.Label.PlainText, i.Value, i.Position)));
            using var bitmap = report.RenderPage(0, 96);
            Assert.Equal(expected, string.Join(" · ", MaterialDesignationRuns(report).Select(r => r.Text)));
            AssertTextContained(report);
        }
    }

    [Fact]
    public void LongMaterialDesignationPreservesEveryStandardInReportMetadata()
    {
        var state = ReportTestProjects.Create();
        var standards = Enumerable.Range(0, 30).Select(i => $"Long standard designation {i}").ToArray();
        var material = LibraryTestSupport.WithMetadata(state.Document.Material, state.Document.Material.Category, "1.0038", standards);
        var (main, _) = Open(state with { Document = state.Document with { Material = material } });
        using var report = ProjectReport.Capture(main.Editor!, Now);
        string expected = "S235JR (1.0038) · " + string.Join(" · ", standards);
        Assert.Equal(expected, report.Data.Material);
        Assert.Contains(report.Data.Metadata, m => m.Label == report.Data.Labels.Material && m.Value == expected);
        for (int page = 0; page < report.Pages.Count; page++)
        {
            using var bitmap = report.RenderPage(page, 96); AssertTextContained(report);
            if (!report.Pages[page].HasGraphics) continue;
            var lines = MaterialDesignationRuns(report);
            Assert.Equal(3, lines.Length);
            Assert.Equal("S235JR (1.0038)", lines[0].Text);
            Assert.Equal("…", lines[^1].Text);
            AssertInformationBlocksSeparated(report, page);
        }
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

    private static (MainWindowViewModel Main, Files Files) Open(ProjectState state,
        ResultPresentationOptions? presentation = null, Action? analyzed = null)
    {
        var files = new Files();
        string path = Path.GetFullPath("report-input.spandraft"); files.Data[path] = ProjectFileCodec.Serialize(state);
        var main = new MainWindowViewModel(files: files, settings: UserSettings.Default with
            { Profile = presentation?.Profile ?? UnitProfile.Default, Mode = presentation?.Mode ?? PresentationMode.Standard },
            analyze: beam => { analyzed?.Invoke(); return BeamAnalysis.Analyze(beam); });
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
        Assert.Contains(profile[QuantityKind.BeamLength].Symbol, report.Data.Tables.Single(t => t.Title == Strings.Supports).Headers[2].PlainText);
        Assert.Contains(profile[QuantityKind.TransverseForce].Symbol, report.Data.Tables.Single(t => t.Title == Strings.Reactions).Headers[2].PlainText);
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
            text => SchematicText.Measure(text, Typeface.Default, visuals.Beam.LabelFontSize),
            presentation: state.Presentation, profile: main.Editor.Presentation.Options.Profile, loadSymbolScale: .85,
            minimumPaneHeight: ReportVisuals.MinimumBeamPaneHeight);
        Assert.True(scene.MinimumPaneHeight < reserved.MinimumPaneHeight);
        Assert.Equal(scene.MinimumPaneHeight, visuals.Beam.Height, 6);
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

    [Theory]
    [InlineData("de-DE")] [InlineData("en-US")]
    public void HeaderCentersTheTitleAboveThreeAlignedMetadataColumns(string culture)
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        var (main, _) = Open(ReportTestProjects.Create());
        using var report = ProjectReport.Capture(main.Editor!, Now);
        using var bitmap = report.RenderPage(0, 96);
        var heading = Assert.Single(report.TextRuns, r => r.Text == report.Data.Labels.Heading).Bounds;
        var title = Assert.Single(report.TextRuns, r => r.Text == report.Data.Title).Bounds;
        Assert.Equal(ReportDocument.PageWidth / 2, heading.MidX, 3);
        Assert.Equal(heading.MidX, title.MidX, 3);
        Assert.True(title.Top > heading.Bottom);
        Assert.True(title.Top - heading.Bottom <= heading.Height / 2);
        Assert.True(title.Height > heading.Height * 1.5f);
        Assert.DoesNotContain(report.TextRuns, r => r.Text == report.Data.Labels.Title);
        string[] labels = [report.Data.Labels.Project, report.Data.Labels.Reference, report.Data.Labels.Author];
        var columns = labels.Select(label => Assert.Single(report.TextRuns, r => r.Text == label).Bounds).ToArray();
        Assert.True(columns[0].Top > title.Bottom);
        Assert.True(columns[0].Top - title.Bottom <= title.Height);
        Assert.All(columns, bounds => Assert.Equal(columns[0].Top, bounds.Top));
        Assert.True(columns[1].Left - columns[0].Left > columns[2].Left - columns[1].Left);
        var values = labels.Select(label => report.Data.Metadata.Single(m => m.Label == label).Value)
            .Select(value => Assert.Single(report.TextRuns, r => r.Text == value).Bounds).ToArray();
        for (int i = 0; i < columns.Length; i++)
        {
            Assert.Equal(columns[i].Left, values[i].Left);
            Assert.True(columns[i].Height < values[i].Height);
            Assert.True(columns[i].Bottom < values[i].Top);
            Assert.True(values[i].Bottom < report.Pages[0].BodyTop - 12);
            if (i + 1 < columns.Length) Assert.True(values[i].Right < columns[i + 1].Left);
        }
        Assert.All(values, bounds => Assert.Equal(values[0].Top, bounds.Top));
    }

    [Fact]
    public void HeaderWrapsLongTitleAndValuesWithinTheExistingHeaderArea()
    {
        var state = ReportTestProjects.Create() with
        {
            Info = new(string.Join(" ", Enumerable.Repeat("Trägernachweis für die Bühnenkonstruktion", 3)),
                "Erweiterung des bestehenden Veranstaltungsgebäudes im nördlichen Projektbereich",
                "Referenz zur Entwurfsplanung im Oktober 2026", "Ingenieurbüro für Tragwerksplanung und Konstruktion")
        };
        var (main, _) = Open(state);
        using var report = ProjectReport.Capture(main.Editor!, Now);
        using var bitmap = report.RenderPage(0, 96);
        Assert.True(report.Pages[0].HasGraphics);
        var header = report.TextRuns.Where(r => r.Bounds.Top < report.Pages[0].BodyTop - 12).ToArray();
        string[] labels = [report.Data.Labels.Project, report.Data.Labels.Reference, report.Data.Labels.Author];
        var columns = labels.Select(label => Assert.Single(header, r => r.Text == label).Bounds).ToArray();
        var title = header.Skip(1).TakeWhile(r => r.Bounds.Top < columns[0].Top).ToArray();
        Assert.True(title.Length > 1);
        Assert.Equal(state.Info.CalculationTitle, string.Join(" ", title.Select(r => r.Text)));
        Assert.All(title, r => Assert.Equal(ReportDocument.PageWidth / 2, r.Bounds.MidX, 3));
        for (int i = 0; i < columns.Length; i++)
        {
            var values = header.Where(r => r.Bounds.Left == columns[i].Left && r.Bounds.Top > columns[i].Bottom).ToArray();
            Assert.Equal(report.Data.Metadata.Single(m => m.Label == labels[i]).Value, string.Join(" ", values.Select(r => r.Text)));
            float right = i + 1 < columns.Length ? columns[i + 1].Left : ReportDocument.PageWidth - ReportDocument.Margin;
            Assert.All(values, r => Assert.True(r.Bounds.Right <= right));
        }
        Assert.All(header, r => Assert.True(r.Bounds.Bottom < report.Pages[0].BodyTop - 12));
        AssertTextContained(report);
    }

    [Fact]
    public void MissingOptionalProjectInformationKeepsAllHeaderLabelsVisible()
    {
        var state = ReportTestProjects.Create();
        var (main, _) = Open(state); using var full = ProjectReport.Capture(main.Editor!, Now);
        var (compactMain, _) = Open(state with { Info = new(state.Info!.CalculationTitle, null, null, state.Info.Author) });
        using var compact = ProjectReport.Capture(compactMain.Editor!, Now);
        Assert.Equal(full.Data.Metadata.Count - 2, compact.Data.Metadata.Count);
        Assert.All(compact.Data.Metadata, row => Assert.False(string.IsNullOrWhiteSpace(row.Value)));
        Assert.True(compact.Pages[0].BodyTop < full.Pages[0].BodyTop);
        Assert.DoesNotContain(compact.Data.Metadata, row => row.Value is "Neutrales Beispiel" or "BEISPIEL-01");
        using var fullBitmap = full.RenderPage(0, 96);
        using var compactBitmap = compact.RenderPage(0, 96);
        string[] labels = [compact.Data.Labels.Project, compact.Data.Labels.Reference, compact.Data.Labels.Author];
        var columns = labels.Select(label => Assert.Single(compact.TextRuns, r => r.Text == label).Bounds).ToArray();
        Assert.All(columns, bounds => Assert.Equal(columns[0].Top, bounds.Top));
        for (int i = 0; i < labels.Length; i++)
            Assert.Equal(Assert.Single(full.TextRuns, r => r.Text == labels[i]).Bounds.Left, columns[i].Left);
        foreach (var column in columns.Take(2))
        {
            var missing = Assert.Single(compact.TextRuns, r => r.Text == "-" && r.Bounds.Left == column.Left
                && r.Bounds.Top > column.Bottom && r.Bounds.Bottom < compact.Pages[0].BodyTop - 12);
            Assert.True(missing.Bounds.Top > column.Bottom);
        }
        var author = Assert.Single(compact.TextRuns, r => r.Text == compact.Data.Labels.Author).Bounds;
        Assert.Equal(Assert.Single(full.TextRuns, r => r.Text == full.Data.Labels.Author).Bounds.Left, author.Left);
        var title = Assert.Single(compact.TextRuns, r => r.Text == compact.Data.Title).Bounds;
        Assert.True(title.Bottom < author.Top);
        Assert.All(compact.TextRuns.Where(r => r.Bounds.Top < compact.Pages[0].BodyTop - 12),
            r => Assert.True(r.Bounds.Bottom < compact.Pages[0].BodyTop - 12));
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

    [Theory]
    [InlineData("de-DE", 0)] [InlineData("en-US", 0)]
    [InlineData("de-DE", 1)] [InlineData("en-US", 1)]
    [InlineData("de-DE", 2)] [InlineData("en-US", 2)]
    [InlineData("de-DE", 3)] [InlineData("en-US", 3)]
    [InlineData("de-DE", 4)] [InlineData("en-US", 4)]
    public void CaptureOmitsEmptyLoadGroupsAndPreservesGroupOrder(string culture, int group)
    {
        using var scope = new UiCultureScope(culture);
        var state = ReportTestProjects.Create(true);
        var document = state.Document;
        document = document.WithLoads(group == 4 ? document.Loads : document.Loads.Where(l =>
            group == 1 && l.Kind == PointLoadKind.Force || group == 2 && l.Kind == PointLoadKind.Moment).Take(1));
        document = document.WithDistributedLoads(group is 3 or 4 ? document.DistributedLoads.Take(1) : []);
        state = state with { Document = document, Presentation = state.Presentation.RetainEntities(document) };
        var (main, _) = Open(state);
        using var report = ProjectReport.Capture(main.Editor!, Now);
        var expected = new List<string> { Strings.Supports };
        if (group is 1 or 4) expected.Add(Strings.Forces);
        if (group is 2 or 4) expected.Add(Strings.Moments);
        if (group is 3 or 4) expected.Add(Strings.DistributedLoads);
        expected.Add(Strings.Reactions);
        Assert.Equal(expected, report.Data.Tables.Select(t => t.Title));
        Assert.All(report.Data.Tables, t => Assert.NotEmpty(t.Rows));
        Assert.Equal(culture == "de-DE" ? "Kr\u00e4fte" : "Forces", Strings.Forces);
        Assert.Equal(culture == "de-DE" ? "Momente" : "Moments", Strings.Moments);
        Assert.Equal(culture == "de-DE" ? "Streckenlasten" : "Distributed loads", Strings.DistributedLoads);
    }

    [Theory]
    [InlineData(1)] [InlineData(25)] [InlineData(90)] [InlineData(250)]
    public void TablesFlowAcrossColumnsAndSummaryFollowsEveryRow(int count)
    {
        var (main, _) = Open(ReportTestProjects.Create());
        using var source = ProjectReport.Capture(main.Editor!, Now);
        ReportTable Table(string title, int rows) => new(title, ["Name"], [1],
            Enumerable.Range(0, rows).Select(i => (IReadOnlyList<string>)new[] { title + i }).ToArray());
        using var report = new ReportDocument(source.Data with
        {
            Tables = [Table("Forces", count), Table("Moments", 1), Table("Distributed", 1), Table("Reactions", count)]
        }, new(130, (_, _, _) => { }));
        AssertCompleteTablesAndFinalSummary(report);
        var first = report.Pages[0];
        if (count >= 25)
        {
            Assert.Null(first.SummaryTop);
            var last = first.Tables.Last();
            Assert.True(ReportDocument.FooterTop - 14 - last.Bounds.Bottom < ReportDocument.RowHeight);
        }
        if (count >= 90)
        {
            Assert.Contains(report.Pages.Skip(1), page => page.Tables.GroupBy(t => t.Bounds.Left).Any(g => g.Count() > 1));
            Assert.Contains(report.Pages.Skip(1), page => page.Tables.Select(t => t.Bounds.Left).Distinct().Count() == 2);
        }
        for (int i = 0; i < report.Pages.Count; i++)
        {
            using var bitmap = report.RenderPage(i, 96);
            foreach (var fragment in report.Pages[i].Tables)
            {
                bool continued = fragment.Rows[0].Row > 0 || fragment.Rows[0].FirstLine > 0;
                string title = report.Data.Tables[fragment.Table].Title
                    + (continued ? " \u00b7 " + report.Data.Labels.Continuation : "");
                Assert.Contains(report.TextRuns, run => run.Text == title);
            }
        }
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void FirstPageStartsNextTableOnlyWhenACompleteDataRowFits(bool rowFits)
    {
        var (main, _) = Open(ReportTestProjects.Create());
        using var source = ProjectReport.Capture(main.Editor!, Now);
        ReportTable Table(string title, int rows) => new(title, ["Name"], [1],
            Enumerable.Range(0, rows).Select(i => (IReadOnlyList<string>)new[] { "R" + i }).ToArray());
        using var probe = new ReportDocument(source.Data with { Tables = [Table("First", 200)] }, new(130, (_, _, _) => { }));
        var fragment = probe.Pages[0].Tables[0];
        float available = ReportDocument.FooterTop - 14 - fragment.Bounds.Top;
        // Same one-line headings; preserve the existing 12-point gap between tables.
        int count = (int)Math.Floor((available - 2 * fragment.HeadingHeight - 12 - ReportDocument.RowHeight) / ReportDocument.RowHeight);
        if (!rowFits) count++;
        using var report = new ReportDocument(source.Data with { Tables = [Table("First", count), Table("Second", 200)] },
            new(130, (_, _, _) => { }));
        var next = report.Pages[0].Tables.Where(f => f.Table == 1).ToArray();
        if (rowFits) Assert.Single(Assert.Single(next).Rows);
        else Assert.Empty(next);
        AssertCompleteTablesAndFinalSummary(report);
    }

    [Fact]
    public void WrappedRowsThatFitFreshColumnsRemainWhole()
    {
        var (main, _) = Open(ReportTestProjects.Create());
        using var source = ProjectReport.Capture(main.Editor!, Now);
        var rows = Enumerable.Range(0, 24).Select(i => (IReadOnlyList<string>)new[]
            { i + " " + string.Join(" ", Enumerable.Repeat("long cell content", 16)) }).ToArray();
        using var report = new ReportDocument(source.Data with { Tables = [new("Reactions", ["Name"], [1], rows)] },
            new(130, (_, _, _) => { }));
        Assert.True(report.Pages.Count > 1);
        var slices = report.Pages.SelectMany(p => p.Tables).SelectMany(t => t.Rows).ToArray();
        Assert.Equal(Enumerable.Range(0, rows.Length), slices.Select(s => s.Row));
        Assert.All(slices, s => { Assert.Equal(0, s.FirstLine); Assert.Equal(s.Cells.Max(c => c.Length), s.LineCount); });
        AssertCompleteTablesAndFinalSummary(report);
    }

    [Fact]
    public void ContinuationPageFlowsThroughShortTablesThenReactionsLeftToRightBeforeNextPage()
    {
        var (main, _) = Open(ReportTestProjects.Create());
        using var source = ProjectReport.Capture(main.Editor!, Now);
        ReportTable Table(string title, int count) => new(title, ["Name"], [1],
            Enumerable.Range(0, count).Select(i => (IReadOnlyList<string>)new[] { title + i }).ToArray());
        using var probe = new ReportDocument(source.Data with { Tables = [Table("Stream", 300)] }, new(130, (_, _, _) => { }));
        int firstPageRows = Assert.Single(probe.Pages[0].Tables).Rows.Count;
        int columnRows = probe.Pages[1].Tables[0].Rows.Count;
        using var report = new ReportDocument(source.Data with
        {
            Tables = [Table("Forces", firstPageRows), Table("Momente", 2),
                Table("Streckenlasten", 2), Table("Reaktionen", columnRows * 2)]
        }, new(130, (_, _, _) => { }));
        var page = report.Pages[1];
        var left = page.Tables.Where(f => f.Bounds.Left == ReportDocument.Margin).ToArray();
        var right = page.Tables.Where(f => f.Bounds.Left > ReportDocument.Margin).ToArray();
        Assert.Equal(new[] { 1, 2, 3 }, left.Select(f => f.Table));
        var continued = Assert.Single(right);
        Assert.Equal(3, continued.Table);
        Assert.Equal(page.BodyTop, left[0].Bounds.Top);
        Assert.Equal(page.BodyTop, continued.Bounds.Top);
        Assert.All(left.Zip(left.Skip(1)), pair => Assert.True(pair.First.Bounds.Bottom < pair.Second.Bounds.Top));
        Assert.Equal(left[^1].Rows[^1].Row + 1, continued.Rows[0].Row);
        Assert.True(ReportDocument.FooterTop - 14 - left[^1].Bounds.Bottom < ReportDocument.RowHeight);
        Assert.True(ReportDocument.FooterTop - 14 - continued.Bounds.Bottom < ReportDocument.RowHeight);
        Assert.Null(page.SummaryTop);
        var next = report.Pages[2].Tables[0];
        Assert.Equal(ReportDocument.Margin, next.Bounds.Left);
        Assert.Equal(continued.Rows[^1].Row + 1, next.Rows[0].Row);
        AssertCompleteTablesAndFinalSummary(report);
        using var bitmap = report.RenderPage(1, 96);
        Assert.Contains(report.TextRuns, r => r.Text == "Reaktionen \u00b7 " + report.Data.Labels.Continuation
            && r.Bounds.Left == continued.Bounds.Left && r.Bounds.Top == continued.Bounds.Top);
    }

    [Theory]
    [InlineData("Supports", false)] [InlineData("Forces", false)] [InlineData("Moments", false)]
    [InlineData("Distributed", false)] [InlineData("Reactions", false)] [InlineData("Future table", true)]
    public void FinalContinuationBalancesAnyTableAtWholeRowBoundaries(string title, bool wrapped)
    {
        var (main, _) = Open(ReportTestProjects.Create());
        using var source = ProjectReport.Capture(main.Editor!, Now);
        ReportTable Table(string name, int count) => new(name, ["Name"], [1],
            Enumerable.Range(0, count).Select(i => (IReadOnlyList<string>)new[] { name + i }).ToArray());
        using var probe = new ReportDocument(source.Data with { Tables = [Table("Stream", 300)] }, new(130, (_, _, _) => { }));
        int firstPageRows = Assert.Single(probe.Pages[0].Tables).Rows.Count;
        string suffix = wrapped ? " " + string.Join(" ", Enumerable.Repeat("wrapped content", 6)) : "";
        var rows = Enumerable.Range(0, 18).Select(i => (IReadOnlyList<string>)new[] { title + i + suffix }).ToArray();
        using var report = new ReportDocument(source.Data with
        {
            Tables = [Table("Forces", firstPageRows), new(title, ["Name"], [1], rows)]
        }, new(130, (_, _, _) => { }));
        Assert.Equal(probe.Pages[0].Tables[0].Bounds, report.Pages[0].Tables[0].Bounds);
        Assert.Equal(firstPageRows, Assert.Single(report.Pages[0].Tables).Rows.Count);
        var page = report.Pages[1];
        Assert.Equal(2, page.Tables.Count);
        var left = page.Tables[0]; var right = page.Tables[1];
        Assert.Equal(ReportDocument.Margin, left.Bounds.Left);
        Assert.True(right.Bounds.Left > left.Bounds.Right);
        Assert.Equal(page.BodyTop, left.Bounds.Top);
        Assert.Equal(page.BodyTop, right.Bounds.Top);
        Assert.Equal(9, left.Rows.Count);
        Assert.Equal(9, right.Rows.Count);
        Assert.Equal(left.Bounds.Bottom, right.Bounds.Bottom, 3);
        Assert.Equal(Enumerable.Range(0, 18), page.Tables.SelectMany(f => f.Rows).Select(r => r.Row));
        Assert.All(page.Tables.SelectMany(f => f.Rows), r =>
        {
            Assert.Equal(0, r.FirstLine);
            Assert.Equal(r.Cells.Max(c => c.Length), r.LineCount);
        });
        Assert.True(page.SummaryTop >= Math.Max(left.Bounds.Bottom, right.Bounds.Bottom) + 16);
        AssertCompleteTablesAndFinalSummary(report);
        using var bitmap = report.RenderPage(1, 96);
        Assert.Contains(report.TextRuns, r => r.Text == title + " \u00b7 " + report.Data.Labels.Continuation
            && r.Bounds.Left == right.Bounds.Left && r.Bounds.Top == right.Bounds.Top);
    }

    [Fact]
    public void FinalContinuationChoosesBestSplitInsideReactionsAfterShortTables()
    {
        var (main, _) = Open(ReportTestProjects.Create());
        using var source = ProjectReport.Capture(main.Editor!, Now);
        ReportTable Table(string title, int count) => new(title, ["Name"], [1],
            Enumerable.Range(0, count).Select(i => (IReadOnlyList<string>)new[] { title + i }).ToArray());
        using var probe = new ReportDocument(source.Data with { Tables = [Table("Stream", 300)] }, new(130, (_, _, _) => { }));
        using var report = new ReportDocument(source.Data with
        {
            Tables = [Table("Forces", probe.Pages[0].Tables[0].Rows.Count), Table("Momente", 1),
                Table("Streckenlasten", 2), Table("Reaktionen", 20)]
        }, new(130, (_, _, _) => { }));
        var page = report.Pages[1];
        var left = page.Tables.Where(f => f.Bounds.Left == ReportDocument.Margin).ToArray();
        var right = Assert.Single(page.Tables, f => f.Bounds.Left > ReportDocument.Margin);
        Assert.Equal(new[] { 1, 2, 3 }, left.Select(f => f.Table));
        Assert.Equal(3, right.Table);
        Assert.Equal(6, left[^1].Rows.Count);
        Assert.Equal(14, right.Rows.Count);
        Assert.Equal(6, right.Rows[0].Row);
        Assert.True(Math.Abs(left[^1].Bounds.Bottom - right.Bounds.Bottom) < ReportDocument.RowHeight);
        AssertCompleteTablesAndFinalSummary(report);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void FinalContinuationCanSplitBetweenTablesOrBetweenTallWholeRows(bool tallRows)
    {
        var (main, _) = Open(ReportTestProjects.Create());
        using var source = ProjectReport.Capture(main.Editor!, Now);
        ReportTable Table(string title, int count) => new(title, ["Name"], [1],
            Enumerable.Range(0, count).Select(i => (IReadOnlyList<string>)new[] { title + i }).ToArray());
        using var probe = new ReportDocument(source.Data with { Tables = [Table("Stream", 300)] }, new(130, (_, _, _) => { }));
        var tables = new List<ReportTable> { Table("Forces", probe.Pages[0].Tables[0].Rows.Count) };
        if (tallRows)
            tables.Add(new("Tall", ["Name"], [1], Enumerable.Range(0, 2)
                .Select(i => (IReadOnlyList<string>)new[] { string.Join("\n", Enumerable.Repeat("Row" + i, 8)) }).ToArray()));
        else
            tables.AddRange([Table("First", 4), Table("Next", 4)]);
        using var report = new ReportDocument(source.Data with { Tables = tables }, new(130, (_, _, _) => { }));
        var page = report.Pages[1];
        Assert.Equal(2, page.Tables.Count);
        var left = page.Tables[0]; var right = page.Tables[1];
        Assert.True(right.Bounds.Left > left.Bounds.Right);
        Assert.Equal(left.Bounds.Bottom, right.Bounds.Bottom, 3);
        Assert.All(page.Tables.SelectMany(f => f.Rows), row =>
        {
            Assert.Equal(0, row.FirstLine);
            Assert.Equal(row.Cells.Max(c => c.Length), row.LineCount);
        });
        if (tallRows)
        {
            Assert.Single(left.Rows);
            Assert.Single(right.Rows);
        }
        else
        {
            Assert.Equal(1, left.Table);
            Assert.Equal(2, right.Table);
            Assert.Equal(0, right.Rows[0].Row);
            using var bitmap = report.RenderPage(1, 96);
            Assert.Contains(report.TextRuns, r => r.Text == "Next" && r.Bounds.Left == right.Bounds.Left);
            Assert.DoesNotContain(report.TextRuns, r => r.Text == "Next \u00b7 " + report.Data.Labels.Continuation);
        }
        AssertCompleteTablesAndFinalSummary(report);
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void SingleShortRemainingTableStaysInLeftColumn(int rows)
    {
        var (main, _) = Open(ReportTestProjects.Create());
        using var source = ProjectReport.Capture(main.Editor!, Now);
        ReportTable Table(string title, int count) => new(title, ["Name"], [1],
            Enumerable.Range(0, count).Select(i => (IReadOnlyList<string>)new[] { title + i }).ToArray());
        using var probe = new ReportDocument(source.Data with { Tables = [Table("Stream", 300)] }, new(130, (_, _, _) => { }));
        using var report = new ReportDocument(source.Data with
        {
            Tables = [Table("Forces", probe.Pages[0].Tables[0].Rows.Count), Table("Short", rows)]
        }, new(130, (_, _, _) => { }));
        var fragment = Assert.Single(report.Pages[1].Tables);
        Assert.Equal(ReportDocument.Margin, fragment.Bounds.Left);
        Assert.Equal(rows, fragment.Rows.Count);
        AssertCompleteTablesAndFinalSummary(report);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void EveryTableCanFlowThroughBothColumnsAndMultiplePages(int longTable)
    {
        var (main, _) = Open(ReportTestProjects.Create());
        using var source = ProjectReport.Capture(main.Editor!, Now);
        string[] titles = ["Supports", "Forces", "Moments", "Distributed", "Reactions"];
        var tables = titles.Select((title, table) => new ReportTable(title, ["Name"], [1],
            Enumerable.Range(0, table == longTable ? 400 : 2)
                .Select(row => (IReadOnlyList<string>)new[] { $"T{table}R{row}" }).ToArray())).ToArray();
        using var report = new ReportDocument(source.Data with
        {
            Tables = [new("Empty", ["Name"], [1], []), .. tables, new("Empty", ["Name"], [1], [])]
        }, new(130, (_, _, _) => { }));
        AssertCompleteTablesAndFinalSummary(report);
        var fragments = report.Pages.SelectMany(p => p.Tables).Where(f => f.Table == longTable + 1).ToArray();
        Assert.True(report.Pages.Count > 3);
        Assert.Contains(report.Pages.Skip(1), page => page.Tables.Where(f => f.Table == longTable + 1)
            .Select(f => f.Bounds.Left).Distinct().Count() == 2);
        Assert.True(fragments.Length > 4);
        foreach (var fragment in report.Pages.SelectMany(p => p.Tables))
        foreach (var slice in fragment.Rows)
            Assert.Equal($"T{fragment.Table - 1}R{slice.Row}", Assert.Single(slice.Cells[0]));
    }

    [Fact]
    public void FirstRowMovedToAnotherPageIsRewrappedWithoutContinuationTitle()
    {
        var (main, _) = Open(ReportTestProjects.Create());
        using var source = ProjectReport.Capture(main.Editor!, Now);
        ReportTable Table(string title, IReadOnlyList<IReadOnlyList<string>> rows) => new(title, ["Name"], [1], rows);
        var simpleRows = Enumerable.Range(0, 200).Select(i => (IReadOnlyList<string>)new[] { "R" + i }).ToArray();
        using var probe = new ReportDocument(source.Data with { Tables = [Table("First", simpleRows)] }, new(130, (_, _, _) => { }));
        var first = probe.Pages[0].Tables[0];
        float available = ReportDocument.FooterTop - 14 - first.Bounds.Top;
        int count = (int)Math.Floor((available - 2 * first.HeadingHeight - 12 - ReportDocument.RowHeight) / ReportDocument.RowHeight);
        string longCell = string.Join(" ", Enumerable.Repeat("wrapped content", 18));
        using var report = new ReportDocument(source.Data with
        {
            Tables = [Table("First", simpleRows.Take(count).ToArray()), Table("Second", [new[] { longCell }])]
        }, new(130, (_, _, _) => { }));
        Assert.DoesNotContain(report.Pages[0].Tables, f => f.Table == 1);
        var next = Assert.Single(report.Pages[1].Tables, f => f.Table == 1);
        var row = Assert.Single(next.Rows);
        Assert.Equal(0, row.FirstLine);
        Assert.Equal(row.Cells[0].Length, row.LineCount);
        Assert.True(next.Bounds.Width > first.Bounds.Width);
        using var narrow = new ReportDocument(source.Data with { Tables = [Table("Second", [new[] { longCell }])] }, new(130, (_, _, _) => { }));
        Assert.True(narrow.Pages[0].Tables[0].Rows[0].LineCount > row.LineCount);
        using var bitmap = report.RenderPage(1, 96);
        Assert.Contains(report.TextRuns, r => r.Text == "Second");
        Assert.DoesNotContain(report.TextRuns, r => r.Text == "Second \u00b7 " + report.Data.Labels.Continuation);
        AssertCompleteTablesAndFinalSummary(report);
    }

    [Fact]
    public void OversizedFirstRowRepeatsHeadersAndContinuationAfterFirstSlice()
    {
        var (main, _) = Open(ReportTestProjects.Create());
        using var source = ProjectReport.Capture(main.Editor!, Now);
        string text = string.Join("\n", Enumerable.Range(0, 180).Select(i => $"Line {i}"));
        using var report = new ReportDocument(source.Data with
        {
            Tables = [new("Arbitrary", ["Name", "Value"], [.7f, .3f], [new[] { text, "42" }, new[] { "End", "7" }])]
        }, new(130, (_, _, _) => { }));
        Assert.Empty(report.Pages[0].Tables);
        AssertCompleteTablesAndFinalSummary(report);
        var fragments = report.Pages.SelectMany(p => p.Tables).ToArray();
        Assert.True(fragments.Length > 2);
        Assert.All(fragments, f => Assert.Equal(fragments[0].Bounds.Width, f.Bounds.Width, 3));
        Assert.Equal(Enumerable.Range(0, 180).Select(i => $"Line {i}"), fragments.SelectMany(f => f.Rows)
            .Where(r => r.Row == 0).SelectMany(r => r.Cells[0].Skip(r.FirstLine).Take(r.LineCount)));
        for (int page = 1; page < report.Pages.Count; page++)
        {
            using var bitmap = report.RenderPage(page, 96);
            foreach (var fragment in report.Pages[page].Tables)
            {
                bool continued = fragment.Rows[0].Row > 0 || fragment.Rows[0].FirstLine > 0;
                Assert.Contains(report.TextRuns, run => run.Text == "Arbitrary" +
                    (continued ? " \u00b7 " + report.Data.Labels.Continuation : "") && run.Bounds.Top == fragment.Bounds.Top);
                Assert.Contains(report.TextRuns, run => run.Text == "Name" &&
                    run.Bounds.Top > fragment.Bounds.Top && run.Bounds.Bottom <= fragment.Bounds.Top + fragment.HeadingHeight);
            }
        }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void SummaryIsPlacedAfterUnreservedTableStream(bool largeSummary)
    {
        var (main, _) = Open(ReportTestProjects.Create());
        using var source = ProjectReport.Capture(main.Editor!, Now);
        var table = new ReportTable("Any table", ["Name"], [1], Enumerable.Range(0, 30)
            .Select(i => (IReadOnlyList<string>)new[] { "R" + i }).ToArray());
        var data = source.Data with { Tables = [table] };
        using var baseline = new ReportDocument(data with { Indicators = [], Labels = data.Labels with { Assumptions = [] } }, new(130, (_, _, _) => { }));
        float summaryTop = baseline.Pages.Last().SummaryTop!.Value;
        // With no indicators the two headings consume 36 points; each short note adds 12.
        int notesThatFit = (int)Math.Floor((ReportDocument.FooterTop - 14 - summaryTop - 36) / 12);
        Assert.True(notesThatFit > 0);
        using var report = new ReportDocument(data with
        {
            Indicators = [], Labels = data.Labels with
            { Assumptions = Enumerable.Repeat("Calculation assumption", notesThatFit + (largeSummary ? 1 : 0)).ToArray() }
        }, new(130, (_, _, _) => { }));
        AssertCompleteTablesAndFinalSummary(report);
        Assert.Equal(baseline.Pages.SelectMany((p, page) => p.Tables.Select(f => (page, f.Table, f.Bounds, Rows: f.Rows.Count))),
            report.Pages.SelectMany((p, page) => p.Tables.Select(f => (page, f.Table, f.Bounds, Rows: f.Rows.Count))));
        int lastTablePage = report.Pages.Select((p, i) => (p, i)).Last(x => x.p.Tables.Count > 0).i;
        Assert.Equal(largeSummary ? lastTablePage + 1 : lastTablePage, report.Pages.Count - 1);
    }

    [Fact]
    public void EmptyStreamProducesSummaryAndImpossibleContentFailsWithoutLooping()
    {
        var (main, _) = Open(ReportTestProjects.Create());
        using var source = ProjectReport.Capture(main.Editor!, Now);
        using var empty = new ReportDocument(source.Data with { Tables = [new("Empty", ["Name"], [1], [])] }, new(130, (_, _, _) => { }));
        AssertCompleteTablesAndFinalSummary(empty);
        Assert.All(empty.Pages, p => Assert.Empty(p.Tables));
        var impossible = new ReportTable("Table", [string.Join("\n", Enumerable.Repeat("Header", 100))], [1], [new[] { "Row" }]);
        Assert.Throws<InvalidOperationException>(() => new ReportDocument(source.Data with { Tables = [impossible] }, new(130, (_, _, _) => { })));
        Assert.Throws<InvalidOperationException>(() => new ReportDocument(source.Data with
        {
            Tables = [], Labels = source.Data.Labels with { Assumptions = Enumerable.Repeat("Note", 100).ToArray() }
        }, new(130, (_, _, _) => { })));
    }

    private static void AssertCompleteTablesAndFinalSummary(ReportDocument report)
    {
        var ordered = report.Pages.SelectMany(p => p.Tables).SelectMany(f => f.Rows.Select(r => (f.Table, r.Row))).ToArray();
        Assert.Equal(ordered.OrderBy(r => r.Table).ThenBy(r => r.Row), ordered);
        Assert.Equal(report.Data.Tables.SelectMany((t, table) => Enumerable.Range(0, t.Rows.Count).Select(row => (table, row))),
            ordered.Distinct());
        int summaryPage = Assert.Single(report.Pages.Select((p, i) => (Page: p, Index: i)), p => p.Page.SummaryTop.HasValue).Index;
        Assert.Equal(report.Pages.Count - 1, summaryPage);
        for (int table = 0; table < report.Data.Tables.Count; table++)
        {
            var slices = report.Pages.SelectMany(p => p.Tables).Where(f => f.Table == table).SelectMany(f => f.Rows).ToArray();
            for (int row = 0; row < report.Data.Tables[table].Rows.Count; row++)
            {
                var parts = slices.Where(s => s.Row == row).ToArray();
                Assert.NotEmpty(parts);
                Assert.Equal(Enumerable.Range(0, parts[0].Cells.Max(c => c.Length)),
                    parts.SelectMany(p => Enumerable.Range(p.FirstLine, p.LineCount)));
            }
        }
        foreach (var page in report.Pages)
        foreach (var fragment in page.Tables)
        {
            Assert.NotEmpty(fragment.Rows);
            Assert.True(fragment.Bounds.Bottom <= (page.SummaryTop - 16 ?? ReportDocument.FooterTop - 14));
        }
    }

    [Fact]
    public void EmptyTablesAndManyRowsPaginateWithNoLostContentOrFooterOverlap()
    {
        var (main, _) = Open(ReportTestProjects.Create(true)); using var source = ProjectReport.Capture(main.Editor!, Now);
        var tables = source.Data.Tables.Select(t => t with { Rows = Enumerable.Range(0, 15).SelectMany(_ => t.Rows).ToArray() }).ToArray();
        using var report = new ReportDocument(source.Data with { Tables = [tables[0], new("Empty", ["Name"], [1], []), .. tables.Skip(1)] },
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
        int forceTable = report.Data.Tables.ToList().FindIndex(t => t.Title == Strings.Forces);
        Assert.Equal(count, report.Data.Tables[forceTable].Rows.Count);
        Assert.Equal(count, report.Pages.SelectMany(p => p.Tables).Where(t => t.Table == forceTable).Sum(t => t.Rows.Count));
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
        AssertCompleteTablesAndFinalSummary(report);
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
            Assert.Contains(report.Data.SectionProperties, p => p.PlainText.StartsWith("I" + axis.AxisDesignation.ToString().ToLowerInvariant() + " = "));
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
        Assert.Equal(new[] { Strings.Supports, Strings.Reactions }, report.Data.Tables.Select(t => t.Title));
        using var bitmap = report.RenderPage(0, 96); AssertTextContained(report);
        string[] labels = [report.Data.Labels.Project, report.Data.Labels.Reference, report.Data.Labels.Author];
        var columns = labels.Select(label => Assert.Single(report.TextRuns, r => r.Text == label).Bounds).ToArray();
        Assert.All(columns, bounds => Assert.Equal(columns[0].Top, bounds.Top));
        Assert.All(columns, bounds => Assert.True(bounds.Bottom < report.Pages[0].BodyTop - 12));
        Assert.All(columns, column => Assert.Single(report.TextRuns, r => r.Text == "-"
            && r.Bounds.Left == column.Left && r.Bounds.Top > column.Bottom
            && r.Bounds.Bottom < report.Pages[0].BodyTop - 12));
    }

    [Fact]
    public void ShellReportWorkspacePreservesSnapshotDirtyStateAndDraftsWithoutStayingBusy()
    {
        var state = ReportTestProjects.Create(); int analyses = 0;
        var (main, files) = Open(state, analyzed: () => analyses++);
        main.Editor!.SetAnnotationOffset(state.Document.Loads[0].Id, new(20, 10));
        main.Editor.DimensionLength.Begin(); main.Editor.DimensionLength.Text = "9000";
        var revision = main.Session!.CurrentRevision; var editor = main.Editor;
        Assert.True(main.ReportAsync().GetAwaiter().GetResult());
        var preview = Assert.IsType<ReportPreviewViewModel>(main.CurrentViewModel);
        Assert.Equal(MainWorkspaceKind.ReportPreview, main.Workspace);
        Assert.False(main.IsWorkspaceBusy); Assert.False(editor.IsBusy); Assert.True(editor.PreserveDrafts);
        editor.DimensionLength.LoseFocus();
        Assert.Same(revision, main.Session.CurrentRevision); Assert.True(main.Session.IsDirty); Assert.Empty(files.Writes);
        Assert.Equal(state.Document.Length, main.Editor.Document.Length); Assert.Equal(1, analyses);
        Assert.True(editor.DimensionLength.IsEditing); Assert.Equal("9000", editor.DimensionLength.Text);
        Assert.False(main.CanNewProject); Assert.False(main.CanOpenProject); Assert.False(main.CanSaveProject);
        Assert.False(main.CanCloseProject); Assert.False(main.CanReport); Assert.False(main.CanUndo); Assert.False(main.CanRedo);
        Assert.False(main.NewAsync().GetAwaiter().GetResult()); Assert.False(main.OpenAsync("missing.spandraft").GetAwaiter().GetResult());
        Assert.False(main.SaveAsync().GetAwaiter().GetResult()); Assert.False(main.CloseProjectAsync().GetAwaiter().GetResult());
        Assert.False(main.ReportAsync().GetAwaiter().GetResult()); Assert.False(main.Undo()); Assert.False(main.Redo());
        Assert.Same(preview, main.ReportPreview);
        Assert.True(main.CloseReport()); Assert.Same(editor, main.CurrentViewModel); Assert.False(editor.IsReportOpen);
        Assert.Null(preview.Picture); Assert.Same(revision, main.Session.CurrentRevision);
        Assert.True(editor.DimensionLength.IsEditing); Assert.Equal("9000", editor.DimensionLength.Text);
        Assert.True(main.ReportAsync().GetAwaiter().GetResult()); Assert.NotSame(preview, main.ReportPreview);
        Assert.Equal(0, main.ReportPreview!.PageIndex); Assert.True(main.ReportPreview.IsPageFit);
        main.CloseReport();
        var unavailable = state with { Document = state.Document.WithSupports([]) };
        var (invalid, _) = Open(unavailable); Assert.False(invalid.CanReport); Assert.False(invalid.ReportAsync().GetAwaiter().GetResult());
        Assert.Throws<InvalidOperationException>(() => ProjectReport.Capture(invalid.Editor!, Now));
    }

    [Fact]
    public void PreviewNavigationAndPdfExportDoNotTouchProjectAndProtectProjectTargets()
    {
        var (main, files) = Open(ReportTestProjects.Create(true));
        Assert.True(main.ReportAsync().GetAwaiter().GetResult());
        var preview = main.ReportPreview!;
        preview.ShowPage(1);
        Assert.Equal("2 / 2", preview.PageLabel); Assert.False(preview.NextCommand.CanExecute(null));
        string destination = Path.GetFullPath("export.pdf");
        preview.SavePdfAsync(destination).GetAwaiter().GetResult(); Assert.True(files.Data.ContainsKey(destination));
        Assert.Equal(new[] { destination }, files.Writes);
        Assert.Throws<IOException>(() => preview.SavePdfAsync(main.Session!.FilePath!).GetAwaiter().GetResult());
        Assert.Throws<IOException>(() => preview.SavePdfAsync(Path.GetFullPath("export.txt")).GetAwaiter().GetResult());
        Assert.Equal(new[] { destination }, files.Writes);
        byte[] exported = files.Data[destination].ToArray(); files.FailWrites = true;
        Assert.Throws<IOException>(() => preview.SavePdfAsync(destination).GetAwaiter().GetResult());
        Assert.Equal(exported, files.Data[destination]);
        Assert.False(main.Session!.IsDirty); main.CloseReport();
    }

    [Theory]
    [InlineData("de-DE", false, 1100, 650)] [InlineData("en-US", true, 1100, 650)]
    [InlineData("de-DE", true, 1100, 650)] [InlineData("en-US", false, 1100, 650)]
    [InlineData("de-DE", true, 1250, 800)] [InlineData("en-US", false, 1250, 800)]
    [InlineData("de-DE", false, 1250, 800)] [InlineData("en-US", true, 1250, 800)]
    public void PreviewToolbarFitsTheMainWorkspaceInBothThemes(string language, bool dark, int width, int height)
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
        Application.Current!.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        var (main, files) = Open(ReportTestProjects.Create(true));
        var (window, view) = ShowReportWorkspace(main, files, width, height);
        try
        {
            window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(window.RequestedThemeVariant, view.ActualThemeVariant);
            var bar = view.FindControl<Border>("ReportToolbar")!;
            var buttons = new[] { "PreviousButton", "NextButton", "ZoomOutButton", "ZoomInButton", "PageFitButton", "PageWidthButton", "SavePdfButton", "PrintButton", "CloseButton", "ZoomValueButton" }
                .Select(n => view.FindControl<Button>(n)!).ToArray();
            Assert.All(buttons, button =>
            {
                var top = button.TranslatePoint(default, bar)!.Value;
                Assert.True(new Rect(bar.Bounds.Size).Contains(new Rect(top, button.Bounds.Size)), button.Name);
                Assert.Equal(buttons[0].Bounds.Height, button.Bounds.Height);
                Assert.Equal(buttons[0].TranslatePoint(default, bar)!.Value.Y, top.Y);
                Assert.False(button.IsFocused); Assert.DoesNotContain("selected", button.Classes);
            });
            Assert.All(buttons.Take(6), button =>
            {
                Assert.False(string.IsNullOrWhiteSpace(Avalonia.Automation.AutomationProperties.GetName(button)));
                Assert.NotNull(ToolTip.GetTip(button));
                if (button.Name is "ZoomOutButton" or "ZoomInButton")
                {
                    Assert.Equal(buttons[0].Bounds.Width, button.Bounds.Width);
                    var symbol = Assert.Single(button.GetVisualDescendants().OfType<PathIcon>());
                    Assert.Equal(new Size(16, 16), symbol.Bounds.Size);
                    Assert.Same(button.Foreground, symbol.Foreground);
                    Assert.Same(Application.Current!.FindResource(button.Name == "ZoomOutButton" ? "FluentIcon.Subtract" : "FluentIcon.Add"), symbol.Data);
                    return;
                }
                bool navigation = button.Name is "PreviousButton" or "NextButton";
                if (navigation) Assert.True(button.Bounds.Width < buttons[4].Bounds.Width);
                else Assert.Equal(new Size(36, 36), button.Bounds.Size);
                var icon = Assert.Single(button.GetVisualDescendants().OfType<PathIcon>());
                Assert.Same(button.Foreground, icon.Foreground);
                int size = navigation ? 20 : 24;
                Assert.Equal(new Size(size, size), icon.Bounds.Size);
            });
            Assert.False(buttons[0].IsEffectivelyEnabled); Assert.True(buttons[1].IsEffectivelyEnabled);
            Assert.Equal(OperatingSystem.IsWindows(), buttons[7].IsEnabled);
            Assert.Equal(language == "de-DE" ? "Ganze Seite anzeigen" : "Show whole page", ToolTip.GetTip(buttons[4]));
            Assert.Equal(language == "de-DE" ? "An Seitenbreite anpassen" : "Fit to page width", ToolTip.GetTip(buttons[5]));
            Assert.All(buttons.Skip(4).Take(2), button =>
                Assert.Equal(ToolTip.GetTip(button), Avalonia.Automation.AutomationProperties.GetName(button)));
            Assert.Contains("secondary", buttons[8].Classes);
            Assert.DoesNotContain("toolbar", buttons[8].Classes); Assert.DoesNotContain("primary", buttons[8].Classes);
            Assert.DoesNotContain("…", Strings.ReportSavePdf); Assert.DoesNotContain("…", Strings.ReportPrint);
            var ordered = new[] { "SavePdfButton", "PrintButton", "PreviousButton", "PageValue", "NextButton",
                "ZoomOutButton", "ZoomValue", "ZoomInButton", "PageFitButton", "PageWidthButton", "CloseButton" }
                .Select(name => view.FindControl<Control>(name)!).ToArray();
            var bounds = ordered.Select(control => new Rect(control.TranslatePoint(default, bar)!.Value, control.Bounds.Size)).ToArray();
            Assert.All(bounds, rect => Assert.True(new Rect(bar.Bounds.Size).Contains(rect)));
            for (int i = 1; i < bounds.Length; i++) Assert.True(bounds[i - 1].Right <= bounds[i].Left, ordered[i].Name);
            Assert.Equal(bar.Padding.Left, bounds[0].Left);
            Assert.Equal(bar.Bounds.Width - bar.Padding.Right, bounds[^1].Right);
            var center = view.FindControl<StackPanel>("PreviewControls")!;
            Assert.Equal(bar.Bounds.Width / 2, center.TranslatePoint(new(center.Bounds.Width / 2, 0), bar)!.Value.X, .5);
            Assert.Empty(bar.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Rectangle>());
            Assert.Null(center.Background);
            Assert.True(bounds[5].Left - bounds[4].Right > bounds[6].Left - bounds[5].Right);
            Assert.Equal(bounds[5].Right, bounds[6].Left); Assert.Equal(bounds[6].Right, bounds[7].Left);
            Assert.Equal(bounds[2].Right, bounds[3].Left); Assert.Equal(bounds[3].Right, bounds[4].Left);
            double pageStep = bounds[3].Center.X - bounds[2].Center.X;
            double zoomStep = bounds[6].Center.X - bounds[5].Center.X;
            Assert.Equal(pageStep, bounds[4].Center.X - bounds[3].Center.X);
            Assert.Equal(zoomStep, bounds[7].Center.X - bounds[6].Center.X);
            Assert.True(bounds[5].Center.X - bounds[4].Center.X > Math.Max(pageStep, zoomStep));
            Assert.True(bounds[8].Center.X - bounds[7].Center.X > Math.Max(pageStep, zoomStep));
            Assert.Equal(bounds[5].Left - bounds[4].Right, bounds[8].Left - bounds[7].Right);
            var zoomLabel = view.FindControl<TextBlock>("ZoomLabel")!;
            double baseline = zoomLabel.TranslatePoint(new(0, zoomLabel.TextLayout.Baseline), bar)!.Value.Y;
            Assert.All(buttons.Skip(6).Take(3), button =>
            {
                var caption = Assert.Single(button.GetVisualDescendants().OfType<TextBlock>());
                Assert.Equal(baseline, caption.TranslatePoint(new(0, caption.TextLayout.Baseline), bar)!.Value.Y, .01);
                Assert.Equal(caption.Text, Avalonia.Automation.AutomationProperties.GetName(button));
                Assert.True(new Rect(button.Bounds.Size).Contains(new Rect(caption.TranslatePoint(default, button)!.Value, caption.Bounds.Size)));
            });
            Assert.IsType<ToolbarTextPresenter>(zoomLabel.Parent);
            Assert.IsType<ToolbarTextPresenter>(view.FindControl<TextBlock>("PageLabel")!.Parent);
            Assert.Equal((bounds[5].Center.X + bounds[7].Center.X) / 2, bounds[6].Center.X, .5);
            Assert.All(new[] { ordered[3], ordered[6] }, label =>
                Assert.Equal(bounds[0].Center.Y, label.TranslatePoint(new(0, label.Bounds.Height / 2), bar)!.Value.Y, .5));
            var preview = main.ReportPreview!;
            var pageLabel = view.FindControl<TextBlock>("PageLabel")!;
            var pageValue = view.FindControl<ToolbarTextPresenter>("PageValue")!;
            Assert.True(double.IsFinite(pageValue.Width));
            Assert.Equal(pageValue.Width, pageValue.Bounds.Width);
            var labelSize = pageValue.Bounds.Size;
            Assert.Equal("1 / 2", pageLabel.Text);
            preview.NextCommand.Execute(null); Dispatcher.UIThread.RunJobs(); Assert.Equal("2 / 2", pageLabel.Text);
            preview.PreviousCommand.Execute(null); Dispatcher.UIThread.RunJobs(); Assert.Equal("1 / 2", pageLabel.Text);
            Assert.Equal(labelSize, pageValue.Bounds.Size);
            Assert.DoesNotContain(view.GetVisualDescendants(), c => c is ComboBox || c is TextBox { IsVisible: true });
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == Strings.ReportHeading);
            Assert.Empty(window.OwnedWindows); Assert.Equal(main.WindowTitle, window.Title);
            var page = view.FindControl<ReportPageControl>("PageView")!;
            var scroll = view.FindControl<ScrollViewer>("PageScroll")!;
            Assert.True(page.Bounds.Width + 32 <= scroll.Viewport.Width + 1);
            Assert.True(page.Bounds.Height + 32 <= scroll.Viewport.Height + 1);
            string directory = Path.Combine(RepositoryRoot(), "output", "pdf", "production"); Directory.CreateDirectory(directory);
            using var image = new RenderTargetBitmap(new(width, height), new Vector(96, 96)); image.Render(window);
            image.Save(Path.Combine(directory, $"workspace-{language}-{dark}-{width}.png"), PngBitmapEncoderOptions.Default);
            using var toolbarImage = new RenderTargetBitmap(new((int)bar.Bounds.Width, (int)bar.Bounds.Height), new(96, 96)); toolbarImage.Render(bar);
            toolbarImage.Save(Path.Combine(directory, $"preview-toolbar-{language}-{dark}-{width}.png"), PngBitmapEncoderOptions.Default);
        }
        finally { window.Close(); }
    }

    [Theory]
    [InlineData("Arial", 1)] [InlineData("Arial", 2)]
    [InlineData("Helvetica Neue", 1)] [InlineData("Helvetica Neue", 2)]
    [InlineData("Segoe UI", 1)] [InlineData("Segoe UI", 2)]
    public void ReportToolbarKeepsSharedTextBaselineAndCentersIconsAndReferenceDigit(string font, int scaling)
    {
        var (main, files) = Open(ReportTestProjects.Create());
        var (window, view) = ShowReportWorkspace(main, files);
        try
        {
            view.FontFamily = new FontFamily(font);
            var controls = view.FindControl<Border>("ReportToolbar")!;
            var page = view.FindControl<TextBlock>("PageLabel")!;
            var zoom = view.FindControl<TextBlock>("ZoomLabel")!;
            var captions = new[] { "SavePdfButton", "PrintButton", "CloseButton" }
                .Select(name => Assert.Single(view.FindControl<Button>(name)!.GetVisualDescendants().OfType<TextBlock>())).ToArray();
            double? initialBaseline = null;
            foreach (int percent in new[] { 25, 50, 61, 75, 125, 400 })
            {
                Assert.True(main.ReportPreview!.TrySetZoom(percent.ToString(CultureInfo.InvariantCulture)));
                page.Text = percent < 100 ? "1 / 1" : "99 / 99";
                Dispatcher.UIThread.RunJobs();
                double baseline = page.TranslatePoint(new(0, page.TextLayout.Baseline), controls)!.Value.Y;
                if (initialBaseline is { } previous) Assert.Equal(previous, baseline, .01);
                initialBaseline = baseline;
                Assert.All(captions.Append(zoom).Append(page), text =>
                {
                    Assert.Equal(baseline, text.TranslatePoint(new(0, text.TextLayout.Baseline), controls)!.Value.Y, .01);
                    Assert.Null(text.RenderTransform);
                    var presenter = Assert.IsType<ToolbarTextPresenter>(text.Parent);
                    var origin = text.TranslatePoint(default, presenter)!.Value;
                    Assert.True(origin.X >= -.5 && origin.Y >= -.5);
                    Assert.True(origin.X + text.Bounds.Width <= presenter.Bounds.Width + .5);
                    Assert.True(origin.Y + text.Bounds.Height <= presenter.Bounds.Height + .5);
                });
                using var image = new RenderTargetBitmap(new((int)Math.Ceiling(controls.Bounds.Width * scaling),
                    (int)Math.Ceiling(controls.Bounds.Height * scaling)), new(96 * scaling, 96 * scaling));
                image.Render(controls);
                using var pixels = new WriteableBitmap(image.PixelSize, image.Dpi, PixelFormat.Bgra8888, AlphaFormat.Premul);
                using var buffer = pixels.Lock(); image.CopyPixels(buffer);
                var bytes = new byte[buffer.RowBytes * buffer.Size.Height];
                System.Runtime.InteropServices.Marshal.Copy(buffer.Address, bytes, 0, bytes.Length);
                var minus = Ink(view.FindControl<Button>("ZoomOutButton")!);
                var label = Ink(view.FindControl<Grid>("ZoomValue")!);
                var plus = Ink(view.FindControl<Button>("ZoomInButton")!);
                int leftGap = label.Left - minus.Right - 1, rightGap = plus.Left - label.Right - 1;
                Assert.True(leftGap > 0 && rightGap > 0);
                // Text alignment uses font geometry; raster bounds also depend on pixel coverage.
                var zoomValue = view.FindControl<Grid>("ZoomValue")!;
                double zoomCenter = zoomValue.TranslatePoint(new(zoomValue.Bounds.Width / 2, 0), controls)!.Value.X * scaling;
                double left = double.PositiveInfinity, right = double.NegativeInfinity, advance = 0;
                foreach (var run in Assert.Single(zoom.TextLayout.TextLines).TextRuns.OfType<ShapedTextRun>())
                {
                    var glyphs = run.GlyphRun;
                    left = Math.Min(left, advance + glyphs.InkBounds.Left - glyphs.BaselineOrigin.X);
                    right = Math.Max(right, advance + glyphs.InkBounds.Right - glyphs.BaselineOrigin.X);
                    advance += run.Size.Width;
                }
                Assert.True(double.IsFinite(left) && double.IsFinite(right));
                double textCenter = zoom.TranslatePoint(new((left + right) / 2, 0), controls)!.Value.X * scaling;
                Assert.True(Math.Abs(zoomCenter - textCenter) <= scaling,
                    $"{font}, {percent} %, {scaling}×: text geometry center {textCenter}px vs zoom control center {zoomCenter}px");
                // Fluent Add and Subtract have different ink widths; compare their rendered centers.
                double symbolMidpoint = (minus.Left + minus.Right + plus.Left + plus.Right + 2) / 4d;
                Assert.True(Math.Abs(zoomCenter - symbolMidpoint) <= scaling,
                    $"{font}, {percent} %, {scaling}×: zoom control center {zoomCenter}px vs symbol midpoint {symbolMidpoint}px");
                var iconNames = new[] { "PreviousButton", "NextButton", "ZoomOutButton", "ZoomInButton", "PageFitButton", "PageWidthButton" };
                double axis = view.FindControl<Button>("CloseButton")!.TranslatePoint(new(0,
                    view.FindControl<Button>("CloseButton")!.Bounds.Height / 2), controls)!.Value.Y * scaling;
                foreach (string name in iconNames)
                {
                    var ink = Ink(view.FindControl<Control>(name)!);
                    Assert.True(Math.Abs((ink.Top + ink.Bottom + 1) / 2d - axis) <= scaling,
                        $"{font}, {percent} %, {scaling}×: {name} ink center {(ink.Top + ink.Bottom + 1) / 2d}px vs axis {axis}px");
                }
                foreach (string name in new[] { "PageValue", "ZoomValue", "CloseButton" })
                {
                    var control = view.FindControl<Control>(name)!;
                    double center = control.TranslatePoint(new(0, control.Bounds.Height / 2), controls)!.Value.Y * scaling;
                    Assert.Equal(axis, center, .01);
                }
                if (font == "Helvetica Neue" && scaling == 2 && percent == 61)
                {
                    string directory = Path.Combine(RepositoryRoot(), "output", "pdf", "production"); Directory.CreateDirectory(directory);
                    image.Save(Path.Combine(directory, "report-toolbar-aligned-2x.png"), PngBitmapEncoderOptions.Default);
                }

                // Text shares a baseline based on digit height. A slash or descender can
                // change a string's ink bounds without changing that alignment.
                string? pageText = page.Text;
                page.Text = "0"; Dispatcher.UIThread.RunJobs();
                Assert.Equal(baseline, page.TranslatePoint(new(0, page.TextLayout.Baseline), controls)!.Value.Y, .01);
                using var reference = new RenderTargetBitmap(image.PixelSize, image.Dpi);
                reference.Render(controls); reference.CopyPixels(buffer);
                System.Runtime.InteropServices.Marshal.Copy(buffer.Address, bytes, 0, bytes.Length);
                var digit = Ink(view.FindControl<ToolbarTextPresenter>("PageValue")!);
                double digitCenter = (digit.Top + digit.Bottom + 1) / 2d;
                Assert.True(Math.Abs(digitCenter - axis) <= scaling,
                    $"{font}, {percent} %, {scaling}×: reference digit center {digitCenter}px vs axis {axis}px");
                page.Text = pageText; Dispatcher.UIThread.RunJobs();

                (int Left, int Top, int Right, int Bottom) Ink(Control control)
                {
                    var origin = control.TranslatePoint(default, controls)!.Value;
                    var foreground = (ISolidColorBrush)(control.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault()?.Foreground ??
                        control.GetVisualDescendants().OfType<PathIcon>().First().Foreground)!;
                    var color = foreground.Color;
                    int threshold = (255 + Math.Max(color.R, Math.Max(color.G, color.B)) + 1) / 2;
                    int left = image.PixelSize.Width, top = image.PixelSize.Height, right = -1, bottom = -1;
                    for (int x = (int)Math.Floor(origin.X * scaling); x < Math.Ceiling((origin.X + control.Bounds.Width) * scaling); x++)
                        for (int y = 0; y < image.PixelSize.Height; y++)
                            // Count at least half-covered glyph pixels against the light surface, excluding the button border.
                            if (bytes[y * buffer.RowBytes + x * 4 + 3] >= 128 &&
                                Math.Max(bytes[y * buffer.RowBytes + x * 4], Math.Max(bytes[y * buffer.RowBytes + x * 4 + 1], bytes[y * buffer.RowBytes + x * 4 + 2])) <= threshold)
                            {
                                left = Math.Min(left, x); right = Math.Max(right, x);
                                top = Math.Min(top, y); bottom = Math.Max(bottom, y);
                            }
                    Assert.True(right >= left, control.Name);
                    return (left, top, right, bottom);
                }
            }
        }
        finally { window.Close(); }
    }

    [Theory]
    [InlineData(0, 0, 1)] [InlineData(1, 0, 1)]
    [InlineData(0, 1, 1)] [InlineData(1, 1, 2)]
    public void PreviewSkiaRenderingMatchesTheComposedPageAtFitAndFullSize(int page, int zoom, int scaling)
    {
        var (main, files) = Open(ReportTestProjects.Create(true));
        var (window, view) = ShowReportWorkspace(main, files);
        var preview = main.ReportPreview!; var report = preview.Report;
        try
        {
            if (zoom == 1) while (preview.Zoom < 1) preview.ZoomInCommand.Execute(null);
            preview.ShowPage(page); Dispatcher.UIThread.RunJobs();
            var control = view.FindControl<ReportPageControl>("PageView")!;
            var size = control.Bounds.Size;
            Assert.Equal(ReportDocument.PageWidth / (double)ReportDocument.PageHeight, size.Width / size.Height, .003);
            if (zoom == 1)
            {
                Assert.Equal(ReportDocument.PageWidth * 96d / 72, control.Width, .001);
                Assert.Equal(ReportDocument.PageHeight * 96d / 72, control.Height, .001);
            }
            using var actual = new RenderTargetBitmap(new((int)Math.Ceiling(size.Width * scaling), (int)Math.Ceiling(size.Height * scaling)),
                new(96 * scaling, 96 * scaling));
            actual.Render(control);
            using var pixels = new WriteableBitmap(actual.PixelSize, actual.Dpi, PixelFormat.Bgra8888, AlphaFormat.Premul);
            using var buffer = pixels.Lock(); actual.CopyPixels(buffer);
            var bytes = new byte[buffer.RowBytes * buffer.Size.Height];
            System.Runtime.InteropServices.Marshal.Copy(buffer.Address, bytes, 0, bytes.Length);
            using var expected = new SKBitmap(new SKImageInfo(actual.PixelSize.Width, actual.PixelSize.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
            using (var canvas = new SKCanvas(expected))
            {
                canvas.Scale((float)(size.Width * scaling / ReportDocument.PageWidth), (float)(size.Height * scaling / ReportDocument.PageHeight));
                report.DrawPage(canvas, page);
            }
            var expectedBytes = expected.Bytes;
            int ink = 0, different = 0;
            for (int y = 0; y < expected.Height; y++)
            for (int x = 0; x < expected.Width; x++)
            {
                int a = y * buffer.RowBytes + x * 4, b = y * expected.RowBytes + x * 4;
                if (expectedBytes[b] < 240 || expectedBytes[b + 1] < 240 || expectedBytes[b + 2] < 240) ink++;
                if (Math.Abs(bytes[a] - expectedBytes[b]) > 24 || Math.Abs(bytes[a + 1] - expectedBytes[b + 1]) > 24
                    || Math.Abs(bytes[a + 2] - expectedBytes[b + 2]) > 24 || Math.Abs(bytes[a + 3] - expectedBytes[b + 3]) > 24) different++;
            }
            Assert.True(ink > 1000);
            Assert.True(different < ink * .05, $"{different} differing pixels for {ink} ink pixels, page {page}, zoom {zoom}, scaling {scaling}.");
        }
        finally { window.Close(); }
    }

    [Fact]
    public void PreviewCachesVisitedPagesAcrossZoomAndReleasesThemOnClose()
    {
        var (main, files) = Open(ReportTestProjects.Create(true)); using var original = ProjectReport.Capture(main.Editor!, Now);
        int draws = 0;
        var report = new ReportDocument(original.Data, new(130, (_, _, _) => draws++));
        using var preview = new ReportPreviewViewModel(report, main.Session!.FilePath, files, () => { });
        var view = new ReportPreviewView { DataContext = preview };
        var window = new Window { Content = view, Width = 1100, Height = 650 };
        window.Show(); Dispatcher.UIThread.RunJobs();
        var control = view.FindControl<ReportPageControl>("PageView")!;
        var first = Assert.IsType<ReportPagePicture>(control.Page); int recordedDraws = draws;
        preview.ShowPage(1); var second = Assert.IsType<ReportPagePicture>(control.Page);
        Assert.NotSame(first, second); Assert.Equal(report.TechnicalAreas(0).Count, recordedDraws);
        var delayed = new ReportPageControl.PageDrawOperation(first, new Rect(control.Bounds.Size));
        for (int i = 0; i < 20; i++)
        {
            (i % 2 == 0 ? preview.ZoomInCommand : preview.PageFitCommand).Execute(null);
            preview.ShowPage(0); Assert.Same(first, control.Page);
            preview.ShowPage(1); Assert.Same(second, control.Page);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.Equal(recordedDraws, draws);
        preview.ShowPage(-1); preview.ShowPage(report.Pages.Count); Assert.Same(second, control.Page);
        preview.Dispose(); window.Close(); Assert.Null(control.Page);
        preview.ShowPage(0); Assert.Null(control.Page);
        using var bitmap = new SKBitmap(400, 600);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Magenta);
        first.Draw(canvas); second.Draw(canvas);
        var pending = new PendingReportDraw(delayed);
        pending.Measure(new(400, 600)); pending.Arrange(new Rect(0, 0, 400, 600));
        DrawingContextHelper.RenderAsync(canvas, pending).GetAwaiter().GetResult();
        Assert.All(bitmap.Pixels, pixel => Assert.Equal(SKColors.Magenta, pixel));
        first.Dispose(); second.Dispose(); delayed.Dispose();
    }

    [Fact]
    public void PreviewPictureOwnerDisposesItsNativePictureExactlyOnce()
    {
        using var recorder = new SKPictureRecorder();
        recorder.BeginRecording(SKRect.Create(20, 20)).Clear(SKColors.White);
        var picture = recorder.EndRecording();
        var owner = new ReportPagePicture(picture);
        Assert.NotEqual(IntPtr.Zero, picture.Handle);
        owner.Dispose(); owner.Dispose();
        Assert.Equal(IntPtr.Zero, picture.Handle);
    }

    [Fact]
    public void PreviewZoomUsesFixedStepsAndFitFollowsOnlyViewportChanges()
    {
        var (main, _) = Open(ReportTestProjects.Create(true));
        Assert.True(main.ReportAsync().GetAwaiter().GetResult());
        var preview = main.ReportPreview!;
        preview.UpdateViewport(400, 600); double fit = preview.Zoom;
        Assert.True(preview.IsPageFit); Assert.True(fit > .25 && fit < .5);
        preview.ZoomInCommand.Execute(null); Assert.Equal(.5, preview.Zoom); Assert.False(preview.IsPageFit);
        preview.UpdateViewport(1200, 800); Assert.Equal(.5, preview.Zoom);
        preview.ShowPage(1); Assert.Equal(.5, preview.Zoom);
        preview.ZoomOutCommand.Execute(null); Assert.Equal(.25, preview.Zoom); Assert.False(preview.ZoomOutCommand.CanExecute(null));
        foreach (double step in new[] { .5, .75, 1, 1.25, 1.5, 2, 3, 4 })
        { preview.ZoomInCommand.Execute(null); Assert.Equal(step, preview.Zoom); Assert.Equal($"{step * 100:0} %", preview.ZoomLabel); }
        Assert.False(preview.ZoomInCommand.CanExecute(null));
        foreach (double step in new[] { 3, 2, 1.5, 1.25, 1, .75, .5, .25 })
        { preview.ZoomOutCommand.Execute(null); Assert.Equal(step, preview.Zoom); }
        preview.PageFitCommand.Execute(null); Assert.True(preview.IsPageFit); Assert.NotEqual(fit, preview.Zoom);
        preview.UpdateViewport(80, 100); Assert.True(preview.Zoom < .25); Assert.False(preview.ZoomOutCommand.CanExecute(null));
        preview.ZoomInCommand.Execute(null); Assert.Equal(.25, preview.Zoom);
        preview.UpdateViewport(5000, 7000); preview.PageFitCommand.Execute(null);
        Assert.True(preview.Zoom > 4); Assert.False(preview.ZoomInCommand.CanExecute(null));
        preview.ZoomOutCommand.Execute(null); Assert.Equal(4, preview.Zoom);
        main.CloseReport();
        Assert.True(main.ReportAsync().GetAwaiter().GetResult());
        Assert.Equal(0, main.ReportPreview!.PageIndex); Assert.True(main.ReportPreview.IsPageFit);
        main.CloseReport();
    }

    [Theory]
    [InlineData("de-DE", "75", .75)] [InlineData("en-US", "75 %", .75)]
    [InlineData("de-DE", "125", 1.25)] [InlineData("en-US", " 125% ", 1.25)]
    [InlineData("de-DE", "87,5 %", .875)] [InlineData("en-US", "87.5", .875)]
    [InlineData("de-DE", "25", .25)] [InlineData("en-US", "400 %", 4)]
    public void InlineZoomCommitsManualPercentageWithoutMovingTheToolbar(string language, string text, double zoom)
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
        var (main, files) = Open(ReportTestProjects.Create(true));
        var (window, view) = ShowReportWorkspace(main, files, 1100, 650);
        try
        {
            var preview = main.ReportPreview!; var picture = preview.Picture;
            var value = view.FindControl<Grid>("ZoomValue")!;
            var button = view.FindControl<Button>("ZoomValueButton")!;
            var input = view.FindControl<TextBox>("ZoomInput")!;
            var center = view.FindControl<StackPanel>("PreviewControls")!;
            var bounds = new Rect(center.TranslatePoint(default, view)!.Value, center.Bounds.Size);
            Click(button); Dispatcher.UIThread.RunJobs();
            Assert.True(input.IsVisible); Assert.True(input.IsFocused); Assert.False(button.IsVisible);
            Assert.False(view.FindControl<TextBlock>("ZoomLabel")!.IsEffectivelyVisible);
            Assert.Equal(preview.ZoomLabel, input.SelectedText);
            Assert.Equal(value.Bounds.Width, input.Bounds.Width);
            var border = Assert.Single(input.GetVisualDescendants().OfType<Border>(), b => b.Name == "PART_BorderElement");
            Assert.Same(Application.Current!.FindResource("Accent"), border.BorderBrush);
            input.Text = text; Press(input, Key.Enter); Dispatcher.UIThread.RunJobs();
            Assert.Equal(zoom, preview.Zoom); Assert.False(preview.IsPageFit); Assert.False(preview.IsPageWidthFit);
            Assert.Same(picture, preview.Picture); Assert.False(input.IsVisible); Assert.True(button.IsVisible);
            Assert.True(view.FindControl<TextBlock>("ZoomLabel")!.IsEffectivelyVisible);
            Assert.Equal(preview.ZoomLabel, view.FindControl<TextBlock>("ZoomLabel")!.Text);
            Assert.Equal(bounds, new Rect(center.TranslatePoint(default, view)!.Value, center.Bounds.Size));
            var label = view.FindControl<TextBlock>("ZoomLabel")!;
            var minus = view.FindControl<Button>("ZoomOutButton")!;
            var plus = view.FindControl<Button>("ZoomInButton")!;
            double midpoint = (minus.TranslatePoint(new(minus.Bounds.Width / 2, 0), value)!.Value.X
                + plus.TranslatePoint(new(plus.Bounds.Width / 2, 0), value)!.Value.X) / 2;
            Assert.Equal(midpoint, value.Bounds.Width / 2, .5);
            Assert.True(label.TranslatePoint(new(label.Bounds.Width, 0), value)!.Value.X <= value.Bounds.Width);
            preview.UpdateViewport(1000, 700); Assert.Equal(zoom, preview.Zoom);
            preview.ShowPage(1); Assert.Equal(zoom, preview.Zoom); Assert.False(main.Session!.IsDirty);
            preview.ZoomOutCommand.Execute(null);
            double lower = new[] { .25, .5, .75, 1, 1.25, 1.5, 2, 3, 4 }.LastOrDefault(step => step < zoom, zoom);
            Assert.Equal(lower, preview.Zoom);
            Wheel(window, view, new Point(400, 200), KeyModifiers.Control, 1);
            Assert.True(preview.Zoom > lower);
        }
        finally { window.Close(); }
    }

    [Theory]
    [InlineData("")] [InlineData("abc")] [InlineData("NaN")] [InlineData("Infinity")]
    [InlineData("0")] [InlineData("-75")] [InlineData("24")] [InlineData("401")]
    [InlineData("75%%")] [InlineData("1,000")] [InlineData("1e2")]
    public void InvalidInlineZoomStaysEditableAndEscapeDiscardsWithoutClosingTheReport(string text)
    {
        var (main, files) = Open(ReportTestProjects.Create());
        var (window, view) = ShowReportWorkspace(main, files);
        try
        {
            var preview = main.ReportPreview!; double zoom = preview.Zoom;
            var button = view.FindControl<Button>("ZoomValueButton")!;
            var input = view.FindControl<TextBox>("ZoomInput")!;
            Click(button); input.Text = text; Press(input, Key.Enter);
            Assert.Equal(zoom, preview.Zoom); Assert.True(preview.IsPageFit);
            Assert.True(input.IsFocused); Assert.True(input.IsVisible); Assert.Contains("error", input.Classes);
            Assert.Equal(Strings.ReportZoomRange, Avalonia.Automation.AutomationProperties.GetHelpText(input));
            Press(input, Key.Escape); Dispatcher.UIThread.RunJobs();
            Assert.Same(preview, main.ReportPreview); Assert.True(view.IsFocused);
            Assert.Equal(zoom, preview.Zoom); Assert.True(button.IsVisible); Assert.False(input.IsVisible);
            Assert.DoesNotContain("error", input.Classes);
            Click(button); input.Text = "125"; view.FindControl<Button>("PageFitButton")!.Focus();
            Assert.Equal(zoom, preview.Zoom); Assert.False(input.IsVisible);
            Click(button); input.Text = "125"; Press(input, Key.Escape);
            Assert.Equal(zoom, preview.Zoom); Assert.Same(preview, main.ReportPreview);
            Press(view, Key.Escape); Assert.Null(main.ReportPreview);
        }
        finally { window.Close(); }
    }

    [Fact]
    public void ZoomCommandsAndBusyStateDiscardPendingInputAndDoNotAllowStaleCommits()
    {
        var (main, files) = Open(ReportTestProjects.Create());
        var (window, view) = ShowReportWorkspace(main, files);
        try
        {
            var preview = main.ReportPreview!;
            var button = view.FindControl<Button>("ZoomValueButton")!;
            var input = view.FindControl<TextBox>("ZoomInput")!;
            Click(button); input.Text = "125"; preview.ZoomInCommand.Execute(null);
            Assert.False(input.IsVisible); double zoom = preview.Zoom;
            Assert.Equal(preview.ZoomLabel, view.FindControl<TextBlock>("ZoomLabel")!.Text);
            Click(button); input.Text = "125";
            var pending = new TaskCompletionSource(); var output = preview.OutputAsync(() => pending.Task);
            Assert.False(input.IsVisible); Assert.False(button.IsEffectivelyEnabled);
            Assert.False(preview.TrySetZoom("125")); Assert.Equal(zoom, preview.Zoom);
            pending.SetResult(); output.GetAwaiter().GetResult(); Dispatcher.UIThread.RunJobs();
            Assert.True(button.IsEffectivelyEnabled);
            main.CloseReport(); Assert.False(preview.TrySetZoom("125"));
        }
        finally { window.Close(); }
    }

    private static void Click(Button button) => typeof(Button).GetMethod("OnClick",
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(button, null);

    private static void Press(InputElement input, Key key)
    {
        var args = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key };
        input.RaiseEvent(args); Assert.True(args.Handled);
    }

    [Fact]
    public void SinglePagePreviewDisablesBothNavigationCommands()
    {
        var (main, _) = Open(ReportTestProjects.Create());
        Assert.True(main.ReportAsync().GetAwaiter().GetResult());
        var preview = main.ReportPreview!;
        Assert.Single(preview.Report.Pages);
        Assert.False(preview.PreviousCommand.CanExecute(null)); Assert.False(preview.NextCommand.CanExecute(null));
        preview.PreviousCommand.Execute(null); preview.NextCommand.Execute(null); Assert.Equal(0, preview.PageIndex);
        main.CloseReport();
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void FitTracksMainWindowResizingWhileManualZoomStaysFixed(bool widthFit)
    {
        var (main, files) = Open(ReportTestProjects.Create(true));
        var (window, view) = ShowReportWorkspace(main, files, 1100, 650);
        try
        {
            var preview = main.ReportPreview!;
            var fitCommand = widthFit ? preview.PageWidthCommand : preview.PageFitCommand;
            fitCommand.Execute(null); Dispatcher.UIThread.RunJobs(); double initial = preview.Zoom; CheckZoomLabel();
            Resize(1250, 800); Assert.True(preview.Zoom > initial);
            Assert.Equal(widthFit, preview.IsPageWidthFit); Assert.Equal(!widthFit, preview.IsPageFit);
            preview.ShowPage(1); Assert.Equal(widthFit, preview.IsPageWidthFit); CheckZoomLabel();
            preview.ZoomInCommand.Execute(null); Dispatcher.UIThread.RunJobs(); double manual = preview.Zoom; CheckZoomLabel();
            Assert.False(preview.IsPageFit); Assert.False(preview.IsPageWidthFit);
            Resize(1100, 650); Assert.Equal(manual, preview.Zoom);
            fitCommand.Execute(null); Dispatcher.UIThread.RunJobs(); CheckZoomLabel();
            Assert.Equal(initial, preview.Zoom, .002);
            var scroll = view.FindControl<ScrollViewer>("PageScroll")!;
            Assert.True(preview.PageWidth + 32 <= scroll.Viewport.Width + 1);
            if (widthFit)
            {
                Assert.Equal(scroll.Viewport.Width - 32, preview.PageWidth, 1.0);
                Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
                Assert.True(scroll.Extent.Width <= scroll.Viewport.Width + 1);
            }
            else Assert.True(preview.PageHeight + 32 <= scroll.Viewport.Height + 1);
            Assert.All(new[] { "PageFitButton", "PageWidthButton" }, name =>
                Assert.DoesNotContain("selected", view.FindControl<Button>(name)!.Classes));
        }
        finally { window.Close(); }

        void CheckZoomLabel()
        {
            string text = view.FindControl<TextBlock>("ZoomLabel")!.Text!;
            Assert.Equal(main.ReportPreview!.ZoomLabel, text); Assert.EndsWith(" %", text);
            double percent = double.Parse(text[..^2], CultureInfo.CurrentUICulture);
            Assert.InRange(Math.Abs(percent - main.ReportPreview.Zoom * 100), 0, .5);
        }

        void Resize(int width, int height)
        {
            window.Width = width; window.Height = height;
            window.Measure(new(width, height)); window.Arrange(new Rect(0, 0, width, height));
            var content = (Control)window.Content!;
            content.InvalidateMeasure(); content.Measure(new(width, height));
            content.InvalidateArrange(); content.Arrange(new Rect(0, 0, width, height));
            Dispatcher.UIThread.RunJobs(); CheckZoomLabel();
        }
    }

    [Fact]
    public void WidthFitUsesOnlyTheAvailableWidthAndRetainsItsModeAcrossPages()
    {
        var (main, _) = Open(ReportTestProjects.Create(true));
        Assert.True(main.ReportAsync().GetAwaiter().GetResult());
        var preview = main.ReportPreview!;
        preview.UpdateViewport(800, 600); preview.PageWidthCommand.Execute(null);
        double zoom = preview.Zoom;
        Assert.Equal(768, preview.PageWidth, .001); Assert.True(preview.PageHeight > 600);
        preview.UpdateViewport(800, 1000); Assert.Equal(zoom, preview.Zoom);
        preview.ShowPage(1); Assert.True(preview.IsPageWidthFit); Assert.Equal(zoom, preview.Zoom);
        preview.UpdateViewport(600, 1000); Assert.Equal(568, preview.PageWidth, .001);
        preview.ZoomOutCommand.Execute(null); Assert.False(preview.IsPageWidthFit);
        main.CloseReport();
    }

    [Fact]
    public void WheelScrollsVerticallyAndShiftScrollsHorizontallyWithoutChangingZoomOrPage()
    {
        var (main, files) = Open(ReportTestProjects.Create(true));
        var (window, view) = ShowReportWorkspace(main, files);
        try
        {
            var preview = main.ReportPreview!;
            while (preview.Zoom < 2) preview.ZoomInCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            var scroll = view.FindControl<ScrollViewer>("PageScroll")!;
            scroll.Offset = new(100, 150); Dispatcher.UIThread.RunJobs();
            Wheel(window, view, new(200, 200), KeyModifiers.None, -1);
            Assert.Equal(100, scroll.Offset.X); Assert.True(scroll.Offset.Y > 150);
            double y = scroll.Offset.Y;
            Wheel(window, view, new(200, 200), KeyModifiers.Shift, -1);
            Assert.True(scroll.Offset.X > 100); Assert.Equal(y, scroll.Offset.Y);
            Assert.Equal(2, preview.Zoom); Assert.Equal(0, preview.PageIndex);
        }
        finally { window.Close(); }
    }

    [Theory]
    [InlineData(1)] [InlineData(1.5)] [InlineData(2)]
    public void ControlWheelKeepsThePaperPointUnderTheMouseAcrossRapidZooms(double scaling)
    {
        using var scaled = new DesktopControlEnvironment(scaling);
        var (main, files) = Open(ReportTestProjects.Create(true));
        var (window, view) = ShowReportWorkspace(main, files);
        try
        {
            var preview = main.ReportPreview!;
            while (preview.Zoom < 2) preview.ZoomInCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            var scroll = view.FindControl<ScrollViewer>("PageScroll")!;
            var page = view.FindControl<ReportPageControl>("PageView")!;
            var picture = page.Page;
            scroll.Offset = new(100, 200); Dispatcher.UIThread.RunJobs();
            var pointer = new Point(300, 250);
            var local = scroll.TranslatePoint(pointer, page)!.Value;
            var relative = new Point(local.X / page.Bounds.Width, local.Y / page.Bounds.Height);
            Wheel(window, view, pointer, KeyModifiers.Control, 1, false);
            Wheel(window, view, pointer, KeyModifiers.Control | KeyModifiers.Shift, 1, false);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(4, preview.Zoom); Assert.Same(picture, page.Page); Assert.Equal(0, preview.PageIndex);
            AssertAnchor();
            Wheel(window, view, pointer, KeyModifiers.Control, -1);
            Assert.Equal(3, preview.Zoom); AssertAnchor();

            void AssertAnchor()
            {
                var actual = page.TranslatePoint(new(relative.X * page.Bounds.Width, relative.Y * page.Bounds.Height), scroll)!.Value;
                Assert.Equal(pointer.X, actual.X, 1 / scaling + .01);
                Assert.Equal(pointer.Y, actual.Y, 1 / scaling + .01);
            }
        }
        finally { window.Close(); }
    }

    [Fact]
    public void ControlWheelAccumulatesFractionsHonorsLimitsAndDropsPendingAnchorsOnPageChanges()
    {
        var (main, files) = Open(ReportTestProjects.Create(true));
        var (window, view) = ShowReportWorkspace(main, files);
        try
        {
            var preview = main.ReportPreview!;
            while (preview.Zoom < 1) preview.ZoomInCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            var scroll = view.FindControl<ScrollViewer>("PageScroll")!;
            var pointer = new Point(500, 250);
            Wheel(window, view, pointer, KeyModifiers.Control, .4); Assert.Equal(1, preview.Zoom);
            Wheel(window, view, pointer, KeyModifiers.Control, .4); Assert.Equal(1, preview.Zoom);
            Wheel(window, view, pointer, KeyModifiers.Control, .2); Assert.Equal(1.25, preview.Zoom);
            Wheel(window, view, pointer, KeyModifiers.Control, 20); Assert.Equal(4, preview.Zoom);
            var offset = scroll.Offset;
            Wheel(window, view, pointer, KeyModifiers.Control, 1); Assert.Equal(offset, scroll.Offset);
            Wheel(window, view, pointer, KeyModifiers.Control, .4);
            Wheel(window, view, pointer, KeyModifiers.Control, -1); Assert.Equal(3, preview.Zoom);
            Wheel(window, view, pointer, KeyModifiers.Control, -20); Assert.Equal(.25, preview.Zoom);
            offset = scroll.Offset;
            Wheel(window, view, pointer, KeyModifiers.Control, -1); Assert.Equal(offset, scroll.Offset);
            Wheel(window, view, pointer, KeyModifiers.Control, -.4);
            Wheel(window, view, pointer, KeyModifiers.Control, 1); Assert.Equal(.5, preview.Zoom);
            Wheel(window, view, pointer, KeyModifiers.Control, 4, false);
            preview.ShowPage(1); Dispatcher.UIThread.RunJobs(); Assert.Equal(default, scroll.Offset);
            Wheel(window, view, pointer, KeyModifiers.Control, 1, false);
            var pending = new TaskCompletionSource(); var output = preview.OutputAsync(() => pending.Task);
            double zoom = preview.Zoom;
            Wheel(window, view, pointer, KeyModifiers.Control, 1); Assert.Equal(zoom, preview.Zoom);
            Assert.Equal(default, scroll.Offset);
            pending.SetResult(); Dispatcher.UIThread.RunJobs(); output.GetAwaiter().GetResult();
            Wheel(window, view, pointer, KeyModifiers.Control, 1, false);
            main.CloseReport(); Dispatcher.UIThread.RunJobs(); Assert.Null(main.ReportPreview);
        }
        finally { window.Close(); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ControlWheelFromFitAccountsForAppearingScrollbarsAndCenteredPaper(bool widthFit)
    {
        var (main, files) = Open(ReportTestProjects.Create(true));
        var (window, view) = ShowReportWorkspace(main, files);
        try
        {
            var preview = main.ReportPreview!;
            if (widthFit) preview.PageWidthCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            var page = view.FindControl<ReportPageControl>("PageView")!;
            var scroll = view.FindControl<ScrollViewer>("PageScroll")!;
            if (widthFit) { scroll.Offset = new(0, 100); Dispatcher.UIThread.RunJobs(); }
            var pointer = new Point(scroll.Viewport.Width / 2, 250);
            var local = scroll.TranslatePoint(pointer, page)!.Value;
            var relative = new Point(local.X / page.Bounds.Width, local.Y / page.Bounds.Height);
            var picture = preview.Picture;
            Wheel(window, view, pointer, KeyModifiers.Control, 1);
            var actual = page.TranslatePoint(new(relative.X * page.Bounds.Width, relative.Y * page.Bounds.Height), scroll)!.Value;
            Assert.False(preview.IsPageFit); Assert.False(preview.IsPageWidthFit);
            Assert.Same(picture, preview.Picture); Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
            Assert.Equal(pointer.Y, actual.Y, 1.0);
            if (widthFit)
            {
                Assert.True(scroll.Extent.Width > scroll.Viewport.Width);
                Assert.Equal(pointer.X, actual.X, 1.0);
            }
            else
            {
                Assert.Equal(0, scroll.Offset.X);
                Assert.Equal(scroll.Viewport.Width / 2, actual.X, 1.0);
            }
        }
        finally { window.Close(); }
    }

    private static PointerWheelEventArgs Wheel(MainWindow window, ReportPreviewView view, Point position, KeyModifiers modifiers,
        double delta, bool layout = true)
    {
        var scroll = view.FindControl<ScrollViewer>("PageScroll")!;
        var page = view.FindControl<ReportPageControl>("PageView")!;
        var root = scroll.TranslatePoint(position, window)!.Value;
        var e = new PointerWheelEventArgs(page, new Pointer(1, PointerType.Mouse, true), window, root, 0, default, modifiers, new(0, delta));
        page.RaiseEvent(e);
        if (modifiers.HasFlag(KeyModifiers.Control)) Assert.True(e.Handled);
        if (layout) Dispatcher.UIThread.RunJobs();
        return e;
    }

    [Fact]
    public void OutputGuardsWorkspaceAndRecoversAfterErrorsAndCancellation()
    {
        var (main, files) = Open(ReportTestProjects.Create(true));
        var (window, view) = ShowReportWorkspace(main, files);
        try
        {
            var preview = main.ReportPreview!; var original = preview.Picture;
            var pending = new TaskCompletionSource();
            var output = preview.OutputAsync(() => pending.Task);
            Assert.True(main.IsWorkspaceBusy); Assert.True(preview.IsBusy); Assert.True(main.Session!.IsBusy);
            Assert.False(main.CloseReport()); Assert.False(main.RequestCloseAsync().GetAwaiter().GetResult());
            Assert.False(preview.CloseCommand.CanExecute(null)); Assert.False(preview.ZoomInCommand.CanExecute(null));
            Assert.False(preview.NextCommand.CanExecute(null));
            Assert.False(view.FindControl<Button>("SavePdfButton")!.IsEffectivelyEnabled);
            Assert.False(window.FindControl<MenuItem>("QuitMenuItem")!.IsEffectivelyEnabled);
            preview.ShowPage(1); Assert.Same(original, preview.Picture);
            bool duplicate = false; preview.OutputAsync(() => { duplicate = true; return Task.CompletedTask; }).GetAwaiter().GetResult();
            Assert.False(duplicate);
            pending.SetException(new IOException("Output failed")); Dispatcher.UIThread.RunJobs(); output.GetAwaiter().GetResult();
            Assert.False(main.IsWorkspaceBusy); Assert.False(main.Session.IsBusy);
            Assert.True(preview.HasError); Assert.Contains("Output failed", preview.Error);
            Assert.True(view.FindControl<TextBlock>("ErrorText")!.IsVisible);
            preview.OutputAsync(() => Task.CompletedTask).GetAwaiter().GetResult();
            Assert.False(preview.HasError); Assert.False(view.FindControl<TextBlock>("ErrorText")!.IsVisible);
            Assert.True(preview.CloseCommand.CanExecute(null)); Assert.Same(original, preview.Picture);
        }
        finally { window.Close(); }
    }

    [Theory]
    [InlineData(LeaveDecision.Cancel)] [InlineData(LeaveDecision.Save)] [InlineData(LeaveDecision.Discard)]
    public void ApplicationCloseKeepsOrDisposesPreviewOnlyAfterTheLeaveDecision(LeaveDecision decision)
    {
        var state = ReportTestProjects.Create(); var files = new Files();
        var dialogs = new ProjectTestSupport.Dialogs { Leave = decision };
        string path = Path.GetFullPath("close-report.spandraft"); files.Data[path] = ProjectFileCodec.Serialize(state);
        var main = new MainWindowViewModel(files: files, dialogs: dialogs);
        Assert.True(main.OpenAsync(path).GetAwaiter().GetResult());
        main.Editor!.SetAnnotationOffset(state.Document.Loads[0].Id, new(20, 10));
        Assert.True(main.ReportAsync().GetAwaiter().GetResult());
        var preview = main.ReportPreview!; var picture = preview.Picture;
        Assert.Equal(decision != LeaveDecision.Cancel, main.RequestCloseAsync().GetAwaiter().GetResult());
        Assert.Equal(1, dialogs.LeaveQuestions);
        if (decision == LeaveDecision.Cancel)
        {
            Assert.Same(preview, main.CurrentViewModel); Assert.Same(picture, preview.Picture);
            Assert.True(main.Editor.IsReportOpen); Assert.True(main.Session!.IsDirty);
            Assert.Empty(files.Writes); main.CloseReport();
        }
        else
        {
            Assert.Null(main.ReportPreview); Assert.Null(preview.Picture);
            Assert.Equal(decision == LeaveDecision.Save, files.Writes.Count == 1);
            Assert.False(main.Editor.IsReportOpen);
        }
    }

    [Fact]
    public void PreviewRetriesFailedRecordingsWithoutReplacingTheCurrentPage()
    {
        var (main, files) = Open(ReportTestProjects.Create()); using var original = ProjectReport.Capture(main.Editor!, Now);
        bool fail = true; int attempts = 0;
        var report = new ReportDocument(original.Data with
        {
            Metadata = Enumerable.Range(0, 10).Select(i => ($"Field {i}", "Value")).ToArray()
        }, new(130, (_, _, _) => { attempts++; if (fail) throw new InvalidOperationException("Recording failed"); }));
        using var preview = new ReportPreviewViewModel(report, main.Session!.FilePath, files, () => { });
        var first = Assert.IsType<ReportPagePicture>(preview.Picture);
        int graphicsPage = report.Pages.Select((p, i) => (p, i)).First(p => p.p.HasGraphics).i;
        Assert.Throws<InvalidOperationException>(() => preview.ShowPage(graphicsPage));
        Assert.Same(first, preview.Picture); Assert.Equal(1, attempts);
        fail = false; preview.ShowPage(graphicsPage);
        Assert.NotSame(first, preview.Picture); Assert.Equal(1 + report.TechnicalAreas(graphicsPage).Count, attempts);
    }

    private static (MainWindow Window, ReportPreviewView View) ShowReportWorkspace(MainWindowViewModel main, Files files, int width = 1250, int height = 800)
    {
        var window = new MainWindow(new DesktopStartupContext(files, new LocalSettingsStore(files, "settings"), UserSettings.Default))
            { Width = width, Height = height };
        window.Show(); Dispatcher.UIThread.RunJobs();
        typeof(MainWindow).GetField("_model", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(window, main);
        window.DataContext = main;
        Assert.True(main.ReportAsync().GetAwaiter().GetResult());
        window.Measure(new(width, height)); window.Arrange(new Rect(0, 0, width, height)); Dispatcher.UIThread.RunJobs();
        return (window, Assert.Single(window.GetVisualDescendants().OfType<ReportPreviewView>()));
    }

    private sealed class PendingReportDraw(ICustomDrawOperation operation) : Control
    {
        public override void Render(DrawingContext context) => context.Custom(operation);
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

    private static (string Text, SKRect Bounds)[] SectionDesignationRuns(ReportDocument report, int page)
    {
        var area = report.TechnicalAreas(page).Single(a => a.Kind == ReportGraphic.Section).Bounds;
        var heading = Assert.Single(report.TextRuns, r => r.Text == report.Data.Labels.Section
            && r.Bounds.Left == area.Left && r.Bounds.Top < area.Top).Bounds;
        var lines = report.TextRuns.Where(r => r.Bounds.Left >= area.Left && r.Bounds.Top > heading.Bottom
            && r.Bounds.Bottom <= area.Top).ToArray();
        Assert.NotEmpty(lines);
        Assert.All(lines, r => Assert.True(r.Bounds.Right <= area.Right + .01f, r.Text));
        return lines;
    }

    private static (string Text, SKRect Bounds)[] MaterialDesignationRuns(ReportDocument report)
    {
        var heading = Assert.Single(report.TextRuns, r => r.Text == report.Data.Labels.Material).Bounds;
        var properties = Assert.Single(report.TextRuns, r => r.Text.Contains(report.Data.MaterialProperties[0].PlainText)).Bounds;
        var lines = report.TextRuns.Where(r => r.Bounds.Left >= heading.Left && r.Bounds.Top > heading.Bottom
            && r.Bounds.Bottom < properties.Top).ToArray();
        Assert.NotEmpty(lines);
        Assert.All(lines, r => Assert.True(r.Bounds.Right <= heading.Left + ReportDocument.RightWidth + .01f, r.Text));
        return lines;
    }

    private static void AssertInformationBlocksSeparated(ReportDocument report, int page)
    {
        AssertTableHeadersAndBounds(report, page);
        if (!report.Pages[page].HasGraphics) return;
        var sectionArea = report.TechnicalAreas(page).Single(a => a.Kind == ReportGraphic.Section).Bounds;
        var sectionHeading = Assert.Single(report.TextRuns, r => r.Text == report.Data.Labels.Section
            && r.Bounds.Left == sectionArea.Left && r.Bounds.Top < sectionArea.Top);
        Assert.All(report.TextRuns.Where(r => r.Bounds.Top < report.Pages[page].BodyTop - 12),
            r => Assert.True(r.Bounds.Bottom < sectionHeading.Bounds.Top, r.Text));
        var designation = SectionDesignationRuns(report, page);
        var materialHeading = Assert.Single(report.TextRuns, r => r.Text == report.Data.Labels.Material);
        Assert.Equal(materialHeading.Bounds.Left, sectionHeading.Bounds.Left);
        Assert.Equal(materialHeading.Bounds.Height, sectionHeading.Bounds.Height, 4);
        Assert.True(sectionHeading.Bounds.Bottom < designation[0].Bounds.Top);
        Assert.True(designation[^1].Bounds.Bottom <= sectionArea.Top);
        var sectionProperties = report.TextRuns.Where(r => r.Bounds.Left >= sectionArea.Left
            && r.Bounds.Right <= sectionArea.Right + .01f && r.Bounds.Top >= sectionArea.Bottom
            && r.Bounds.Bottom < materialHeading.Bounds.Top).ToArray();
        Assert.NotEmpty(sectionProperties);
        var materialProperties = Assert.Single(report.TextRuns, r => r.Text.Contains(report.Data.MaterialProperties[0].PlainText));
        Assert.All(report.Data.SectionProperties, p => Assert.Contains(p.PlainText, string.Join(" ", sectionProperties.Select(r => r.Text))));
        var tables = report.Pages[page].Tables;
        var materialPropertyRuns = report.TextRuns.Where(r => r.Bounds.Left >= sectionArea.Left
            && r.Bounds.Right <= sectionArea.Right + .01f && r.Bounds.Top >= materialProperties.Bounds.Top
            && (tables.Count == 0 || r.Bounds.Top < tables[0].Bounds.Top)
            && report.Data.MaterialProperties.Any(p => r.Text.Contains(p.PlainText, StringComparison.Ordinal) || p.PlainText.Contains(r.Text, StringComparison.Ordinal))).ToArray();
        string materialText = string.Join(" ", materialPropertyRuns.Select(r => r.Text));
        Assert.All(report.Data.MaterialProperties, p => Assert.Contains(p.PlainText, materialText));
        Assert.All(sectionProperties, run => Assert.True(sectionArea.Bottom <= run.Bounds.Top && run.Bounds.Bottom < materialHeading.Bounds.Top));
        Assert.True(materialHeading.Bounds.Bottom < materialProperties.Bounds.Top);
        foreach (var run in sectionProperties.Concat(new[] { materialHeading, materialProperties }))
        {
            Assert.True(run.Bounds.Left >= sectionArea.Left);
            Assert.True(run.Bounds.Right <= sectionArea.Right + .01f);
        }
        if (tables.Count > 0) Assert.All(materialPropertyRuns, run => Assert.True(run.Bounds.Bottom < tables[0].Bounds.Top));
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
            foreach (string header in report.Data.Tables[fragment.Table].Headers.Select(h => h.PlainText))
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
