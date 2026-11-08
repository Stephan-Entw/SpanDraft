using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SkiaSharp;
using SpanDraft.Analysis;
using SpanDraft.Desktop;
using SpanDraft.Desktop.Controls;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Reporting;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;
using SpanDraft.Desktop.Views;
using SpanDraft.Reporting;
using Xunit;

namespace SpanDraft.Tests;

public abstract class ProjectReportTestSupport : IDisposable
{
    private protected readonly CultureInfo originalCulture = CultureInfo.CurrentCulture, originalUi = CultureInfo.CurrentUICulture;
    private protected readonly DesktopControlEnvironment? environment;
    private protected ProjectReportTestSupport(bool controls = true)
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");
        if (controls)
        {
            environment = new();
            Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
        }
    }
    public void Dispose() { environment?.Dispose(); CultureInfo.CurrentCulture = originalCulture; CultureInfo.CurrentUICulture = originalUi; }
    private protected static readonly DateTimeOffset Now = new(2026, 10, 23, 12, 34, 56, TimeSpan.FromHours(2));

    private protected static (MainWindowViewModel Main, Files Files) Open(ProjectState state,
        ResultPresentationOptions? presentation = null, Action? analyzed = null)
    {
        var files = new Files();
        string path = Path.GetFullPath("report-input.spandraft"); files.Data[path] = ProjectFileCodec.Serialize(state);
        var main = new MainWindowViewModel(files: files, settings: UserSettings.Default with
            { Profile = presentation?.Profile ?? UnitProfile.Default, SignificantDigits = presentation?.SignificantDigits ?? 3,
              Notation = presentation?.Notation ?? EngineeringNotation.Automatic },
            analyze: beam => { analyzed?.Invoke(); return BeamAnalysis.Analyze(beam); });
        Assert.True(main.OpenAsync(path).GetAwaiter().GetResult());
        return (main, files);
    }

    private protected static void AssertCompleteTablesAndFinalSummary(ReportDocument report)
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

    private protected static void Click(Button button) => typeof(Button).GetMethod("OnClick",
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(button, null);

    private protected static void Press(InputElement input, Key key)
    {
        var args = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key };
        input.RaiseEvent(args); Assert.True(args.Handled);
    }

    private protected static PointerWheelEventArgs Wheel(MainWindow window, ReportPreviewView view, Point position, KeyModifiers modifiers,
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

    private protected static (MainWindow Window, ReportPreviewView View) ShowReportWorkspace(MainWindowViewModel main, Files files, int width = 1250, int height = 800)
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

    private protected sealed class PendingReportDraw(ICustomDrawOperation operation) : Control
    {
        public override void Render(DrawingContext context) => context.Custom(operation);
    }

    private protected static (string Text, SKRect Bounds)[] SectionDesignationRuns(ReportDocument report, int page)
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

    private protected static (string Text, SKRect Bounds)[] MaterialDesignationRuns(ReportDocument report)
    {
        var heading = Assert.Single(report.TextRuns, r => r.Text == report.Data.Labels.Material).Bounds;
        var properties = Assert.Single(report.TextRuns, r => r.Text.Contains(report.Data.MaterialProperties[0].PlainText)).Bounds;
        var lines = report.TextRuns.Where(r => r.Bounds.Left >= heading.Left && r.Bounds.Top > heading.Bottom
            && r.Bounds.Bottom < properties.Top).ToArray();
        Assert.NotEmpty(lines);
        Assert.All(lines, r => Assert.True(r.Bounds.Right <= heading.Left + ReportDocument.RightWidth + .01f, r.Text));
        return lines;
    }

    private protected static void AssertInformationBlocksSeparated(ReportDocument report, int page)
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

    private protected static void AssertTableHeadersAndBounds(ReportDocument report, int page)
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

    private protected static SKBitmap RenderBeam(ReportVisuals visuals)
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

    private protected static int CountPixels(SKBitmap bitmap, Rect area, Func<int, int, bool> include)
    {
        int count = 0;
        for (int y = Math.Max(0, (int)area.Top); y < Math.Min(bitmap.Height, (int)Math.Ceiling(area.Bottom)); y++)
            for (int x = Math.Max(0, (int)area.Left); x < Math.Min(bitmap.Width, (int)Math.Ceiling(area.Right)); x++)
                if (include(x, y)) count++;
        return count;
    }

    private protected static void AssertTextContained(ReportDocument report)
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
    private protected static string RepositoryRoot([System.Runtime.CompilerServices.CallerFilePath] string source = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "../.."));
    private protected sealed class Files : IProjectFileStore
    {
        internal readonly Dictionary<string, byte[]> Data = []; internal readonly List<string> Writes = [];
        internal bool FailWrites;
        public Task<byte[]?> ReadAsync(string path) => Task.FromResult(Data.GetValueOrDefault(path));
        public Task WriteAtomicAsync(string path, byte[] bytes, CancellationToken cancellationToken = default)
        { if (FailWrites) throw new IOException("Injected write failure"); Writes.Add(path); Data[path] = bytes; return Task.CompletedTask; }
        public Task DeleteAsync(string path) { Data.Remove(path); return Task.CompletedTask; }
    }
}
