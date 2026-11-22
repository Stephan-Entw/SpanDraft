using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using SpanDraft.Desktop;
using SpanDraft.Desktop.Diagnostics;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.ViewModels;
using Xunit;
using static SpanDraft.Tests.ProjectTestSupport;

namespace SpanDraft.Tests;

[Collection("Avalonia")]
public sealed class DesktopDiagnosticsControlTests
{
    private static readonly DiagnosticsEnvironment Environment = new("9.8.7", "TestOS 42", "Arm64", ".NET 10.0.0");

    private sealed class Frames : IInteractionFrameProbe
    {
        public readonly List<TaskCompletionSource<long?>> Requests = [];
        public Task<long?> ObserveNextFrameAsync(TimeProvider clock, CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource<long?>();
            lock (Requests) Requests.Add(completion);
            return completion.Task.WaitAsync(cancellationToken);
        }
    }

    private static void RecordEntry(InteractionPerformance recorder, Frames frames, InteractionAction action)
    {
        using (recorder.Begin(action)) { recorder.PresentationStarting(); recorder.Committed(); }
        frames.Requests[^1].SetResult(Stopwatch());
        static long Stopwatch() => System.Diagnostics.Stopwatch.GetTimestamp();
    }

    // The inert platform has no render timer; drive Avalonia's real server pass explicitly.
    private static void Render(Compositor compositor)
    {
        var server = typeof(Compositor).GetField("_server", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(compositor)!;
        server.GetType().GetMethod("Render")!.Invoke(server, [false]);
    }

    // The inert platform has no screens, so the dialog is laid out through its own window without being shown.
    private static void Arrange(Window window, double width = 0, double height = 0)
    {
        if (width == 0) width = window.Width;
        if (height == 0) height = window.Height;
        window.ApplyTemplate();
        for (int pass = 0; pass < 3; pass++)
        {
            window.Measure(new Size(width, height)); window.Arrange(new Rect(0, 0, width, height));
            var content = (Control)window.Content!;
            // Visibility changes do not reach a window that is not rooted; invalidate explicitly like the settings tests.
            foreach (var child in content.GetVisualDescendants().OfType<Control>())
            { child.InvalidateMeasure(); child.InvalidateArrange(); }
            content.InvalidateMeasure(); content.InvalidateArrange();
            content.Measure(new Size(width, height)); content.Arrange(new Rect(0, 0, width, height));
            Dispatcher.UIThread.RunJobs();
        }
    }

    // Visibility within the dialog's content; the window itself is never shown here.
    private static bool Shown(Control control, Window window) =>
        control.GetSelfAndLogicalAncestors().TakeWhile(c => c != window).OfType<Control>().All(c => c.IsVisible);

    private static string[] Texts(Control root, Window window) => root.GetVisualDescendants().OfType<TextBlock>()
        .Where(t => Shown(t, window)).Select(t => t.Text ?? string.Empty).ToArray();

    [Fact]
    public void DiagnosticsDialogHasNoMenuRow()
    {
        using var environment = new DesktopControlEnvironment();
        var dialog = new DiagnosticsWindow();
        Arrange(dialog);
        Assert.Empty(dialog.GetVisualDescendants().OfType<Menu>());
        dialog.Close();
    }

    [Fact]
    public void MenuOpensOneModelessDiagnosticsWindowThatShowsEntriesOfRealInteractionsLiveUntilClosed()
    {
        using var environment = new DesktopControlEnvironment();
        var files = new Files();
        string path = TestPath("diagnostics.spandraft");
        files.Data[path] = ProjectFileCodec.Serialize(State(4));
        var startup = new DesktopStartupContext(files, new LocalSettingsStore(files, "settings"), UserSettings.Default);
        using var culture = new UiCultureScope("en-US");
        var main = new MainWindow(HostWithLaunchArguments(startup, [path]));
        main.Show(); Dispatcher.UIThread.RunJobs();
        var model = Assert.IsType<MainWindowViewModel>(main.DataContext);
        var editor = Assert.IsType<EditorViewModel>(model.Editor);
        var item = main.FindControl<MenuItem>("DiagnosticsMenuItem")!;

        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        var diagnostics = Assert.IsType<DiagnosticsWindow>(Assert.Single(main.OwnedWindows));
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.Same(diagnostics, Assert.Single(main.OwnedWindows)); // No second instance.
        var view = Assert.IsType<DiagnosticsViewModel>(diagnostics.DataContext);
        Assert.Empty(view.Rows);

        // The project stays operable while the window is open; its entry arrives through the notification.
        Assert.True(main.IsEffectivelyEnabled);
        Assert.True(editor.SetAnnotationOffset(SupportId, new(1, 2)));
        WaitForRows(main, view, 1);
        var row = Assert.Single(view.Rows);
        Assert.Equal(Strings.DiagnosticsActionAnnotationOffset, row.Action);
        Assert.Equal("0.0", row.Analysis); // Non-mechanical: no analysis phase.

        diagnostics.Close(); Dispatcher.UIThread.RunJobs();
        Assert.Empty(main.OwnedWindows);
        Assert.Null(diagnostics.DataContext);
        // A reopened window starts from the current snapshot of the same history.
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        var reopened = Assert.IsType<DiagnosticsViewModel>(Assert.Single(main.OwnedWindows).DataContext);
        Assert.NotSame(view, reopened);
        Assert.Equal(row, Assert.Single(reopened.Rows));
        main.Close(); Dispatcher.UIThread.RunJobs();
    }

    private static void WaitForRows(MainWindow main, DiagnosticsViewModel model, int count)
    {
        var host = main.FindControl<ContentControl>("WorkspaceContent")!;
        var compositor = ElementComposition.GetElementVisual(host)!.Compositor;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        // Frame requests, the rendered batch and the recorder's continuation each need their own turn.
        while (model.Rows.Count < count)
        {
            Dispatcher.UIThread.RunJobs();
            Render(compositor);
            Dispatcher.UIThread.RunJobs();
            if (model.Rows.Count >= count) break;
            Thread.Yield(); // The recorder continues on its own thread; the UI thread must stay this one.
            Assert.False(timeout.IsCancellationRequested, $"Expected {count} diagnostics rows, found {model.Rows.Count}.");
        }
    }

    [Theory]
    [InlineData("en-US")] [InlineData("de-DE")]
    public void WindowShowsHeadlineSystemLineAndOneAlignedTableWithoutAnySwitch(string language)
    {
        using var environment = new DesktopControlEnvironment();
        using var culture = new UiCultureScope(language);
        var frames = new Frames(); var recorder = new InteractionPerformance(frames);
        foreach (var action in new[] { InteractionAction.PointForce, InteractionAction.SelfWeightVisibility, InteractionAction.Undo })
            RecordEntry(recorder, frames, action);
        var model = new DiagnosticsViewModel(recorder, Environment, TimeZoneInfo.Utc, action => action());
        var window = new DiagnosticsWindow(model);
        Arrange(window);

        // Everything is visible from the start; there is no mode switch and no label-less value row.
        Assert.Empty(window.GetVisualDescendants().OfType<CheckBox>());
        Assert.Equal($"{ApplicationMetadata.Product} 9.8.7", window.FindControl<TextBlock>("Headline")!.Text);
        Assert.Equal("TestOS 42 · Arm64 · .NET 10.0.0", window.FindControl<SelectableTextBlock>("SystemLine")!.Text);
        // Below the table there is nothing but the discreet copy button, at the right edge.
        var copy = window.FindControl<Button>("CopyButton")!;
        Assert.Equal(Strings.DiagnosticsCopy, copy.Content);
        Assert.True(Box(copy, window).Top >= Box(window.FindControl<ItemsControl>("HistoryRows")!, window).Bottom - 1);
        Assert.Equal(window.Width - 24, Box(copy, window).Right, 0.5);
        Assert.Equal(Strings.DiagnosticsDuration, window.FindControl<TextBlock>("DurationCaption")!.Text);
        Assert.DoesNotContain(Strings.DiagnosticsEmpty, Texts((Control)window.Content!, window));
        var header = window.FindControl<Grid>("HistoryHeader")!;
        string[] headings = header.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Name != "DurationCaption").Select(t => t.Text!).ToArray();
        Assert.Equal(new[] { Strings.DiagnosticsTime, Strings.DiagnosticsAction, Strings.DiagnosticsPreparation,
            Strings.DiagnosticsAnalysis, Strings.DiagnosticsPresentation, Strings.DiagnosticsUi, Strings.DiagnosticsTotal }, headings);
        var rows = RowGrids(window);
        Assert.Equal(3, rows.Length);
        Assert.Equal(new[] { Strings.DiagnosticsActionUndo, Strings.DiagnosticsActionSelfWeightVisibility, Strings.DiagnosticsActionPointForce },
            rows.OrderBy(r => r.TranslatePoint(default, window)!.Value.Y).Select(r => ((TextBlock)r.Children[1]).Text));

        AssertAligned(window, window.Width, header, rows);
        AssertDurationCaptionSpansThePhaseColumns(window);
        // The heading row of the five durations is as tall as a table row, so the headings are not squeezed against the rule.
        Assert.Equal(Box(RowGrids(window)[0], window).Height, header.RowDefinitions[1].ActualHeight, 0.5);
        // The two headings without a unit stand centred over both header rows, not on the lower one.
        double headerMiddle = Box(header, window).Top + header.Bounds.Height / 2;
        foreach (int column in new[] { 0, 1 })
            Assert.Equal(headerMiddle, Box(Cell(header, column), window).Center.Y, 1.0);

        // Resizing only gives the action column more room; the number columns keep their width and stay at the right edge.
        double actionBefore = Box(Cell(header, 1), window).Width, phaseBefore = Box(Cell(header, 3), window).Width;
        double totalRightBefore = Box(Cell(header, 6), window).Right;
        Arrange(window, window.Width + 200, window.Height);
        Assert.Equal(actionBefore + 200, Box(Cell(header, 1), window).Width, 1.0);
        Assert.Equal(phaseBefore, Box(Cell(header, 3), window).Width, 0.5);
        Assert.Equal(totalRightBefore + 200, Box(Cell(header, 6), window).Right, 1.0);
        AssertAligned(window, window.Width + 200, header, rows);
        window.Close();
        Assert.Null(window.DataContext);
    }

