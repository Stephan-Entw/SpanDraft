using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Presenters;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SpanDraft.Core.Sections.Parametric;
using SpanDraft.Desktop;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.ViewModels;
using SpanDraft.Desktop.Views;

namespace SpanDraft.StoreScreenshots;

internal sealed class CaptureApplication(WorkerRequest request, IsolatedFileStore files, string projectPath, RecentProjectEntry[] demoRecents)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private MainWindow window = null!;
    private IntPtr hwnd;
    private NativeRect fixedOuter, fixedVisible;
    private WindowSizeOverride? sizeOverride;

    internal void Start(IClassicDesktopStyleApplicationLifetime lifetime)
    {
        lifetime.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            Console.Error.WriteLine(e.Exception.Message); e.Handled = true; lifetime.Shutdown(1);
        };
        lifetime.Startup += (_, _) => Dispatcher.UIThread.Post(async () =>
        {
            int code = 1;
            try
            {
                window = (MainWindow)lifetime.MainWindow!;
                await window.Initialization.WaitAsync(Timeout);
                hwnd = window.TryGetPlatformHandle()?.Handle ?? throw new InvalidOperationException("Missing native HWND.");
                NativeWindows.AssertOwnWindow(hwnd);
                await Calibrate();
                await CaptureAll();
                code = 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error.Message); }
            finally { sizeOverride?.Dispose(); lifetime.Shutdown(code); }
        });
    }

    private async Task Calibrate()
    {
        var currentDisplay = DisplaySnapshot.RequireTarget(NativeWindows.Displays(), request.Display.Target);
        if (!currentDisplay.Devices.SequenceEqual(request.Display.Devices))
            throw new InvalidOperationException("Display configuration changed before window positioning.");
        var display = currentDisplay.CaptureDisplay;
        window.WindowState = WindowState.Normal;
        var initial = NativeWindows.WindowRect(hwnd);
        NativeWindows.Position(hwnd, display.X + 32, display.Y + 32, initial.Width, initial.Height);
        var deadline = Stopwatch.StartNew();
        while (NativeWindows.GetDpiForWindow(hwnd) != 192 || window.RenderScaling != 2)
        {
            if (deadline.Elapsed > Timeout) throw new TimeoutException("Window did not adopt native 192 DPI on the selected display.");
            await RenderFrame();
        }
        await Settle();
        var outer = NativeWindows.WindowRect(hwnd); var visible = NativeWindows.DwmRect(hwnd);
        var size = WindowSizing.OuterSize(request.Size, outer, visible);
        sizeOverride = new WindowSizeOverride(hwnd, size.Width, size.Height);
        NativeWindows.Position(hwnd, display.X + (display.Width - size.Width) / 2,
            display.Y + (display.Height - size.Height) / 2, size.Width, size.Height);
        await Settle();
        fixedOuter = NativeWindows.WindowRect(hwnd); fixedVisible = NativeWindows.DwmRect(hwnd);
        if ((fixedVisible.Width, fixedVisible.Height) != request.Size.Pixels)
            throw new InvalidOperationException("Native window sizing failed; refusing to crop or resize PNGs.");
    }

    private async Task CaptureAll()
    {
        var main = (MainWindowViewModel)window.DataContext!;
        CaptureFiles.AssertDemoRecents(demoRecents, request.BuildFinishedUtc);
        if (!main.StartPage.RecentProjects.Select(r => r.FilePath).SequenceEqual(demoRecents.Select(r => r.Path)))
            throw new InvalidOperationException("Unexpected recent projects in isolated start page.");
        foreach (var recent in main.StartPage.RecentProjects) CaptureFiles.AssertNoPersonalPaths(recent.FilePath);
        var evidence = new List<CaptureEvidence>();
        string? selectedBuiltInMaterial = null;
        await Take("Start", MainWorkspaceKind.Start);
        if (request.Scenes.Any(s => s != "Start"))
        {
            if (!await main.OpenAsync(projectPath).WaitAsync(Timeout)) throw new InvalidOperationException("Could not open the supplied demo project.");
            if (main.Session?.IsDirty != false || main.Editor?.Presentation.IsSuccess != true)
                throw new InvalidOperationException("Demo project must be unchanged and solve successfully.");
            await Take("Project", MainWorkspaceKind.BeamEditor);
            if (request.Scenes.Contains("Section"))
            {
                main.EditProject(); main.Setup.ShowSections();
                // The existing fixture supplies the section. No synthetic replacement model is needed.
                var section = main.Editor!.Document.Section as ISectionGeometry
                    ?? throw new InvalidOperationException("The fixture does not contain an I-profile.");
                if (section.Height.Meters != .1 || section.Width.Meters != .08 || section.FlangeThickness.Meters != .012
                    || section.WebThickness.Meters != .01 || section.Radius.Meters != .006)
                    throw new InvalidOperationException("Fixture I-profile dimensions do not match the requested motif.");
                await Take("Section", MainWorkspaceKind.SectionDefinition);
                main.Setup.Definition!.CancelCommand.Execute(null);
                if (!await main.CancelSetupAsync().WaitAsync(Timeout)) throw new InvalidOperationException("Could not leave the section editor without applying changes.");
            }
            if (request.Scenes.Contains("Report"))
            {
                if (!await main.ReportAsync().WaitAsync(Timeout) || main.ReportPreview?.PageIndex != 0 || main.ReportPreview.HasError)
                    throw new InvalidOperationException("Could not generate the demo project's report preview.");
                await Take("Report", MainWorkspaceKind.ReportPreview);
                main.CloseReport();
            }
            if (request.Scenes.Contains("Materials"))
            {
                main.EditProject(); main.Setup.ShowMaterials();
                var materials = main.Setup.CurrentStep as MaterialSelectionViewModel
                    ?? throw new InvalidOperationException("The real material browser did not open.");
                materials.SelectedFilter = materials.BuiltInFilter;
                materials.SearchText = "";
                materials.SelectedEntry = materials.BuiltInEntries.Single(e => e.Material.Name == "S355JR" && e.Material.MaterialNumber == "1.0045");
                await Settle();
                var view = window.GetVisualDescendants().OfType<MaterialSelectionView>().Single();
                var list = view.FindControl<ListBox>("MaterialEntries")!;
                list.ScrollIntoView(materials.SelectedEntry);
                await Settle();
                if (!materials.IsBuiltInFilter || materials.SelectedEntry is not { IsBuiltIn: true } selected
                    || materials.FilteredEntries.Count < 2 || materials.IsEditing
                    || list.ContainerFromItem(selected) is not Control { IsEffectivelyVisible: true } selectedRow)
                    throw new InvalidOperationException("The built-in material list and S355JR selection must be visible.");
                AssertInViewport(selectedRow, list.GetVisualDescendants().OfType<ScrollContentPresenter>().First());
                selectedBuiltInMaterial = selected.DisplayName;
                await Take("Materials", MainWorkspaceKind.MaterialSelection);
                materials.BackCommand.Execute(null);
                if (!await main.CancelSetupAsync().WaitAsync(Timeout)) throw new InvalidOperationException("Could not leave the material browser without applying changes.");
            }
            if (main.Session?.IsDirty != false) throw new InvalidOperationException("The screenshot run changed the demo session.");
        }
        CaptureFiles.AssertFixture(request.Fixture);
        CaptureFiles.AssertFixture(projectPath);
        foreach (string stateFile in Directory.EnumerateFiles(Path.Combine(request.RunRoot, "State")))
            CaptureFiles.AssertNoPersonalPaths(File.ReadAllText(stateFile));
        CaptureFiles.Write(Path.Combine(request.RunRoot, "worker-result.json"), new WorkerResult(evidence.ToArray(),
            CaptureFiles.Hash(request.Fixture), files.AccessedPaths.Select(p => Path.GetRelativePath(request.RepoRoot, p)).ToArray(),
            demoRecents, selectedBuiltInMaterial));

        async Task Take(string scene, MainWorkspaceKind workspace)
        {
            if (!request.Scenes.Contains(scene)) return;
            await Settle();
            var display = DisplaySnapshot.RequireTarget(NativeWindows.Displays(), request.Display.Target);
            if (!display.Devices.SequenceEqual(request.Display.Devices))
                throw new InvalidOperationException("Display configuration changed during the capture run.");
            if (main.Workspace != workspace || main.IsWorkspaceBusy) throw new InvalidOperationException("Unexpected or busy screenshot workspace.");
            if (scene == "Start") AssertStartPage();
            var outer = NativeWindows.WindowRect(hwnd); var visible = NativeWindows.DwmRect(hwnd);
            var client = NativeWindows.ClientRect(hwnd);
            if (outer != fixedOuter || visible != fixedVisible || window.RenderScaling != 2 || NativeWindows.GetDpiForWindow(hwnd) != 192)
                throw new InvalidOperationException("Window bounds or native rasterization changed between motifs.");
            if (Math.Abs(client.Width - window.ClientSize.Width * 2) > 1 || Math.Abs(client.Height - window.ClientSize.Height * 2) > 1)
                throw new InvalidOperationException("Native client pixels do not match the application's 2x layout.");
            var chrome = NativeWindows.Caption(hwnd);
            string file = CaptureFiles.FileName(scene, request.Language);
            string output = Path.Combine(request.RunRoot, file);
            await Program.RunProcess(request.WinApp, ["ui", "screenshot", "--window", hwnd.ToInt64().ToString(System.Globalization.CultureInfo.InvariantCulture),
                "--output", output, "--json"], Timeout);
            var pixels = CaptureFiles.PngSize(output);
            if (pixels != request.Size.Pixels || visible != NativeWindows.DwmRect(hwnd))
                throw new InvalidDataException("WinApp PNG does not match the complete native window.");
            using var process = Process.GetCurrentProcess();
            var started = new DateTimeOffset(process.StartTime.ToUniversalTime());
            string executable = process.MainModule!.FileName;
            if (started <= request.BuildFinishedUtc || !Path.GetFullPath(executable).Equals(Path.GetFullPath(Environment.ProcessPath!), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Capture process provenance check failed.");
            evidence.Add(new(request.Language, scene, file, pixels.Width, pixels.Height, 192, window.RenderScaling,
                outer, visible, client, chrome, hwnd.ToInt64(), Environment.ProcessId,
                Path.GetRelativePath(request.RepoRoot, executable), started));
        }
    }

    private void AssertStartPage()
    {
        var view = window.GetVisualDescendants().OfType<StartPageView>().Single();
        var list = view.FindControl<ItemsControl>("RecentProjectsList")!;
        var viewport = list.FindAncestorOfType<ScrollContentPresenter>()
            ?? throw new InvalidOperationException("The recent-project list has no viewport.");
        var rows = list.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("recentProject")).ToArray();
        if (rows.Length != demoRecents.Length) throw new InvalidOperationException("All five recent-project rows must be rendered.");
        for (int i = 0; i < rows.Length; i++)
        {
            var path = demoRecents[i].Path;
            if (rows[i].DataContext is not RecentProjectItemViewModel recent || recent.FilePath != path
                || !rows[i].GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == path))
                throw new InvalidOperationException("A recent-project row does not display its neutral demo path.");
            AssertInViewport(rows[i], viewport);
        }
        foreach (var text in view.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible && t.Text is not null))
        {
            CaptureFiles.AssertNoPersonalPaths(text.Text!);
            if (text.Text!.Contains(request.RepoRoot, StringComparison.OrdinalIgnoreCase)
                || text.Text.Contains("artifacts", StringComparison.OrdinalIgnoreCase)
                || text.Text.Contains(".runs", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("An internal tooling path is visible on the start page.");
        }
    }

    private static void AssertInViewport(Control row, Control viewport)
    {
        if (row.TranslatePoint(default, viewport) is not { } origin
            || !new Rect(viewport.Bounds.Size).Contains(new Rect(origin, row.Bounds.Size)))
            throw new InvalidOperationException("A required screenshot row is outside its viewport.");
    }

    private async Task Settle()
    {
        var deadline = Stopwatch.StartNew();
        NativeRect? previous = null;
        int stable = 0;
        while (stable < 2)
        {
            if (deadline.Elapsed > Timeout) throw new TimeoutException("Layout and native window bounds did not stabilize.");
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            await RenderFrame();
            if (window.OwnedWindows.Count != 0)
                throw new InvalidOperationException("An owned window or native dialog would contaminate the HWND capture.");
            var rect = NativeWindows.DwmRect(hwnd);
            bool valid = window.IsMeasureValid && window.IsArrangeValid
                && window.GetVisualDescendants().OfType<Control>().Where(c => c.IsEffectivelyVisible)
                    .All(c => c.IsMeasureValid && c.IsArrangeValid);
            stable = valid && rect == previous ? stable + 1 : 0;
            previous = rect;
        }
        NativeWindows.Flush();
    }

    private async Task RenderFrame()
    {
        var frame = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        window.RequestAnimationFrame(_ => frame.TrySetResult());
        await frame.Task.WaitAsync(Timeout);
        window.InvalidateVisual();
        var compositor = ElementComposition.GetElementVisual(window)?.Compositor
            ?? throw new InvalidOperationException("The real window has no compositor.");
        var batch = compositor.RequestCompositionBatchCommitAsync();
        await batch.Rendered.WaitAsync(Timeout);
    }
}
