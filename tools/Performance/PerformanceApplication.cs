using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using SpanDraft.Desktop;
using SpanDraft.Desktop.Diagnostics;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.ViewModels;

namespace SpanDraft.Performance;

internal sealed class MemoryProjectStore : IProjectFileStore
{
    private readonly ConcurrentDictionary<string, byte[]> _data = new();
    public Task<byte[]?> ReadAsync(string path) => Task.FromResult(_data.TryGetValue(path, out var bytes) ? bytes.ToArray() : null);
    public Task WriteAtomicAsync(string path, byte[] bytes, CancellationToken cancellationToken = default)
    { cancellationToken.ThrowIfCancellationRequested(); _data[path] = bytes.ToArray(); return Task.CompletedTask; }
    public Task DeleteAsync(string path) { _data.TryRemove(path, out _); return Task.CompletedTask; }
}

internal sealed class PerformanceApplication(MeasureOptions options, MemoryProjectStore files)
{
    private sealed class ReferenceIdentityResolver : IProjectIdentityResolver
    {
        public ProjectIdentity Resolve(string path) => new(path, caseSensitive: true);
    }

    private static readonly TimeSpan HostTimeout = TimeSpan.FromSeconds(30);
    private MainWindow _window = null!;
    private MainWindowViewModel _main = null!;
    private RunEnvironment? _environment;
    private readonly CancellationTokenSource _cancel = new();

    public static int Run(MeasureOptions options)
    {
        var files = new MemoryProjectStore();
        string root = Path.Combine(Path.GetTempPath(), "SpanDraft-performance");
        string At(string name) => Path.Combine(root, name);
        var settings = UserSettings.Default with
        {
            UiLanguage = UiLanguagePreference.English, RegionalFormat = RegionalFormatPreference.EnglishUnitedStates,
            Profile = UnitProfile.StructuralEngineering, LastStandardProfile = UnitProfileKind.StructuralEngineering
        };
        var startup = new DesktopStartupContext(files, new(files, At("settings.json")), settings)
        {
            ProjectIdentities = new ReferenceIdentityResolver(),
            UserNameProvider = static () => null,
            StateStores = new(new InMemoryRecoveryStorage(),
                new(files, At("materials.json")), new(files, At("sections.json")), new(files, At("recents.json")))
        };
        CultureConfigurator.Apply(CultureConfigurator.Resolve(settings, CultureInfo.InvariantCulture, CultureInfo.InvariantCulture));
        var host = new PerformanceApplication(options, files);
        return AppBuilder.Configure(() => new App(Initialized(new DesktopApplicationHost(startup)))).UsePlatformDetect().StartWithClassicDesktopLifetime([], host.Start);
    }

    // Like the bootstrap, the tool starts the shared services before the first window; the window never loads them.
    private static DesktopApplicationHost Initialized(DesktopApplicationHost host)
    {
        host.InitializeServicesAsync().GetAwaiter().GetResult();
        return host;
    }