    private static Rect Box(Control c, Window window) => new(c.TranslatePoint(default, window)!.Value, c.Bounds.Size);

    private static Grid[] RowGrids(Window window) => window.FindControl<ItemsControl>("HistoryRows")!.GetVisualDescendants()
        .OfType<Grid>().Where(g => g.Classes.Contains("historyRow")).ToArray();

    // The text of one table column, whether it sits directly in the grid or inside the set-apart total cell.
    private static TextBlock Cell(Grid row, int column) => row.GetVisualDescendants().OfType<TextBlock>()
        .Where(t => t.Name != "DurationCaption").Single(t => Grid.GetColumn(t.Parent is Border b && b.Parent == row ? b : t) == column);

    // Relative invariants: every row shares the header's column edges and no text is clipped or overlaps its neighbour.
    private static void AssertAligned(Window window, double width, Grid header, Grid[] rows)
    {
        double rightEdge = width - 24 - 12 - 1; // Content margin, cell padding and the table border.
        foreach (var row in rows)
            for (int i = 0; i < 7; i++)
            {
                var cell = Cell(row, i); var head = Cell(header, i);
                var cellBox = Box(cell, window); var headBox = Box(head, window);
                Assert.True(cell.TextLayout.Width <= cell.Bounds.Width + 0.01, $"clipped: {cell.Text}");
                Assert.True(head.TextLayout.Width <= head.Bounds.Width + 0.01, $"clipped: {head.Text}");
                if (i >= 2) Assert.Equal(headBox.Right, cellBox.Right, 0.5); else Assert.Equal(headBox.Left, cellBox.Left, 0.5);
                if (i < 6) Assert.True(cellBox.Right <= Box(Cell(row, i + 1), window).Left + 0.5, "Cells overlap.");
                Assert.InRange(cellBox.Right, 0, rightEdge + 0.5);
            }
    }

