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
    private static void Arrange(Window window, double width = 720, double height = 700)
    {
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
    public void HelpMenuOffersDiagnosticsBetweenUserManualAndAboutAndDialogsHaveNoMenuRow()
    {
        using var environment = new DesktopControlEnvironment();
        var window = new MainWindow();
        var help = Assert.IsType<MenuItem>(window.FindControl<MenuItem>("DiagnosticsMenuItem")!.Parent);
        Assert.Equal(Strings.Help, help.Header);
        Assert.Equal(new object?[] { Strings.UserManual, Strings.Diagnostics, null, Strings.About },
            help.Items.OfType<Control>().Select(i => (i as MenuItem)?.Header).ToArray());
        Assert.Equal("DiagnosticsMenuItem", ((MenuItem)help.Items.OfType<Control>().ElementAt(1)).Name);
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
        var main = new MainWindow(startup, [path]);
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
        Assert.Equal("0.0 ms", row.Analysis); // Non-mechanical: no analysis phase.

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
    public void NormalViewShowsOnlyEnvironmentAndNoteAndExtendedViewAlignsTheSevenColumns(string language)
    {
        using var environment = new DesktopControlEnvironment();
        using var culture = new UiCultureScope(language);
        var frames = new Frames(); var recorder = new InteractionPerformance(frames);
        foreach (var action in new[] { InteractionAction.PointForce, InteractionAction.SelfWeightVisibility, InteractionAction.Undo })
            RecordEntry(recorder, frames, action);
        var model = new DiagnosticsViewModel(recorder, Environment, TimeZoneInfo.Utc, action => action());
        var window = new DiagnosticsWindow(model);
        Arrange(window);

        var info = window.FindControl<Grid>("EnvironmentInfo")!;
        Assert.Equal(new[] { Strings.VersionLabel, "9.8.7", Strings.DiagnosticsOperatingSystem, "TestOS 42",
            Strings.DiagnosticsArchitecture, "Arm64", Strings.DiagnosticsRuntime, ".NET 10.0.0" }, Texts(info, window));
        Assert.Contains(Strings.DiagnosticsLocalNote, Texts((Control)window.Content!, window));
        Assert.False(Shown(window.FindControl<StackPanel>("AdvancedPanel")!, window));
        Assert.Null(window.FindControl<ItemsControl>("HistoryRows")!.ItemsSource); // Nothing bound while hidden.
        Assert.DoesNotContain(Strings.DiagnosticsFrameNote, Texts((Control)window.Content!, window));

        window.FindControl<CheckBox>("AdvancedOption")!.IsChecked = true;
        Arrange(window);
        var panel = window.FindControl<StackPanel>("AdvancedPanel")!;
        Assert.True(Shown(panel, window));
        Assert.Contains(Strings.DiagnosticsFrameNote, Texts((Control)window.Content!, window));
        Assert.DoesNotContain(Strings.DiagnosticsEmpty, Texts((Control)window.Content!, window));
        var header = window.FindControl<Grid>("HistoryHeader")!;
        Assert.Equal(new[] { Strings.DiagnosticsTime, Strings.DiagnosticsAction, Strings.DiagnosticsPreparation,
            Strings.DiagnosticsAnalysis, Strings.DiagnosticsPresentation, Strings.DiagnosticsUi, Strings.DiagnosticsTotal },
            header.Children.OfType<TextBlock>().Select(t => t.Text));
        var rows = window.FindControl<ItemsControl>("HistoryRows")!.GetVisualDescendants().OfType<Grid>()
            .Where(g => g.Children.Count == 7).ToArray();
        Assert.Equal(3, rows.Length);
        Assert.Equal(new[] { Strings.DiagnosticsActionUndo, Strings.DiagnosticsActionSelfWeightVisibility, Strings.DiagnosticsActionPointForce },
            rows.OrderBy(r => r.TranslatePoint(default, window)!.Value.Y).Select(r => ((TextBlock)r.Children[1]).Text));

        // Relative invariants: every row shares the header's column edges, text stays inside its column and the window.
        Rect Box(Control c) => new(c.TranslatePoint(default, window)!.Value, c.Bounds.Size);
        double windowWidth = ((Control)window.Content!).Bounds.Width;
        foreach (var row in rows)
            for (int i = 0; i < 7; i++)
            {
                var cell = (TextBlock)row.Children[i]; var head = header.Children.OfType<TextBlock>().ElementAt(i);
                var cellBox = Box(cell); var headBox = Box(head);
                Assert.True(cell.TextLayout.Width <= cell.Bounds.Width + 0.01, $"clipped: {cell.Text}");
                if (i >= 2) Assert.Equal(headBox.Right, cellBox.Right, 0.5); else Assert.Equal(headBox.Left, cellBox.Left, 0.5);
                if (i < 6) Assert.True(cellBox.Right <= Box((Control)row.Children[i + 1]).Left + 0.5, "Cells overlap.");
                Assert.InRange(cellBox.Right, 0, windowWidth + 0.5);
            }
        window.Close();
        Assert.Null(window.DataContext);
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
    public void ExtendedViewWithoutEntriesExplainsThatNothingWasRecordedYet()
    {
        using var environment = new DesktopControlEnvironment();
        using var culture = new UiCultureScope("en-US");
        var window = new DiagnosticsWindow(new DiagnosticsViewModel(new InteractionPerformance(new Frames()), Environment));
        Arrange(window);
        window.FindControl<CheckBox>("AdvancedOption")!.IsChecked = true;
        Arrange(window);
        Assert.Contains(Strings.DiagnosticsEmpty, Texts((Control)window.Content!, window));
        Assert.False(Shown(window.FindControl<ItemsControl>("HistoryRows")!, window));
        window.Close();
    }
}