    private void Start(IClassicDesktopStyleApplicationLifetime lifetime)
    {
        lifetime.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        ConsoleCancelEventHandler cancelled = (_, e) => { e.Cancel = true; _cancel.Cancel(); };
        Console.CancelKeyPress += cancelled;
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        { Console.Error.WriteLine("Desktop failure: " + e.Exception.Message); e.Handled = true; lifetime.Shutdown(1); };
        lifetime.Startup += (_, _) => Dispatcher.UIThread.Post(async () =>
        {
            int exitCode = 1;
            try
            {
                _window = (MainWindow)lifetime.MainWindow!;
                await _window.Initialization.WaitAsync(HostTimeout, _cancel.Token);
                _main = (MainWindowViewModel)_window.DataContext!;
                if (_window.TryGetPlatformHandle() is not { Handle: var handle } || handle == IntPtr.Zero || !_window.IsVisible)
                    throw new InvalidOperationException("A visible native desktop window is required.");
                _window.WindowState = WindowState.Normal; _window.Width = 1300; _window.Height = 800; _window.CanResize = false;
                _window.Activate();
                await Stabilize();
                _environment = ReadEnvironment();
                Console.WriteLine("Native desktop window ready; measuring confirmed product actions through DEV-75.");
                var started = DateTimeOffset.UtcNow;
                var results = new List<ActionResult>();
                foreach (var scenario in ReferenceScenarios.Select(options.Scenario))
                foreach (var action in ReferenceActions.Ids)
                {
                    _cancel.Token.ThrowIfCancellationRequested();
                    var reference = ReferenceScenarios.Create(scenario);
                    string path = Path.Combine(Path.GetTempPath(), "SpanDraft-performance", "reference.spandraft");
                    await files.WriteAtomicAsync(path, ProjectFileCodec.Serialize(reference));
                    if (!await _main.OpenAsync(path)) throw new InvalidOperationException("Could not open synthetic reference.");
                    if (_main.Editor?.Presentation.IsSuccess != true) throw new InvalidOperationException("Reference analysis failed.");
                    await Stabilize();
                    bool primed = false;
                    var result = await MeasurementLoop.RunAsync(scenario, action, options.Warmup, options.Iterations,
                        async () =>
                        {
                            ReferenceActions.AssertReference(_main, reference, primed);
                            CheckEnvironment();
                            var confirm = ReferenceActions.Prepare(_main, action);
                            await Stabilize();
                            var entry = await Observe(confirm, ReferenceActions.Kind(action));
                            if (_main.Session!.UndoHistory.Count != 1 || _main.Session.RedoHistory.Count != 0
                                || _main.Editor!.Presentation.IsSuccess != true || reference.ContentEquals(_main.Session.CurrentRevision.State))
                                throw new InvalidOperationException("Reference action did not create the expected single mechanical revision.");
                            CheckEnvironment();
                            return entry;
                        },
                        async () =>
                        {
                            var entry = await Observe(() => Task.FromResult(_main.Undo()), InteractionAction.Undo);
                            primed = true;
                            ReferenceActions.AssertReference(_main, reference, primed);
                            await Stabilize();
                            return entry;
                        });
                    results.Add(result);
                    Console.WriteLine($"{scenario}/{action}: {result.ValidSampleCount} samples, {result.DiscardedSampleCount} discarded.");
                    if (!await _main.CloseProjectAsync()) throw new InvalidOperationException("Could not close the synthetic reference.");
                }
                var version = typeof(MainWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "unavailable";
                var run = new RunFile(1, 1, "milliseconds", version, BuildSource.Read(typeof(Program).Assembly), started,
                    _environment, new(options.Scenario, options.Warmup, options.Iterations, "undo-to-reference"), results.ToArray());
                byte[] bytes = RunFiles.Serialize(run);
                Directory.CreateDirectory(Path.GetDirectoryName(options.Output)!);
                string temporary = options.Output + ".tmp-" + Guid.NewGuid().ToString("N");
                try { await File.WriteAllBytesAsync(temporary, bytes, _cancel.Token); File.Move(temporary, options.Output); }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
                Console.WriteLine("Measurement file written. This run does not establish a general performance baseline.");
                exitCode = 0;
            }
            catch (Exception error) { Console.Error.WriteLine("Measurement failed: " + error.Message); }
            finally { Console.CancelKeyPress -= cancelled; _cancel.Dispose(); lifetime.Shutdown(exitCode); }
        });
    }

    private async Task Stabilize()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_cancel.Token);
        deadline.CancelAfter(HostTimeout);
        if (!await _window.WaitForRenderedFrameAsync(deadline.Token))
            throw new InvalidOperationException("The native preparation frame was unavailable; no substitute timings were generated.");
    }

    private async Task<InteractionPerformanceEntry?> Observe(Func<Task<bool>> confirm, InteractionAction expected)
    {
        var completion = new TaskCompletionSource<InteractionPerformanceEntry?>(TaskCreationOptions.RunContinuationsAsynchronously);
        int count = 0;
        void Finished(InteractionPerformanceEntry? entry)
        {
            if (Interlocked.Increment(ref count) != 1) completion.TrySetException(new InvalidOperationException("Unexpected concurrent measurement."));
            else completion.TrySetResult(entry);
        }
        _window.MeasurementFinished += Finished;
        try
        {
            if (!await confirm()) throw new InvalidOperationException("Reference confirmation was rejected.");
            var entry = await completion.Task.WaitAsync(HostTimeout, _cancel.Token);
            if (Volatile.Read(ref count) != 1) throw new InvalidOperationException("Unexpected concurrent measurement.");
            if (entry is not null && entry.Action != expected) throw new InvalidOperationException("Unexpected DEV-75 action classification.");
            return entry;
        }
        finally { _window.MeasurementFinished -= Finished; }
    }

    private RunEnvironment ReadEnvironment()
    {
        var screen = _window.Screens.ScreenFromWindow(_window);
        return new(RuntimeInformation.OSDescription, RuntimeInformation.ProcessArchitecture.ToString(), RuntimeInformation.FrameworkDescription,
            _window.RenderScaling, _window.ClientSize.Width, _window.ClientSize.Height, screen?.Bounds.Width, screen?.Bounds.Height,
            null, options.EnvironmentId, CultureInfo.CurrentUICulture.Name, CultureInfo.CurrentCulture.Name,
            _main.Settings.LastStandardProfile.ToString());
    }
    private void CheckEnvironment()
    {
        if (ReadEnvironment() != _environment) throw new InvalidOperationException("Display, window or protocol settings changed during the run.");
    }
}