    // The unit is stated once, centred above exactly the five duration columns and above their headings.
    private static void AssertDurationCaptionSpansThePhaseColumns(Window window)
    {
        var header = window.FindControl<Grid>("HistoryHeader")!;
        var caption = Box(window.FindControl<TextBlock>("DurationCaption")!, window);
        var span = Box(window.FindControl<Border>("DurationSpan")!, window);
        Assert.Equal(Box(Cell(header, 2), window).Left, span.Left, 0.5);
        Assert.Equal(Box(Cell(header, 6), window).Right, span.Right, 0.5);
        Assert.True(caption.Left >= span.Left - 0.5 && caption.Right <= span.Right + 0.5);
        Assert.True(caption.Bottom <= Box(Cell(header, 2), window).Top + 0.5, "The unit sits above the headings.");
    }

    [Theory]
    [InlineData(0)] [InlineData(3)] [InlineData(40)]
    public void HeaderIsSeparatedFromTheBodyByItsOwnRuleAndTheTableHasNoVerticalRules(int count)
    {
        using var environment = new DesktopControlEnvironment();
        using var culture = new UiCultureScope("de-DE");
        var frames = new Frames(); var recorder = new InteractionPerformance(frames);
        var model = new DiagnosticsViewModel(recorder, Environment, TimeZoneInfo.Utc, action => action());
        var window = new DiagnosticsWindow(model);
        for (int i = 0; i < count; i++) RecordEntry(recorder, frames, InteractionAction.PointForce);
        Arrange(window);
        // The rule under the header belongs to the header, so it is there whatever the body holds.
        Assert.True(window.FindControl<Border>("HeaderFrame")!.BorderThickness.Bottom > 0);
        // Columns are told apart by their spacing alone: no cell draws a vertical rule.
        var table = (Control)window.FindControl<Border>("HeaderFrame")!.Parent!;
        Assert.All(table.GetVisualDescendants().OfType<Border>(), border =>
        { Assert.Equal(0, border.BorderThickness.Left); Assert.Equal(0, border.BorderThickness.Right); });
        window.Close();
    }

    [Fact]
    public void HeaderStaysAlignedWithTheRowsOnceTheyScrollAndWhenTheScrollBarGoesAway()
    {
        using var environment = new DesktopControlEnvironment();
        using var culture = new UiCultureScope("en-US");
        var frames = new Frames(); var recorder = new InteractionPerformance(frames);
        var model = new DiagnosticsViewModel(recorder, Environment, TimeZoneInfo.Utc, action => action());
        var window = new DiagnosticsWindow(model);
        for (int i = 0; i < 40; i++) RecordEntry(recorder, frames, InteractionAction.PointForce);
        Arrange(window);
        var scroller = window.FindControl<ScrollViewer>("HistoryScroller")!;
        Assert.True(scroller.Extent.Height > scroller.Viewport.Height, "The history has to scroll for this check.");
        Assert.True(scroller.Bounds.Width > scroller.Viewport.Width, "A scroll bar takes width from the rows.");
        var header = window.FindControl<Grid>("HistoryHeader")!;
        var rows = RowGrids(window);
        Assert.NotEmpty(rows);
        // The scroll bar's width is taken from the header as well, so every column keeps its edge.
        AssertAligned(window, window.Width - (scroller.Bounds.Width - scroller.Viewport.Width), header, rows);
        window.Close();
    }

    [Fact]
    public void MinimumHeightShowsEightRowsAndATallerWindowShowsMore()
    {
        using var environment = new DesktopControlEnvironment();
        using var culture = new UiCultureScope("de-DE");
        var frames = new Frames(); var recorder = new InteractionPerformance(frames);
        var model = new DiagnosticsViewModel(recorder, Environment, TimeZoneInfo.Utc, action => action());
        var window = new DiagnosticsWindow(model);
        for (int i = 0; i < 30; i++) RecordEntry(recorder, frames, InteractionAction.PointForce);
        Arrange(window, window.MinWidth, window.MinHeight);
        var scroller = window.FindControl<ScrollViewer>("HistoryScroller")!;
        double rowHeight = RowGrids(window)[0].Parent is Border entry ? entry.Bounds.Height : throw new InvalidOperationException();
        Assert.True(scroller.Viewport.Height >= 8 * rowHeight - 0.5, $"Only {scroller.Viewport.Height / rowHeight:0.0} rows visible.");
        Assert.True(window.Height >= window.MinHeight && window.Width >= window.MinWidth); // It opens at its minimum, not smaller.
        double before = scroller.Viewport.Height;
        Arrange(window, window.MinWidth, window.MinHeight + 4 * rowHeight);
        Assert.Equal(before + 4 * rowHeight, scroller.Viewport.Height, 1.0); // The table takes all additional height.
        window.Close();
    }

    // The test thread has no UI synchronization context, so the copy completes on the calling thread.
    private static Task Copy(List<string> target, string text) { target.Add(text); return Task.CompletedTask; }

    [Fact]
    public void CopyButtonPutsTheTechnicalValuesOnTheClipboardAndConfirmsOnlyWhenItWorked()
    {
        using var environment = new DesktopControlEnvironment();
        using var culture = new UiCultureScope("en-US");
        var frames = new Frames(); var recorder = new InteractionPerformance(frames);
        RecordEntry(recorder, frames, InteractionAction.Undo);
        var model = new DiagnosticsViewModel(recorder, Environment, TimeZoneInfo.Utc, action => action());
        var copied = new List<string>(); bool fail = false;
        var window = new DiagnosticsWindow(model, text => fail ? Task.FromException(new IOException("busy")) : Copy(copied, text));
        Arrange(window);
        var button = window.FindControl<Button>("CopyButton")!;

        fail = true;
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(Strings.DiagnosticsCopy, button.Content); // A failed copy neither throws nor claims success.
        fail = false;
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(model.ClipboardText(), Assert.Single(copied));
        Assert.Equal(Strings.DiagnosticsCopied, button.Content);
        window.Close();
    }

    [Fact]
    public void ClosingTheWindowEndsItsListenerAndUpdatesNoLongerReachTheModel()
    {
        using var environment = new DesktopControlEnvironment();
        using var culture = new UiCultureScope("en-US");
        var frames = new Frames(); var recorder = new InteractionPerformance(frames);
        var model = new DiagnosticsViewModel(recorder, Environment, TimeZoneInfo.Utc, action => action());
        var window = new DiagnosticsWindow(model);
        Arrange(window);
        RecordEntry(recorder, frames, InteractionAction.PointForce);
        Assert.Equal(Strings.DiagnosticsActionPointForce, Assert.Single(model.Rows).Action); // Live while open.
        window.Close();
        Assert.Null(window.DataContext);
        RecordEntry(recorder, frames, InteractionAction.Undo);
        Assert.Equal(2, recorder.GetEntries().Count);
        Assert.Single(model.Rows); // Closed: no refresh work at all.
    }

    [Fact]
    public void EmptyHistoryKeepsTheTableFrameAndSaysSoInOneLine()
    {
        using var environment = new DesktopControlEnvironment();
        using var culture = new UiCultureScope("en-US");
        var window = new DiagnosticsWindow(new DiagnosticsViewModel(new InteractionPerformance(new Frames()), Environment));
        Arrange(window);
        Assert.Contains(Strings.DiagnosticsEmpty, Texts((Control)window.Content!, window));
        Assert.Contains(Strings.DiagnosticsTotal, Texts((Control)window.Content!, window)); // The columns are there from the start.
        Assert.False(Shown(window.FindControl<ItemsControl>("HistoryRows")!, window));
        window.Close();
    }
}
