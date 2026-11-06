using System.Diagnostics;
using System.Globalization;
using Avalonia;
using SpanDraft.Desktop;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.Presentation;

namespace SpanDraft.Screenshots;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (!OperatingSystem.IsWindows()) { Console.Error.WriteLine("Native screenshots require Windows."); return 1; }
        try
        {
            if (NativeWindows.SetThreadDpiAwarenessContext(new(-4)) == IntPtr.Zero) throw new System.ComponentModel.Win32Exception();
            if (args is ["worker", var requestPath]) return Worker(CaptureFiles.Read<WorkerRequest>(requestPath));
            if (args.Length == 0 || args[0] != "run") throw new ArgumentException("Use capture-screenshots.ps1.");
            return Run(args.Skip(1).ToArray()).GetAwaiter().GetResult();
        }
        catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }
    }

    private static async Task<int> Run(string[] args)
    {
        if (args.Length % 2 != 0) throw new ArgumentException("Expected named argument/value pairs.");
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        string[] allowed = ["--repo", "--winapp", "--language", "--scene", "--target", "--scale-percent", "--penpot-scale-percent", "--build-finished-utc"];
        for (int i = 0; i < args.Length; i += 2)
            if (!allowed.Contains(args[i]) || !options.TryAdd(args[i], args[i + 1])) throw new ArgumentException("Unknown or duplicate argument.");
        string repo = Path.GetFullPath(options["--repo"]);
        string output = Path.Combine(repo, "artifacts", "screenshots");
        CaptureFiles.AssertNoPersonalPaths(output);
        IsolatedFileStore.EnsureNoReparsePoints(output);
        Directory.CreateDirectory(output);
        string lockPath = Path.Combine(output, ".capture.lock");
        IsolatedFileStore.EnsureNoReparsePoints(lockPath);
        using var runLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var started = DateTimeOffset.UtcNow;
        string destination = CapturePublication.Destination(output, started);
        string runs = Path.Combine(output, ".runs");
        IsolatedFileStore.EnsureNoReparsePoints(runs);
        Directory.CreateDirectory(runs);
        string runRoot = Path.Combine(runs, Guid.NewGuid().ToString("N"));
        string fixture = Path.Combine(repo, "tools", "Screenshots", "Fixtures", "Crane Beam.spandraft");
        var captures = new List<CaptureEvidence>();
        var phases = new List<CapturePhase>();
        var transitions = new List<object>();
        var accessedPaths = new List<string>();
        var sceneStates = new Dictionary<string, object>();
        CaptureBatch[] batches = [];
        DisplayDevice[]? before = null, after = null;
        string? failure = null;
        bool fixtureUnchanged = false, published = false;
        using var cancellation = new CancellationTokenSource();
        void Cancel(object? sender, ConsoleCancelEventArgs e) { e.Cancel = true; cancellation.Cancel(); }
        Console.CancelKeyPress += Cancel;
        try
        {
            before = NativeWindows.Displays();
            IsolatedFileStore.EnsureNoReparsePoints(fixture);
            CaptureFiles.AssertFixture(fixture);
            batches = CaptureBatch.Select(options.GetValueOrDefault("--target", "All"),
                options.GetValueOrDefault("--language", "All"), options.GetValueOrDefault("--scene", "All"),
                int.Parse(options.GetValueOrDefault("--scale-percent", "250"), CultureInfo.InvariantCulture),
                int.Parse(options.GetValueOrDefault("--penpot-scale-percent", "200"), CultureInfo.InvariantCulture));
            var built = DateTimeOffset.Parse(options["--build-finished-utc"], CultureInfo.InvariantCulture);
            string executable = Path.GetFullPath(Environment.ProcessPath!);
            using (var current = Process.GetCurrentProcess())
                if (current.StartTime.ToUniversalTime() <= built.UtcDateTime
                    || !Path.GetFullPath(current.MainModule!.FileName).Equals(executable, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("The screenshot harness was not started from the current build.");
            // Reject an unsuitable first display before any application state is created.
            DisplaySnapshot.RequireTarget(before, batches[0].ScalePercent);
            await RunProcess(options["--winapp"], ["--version"], TimeSpan.FromSeconds(20), cancellation.Token);
            IsolatedFileStore.EnsureNoReparsePoints(runRoot);
            Directory.CreateDirectory(runRoot);
            foreach (var group in batches.GroupBy(b => b.ScalePercent))
            {
                cancellation.Token.ThrowIfCancellationRequested();
                var devices = NativeWindows.Displays();
                var previous = phases.LastOrDefault();
                DateTimeOffset? confirmed = null;
                if (previous is not null)
                {
                    if (!previous.DisplaysAfter!.SequenceEqual(devices))
                        throw new InvalidOperationException("Display configuration changed outside a confirmed phase boundary.");
                    await ConfirmScaleChange(group.Key, cancellation.Token);
                    confirmed = DateTimeOffset.UtcNow;
                    devices = NativeWindows.Displays();
                    transitions.Add(new { requestedScalePercent = group.Key, confirmedUtc = confirmed,
                        displaysBefore = previous.DisplaysAfter, displaysAfter = devices });
                    DisplaySnapshot.AssertScaleOnlyTransition(previous.DisplaysAfter!, devices);
                }
                else if (!before.SequenceEqual(devices))
                    throw new InvalidOperationException("Display configuration changed during preflight.");
                var display = DisplaySnapshot.RequireTarget(devices, group.Key);
                var phase = new CapturePhase(group.Key, DateTimeOffset.UtcNow, confirmed, display);
                phases.Add(phase);
                Console.WriteLine($"Display {display.Target}: {display.CaptureDisplay.Width} x {display.CaptureDisplay.Height} / {group.Key}% ({display.CaptureDisplay.Dpi} DPI); direct HWND capture.");
                try
                {
                    foreach (var batch in group)
                    {
                        string workerRoot = Path.Combine(runRoot, Guid.NewGuid().ToString("N"));
                        IsolatedFileStore.EnsureNoReparsePoints(workerRoot);
                        Directory.CreateDirectory(workerRoot);
                        string requestPath = Path.Combine(workerRoot, "request.json");
                        var request = new WorkerRequest(repo, workerRoot, fixture, options["--winapp"], batch.Language,
                            batch.Scenes, batch.Profile, batch.ScalePercent, built, display);
                        CaptureFiles.Write(requestPath, request);
                        await RunProcess(executable, ["worker", requestPath], TimeSpan.FromMinutes(3), cancellation.Token);
                        var result = CaptureFiles.Read<WorkerResult>(Path.Combine(workerRoot, "worker-result.json"));
                        CapturePublication.ValidateWorker(request, result, started);
                        if (result.Captures.Any(c => !c.Executable.Equals(Path.GetRelativePath(repo, executable), StringComparison.OrdinalIgnoreCase)))
                            throw new InvalidDataException("Worker executable differs from the current build.");
                        sceneStates[batch.Profile.Name + "/" + batch.Language] = new { result.StartRecents, result.SelectedBuiltInMaterial };
                        accessedPaths.AddRange(result.AccessedPaths);
                        string staged = Path.Combine(runRoot, batch.Profile.Directory);
                        Directory.CreateDirectory(staged);
                        foreach (var capture in result.Captures)
                        {
                            File.Move(Path.Combine(workerRoot, capture.File), Path.Combine(staged, capture.File));
                            captures.Add(capture);
                        }
                        DeleteRunDirectory(runRoot, workerRoot);
                    }
                }
                finally
                {
                    phase.DisplaysAfter = NativeWindows.Displays();
                    phase.FinishedUtc = DateTimeOffset.UtcNow;
                }
                if (!phase.DisplaysUnchanged)
                    throw new InvalidOperationException("The display configuration changed during an active capture phase.");
            }
            CapturePublication.ValidateImages(runRoot, batches, captures.ToArray());
            CaptureFiles.AssertFixture(fixture);
            fixtureUnchanged = true;
            after = NativeWindows.Displays();
            if (!phases[^1].DisplaysAfter!.SequenceEqual(after))
                throw new InvalidOperationException("Display configuration changed after the final capture phase.");
            cancellation.Token.ThrowIfCancellationRequested();
            CaptureFiles.Write(Path.Combine(runRoot, "result.json"), Manifest(true));
            CapturePublication.Publish(runRoot, destination, batches.SelectMany(b => b.RelativeFiles).ToArray());
            published = true;
        }
        catch (Exception error)
        {
            failure = error.Message;
            try
            {
                after = NativeWindows.Displays();
                IsolatedFileStore.EnsureNoReparsePoints(fixture);
                fixtureUnchanged = File.Exists(fixture) && CaptureFiles.Hash(fixture) == CaptureFiles.FixtureHash;
            }
            catch (Exception checkError) { failure += " Preservation check failed: " + checkError.Message; }
            try { DeleteRunDirectory(runs, runRoot); }
            catch (Exception cleanupError) { failure += " Cleanup failed: " + cleanupError.Message; }
            // A failure report is staged separately so even cleanup failure cannot expose partial PNGs.
            string diagnosticRoot = Path.Combine(runs, Guid.NewGuid().ToString("N"));
            try
            {
                IsolatedFileStore.EnsureNoReparsePoints(diagnosticRoot);
                Directory.CreateDirectory(diagnosticRoot);
                CaptureFiles.Write(Path.Combine(diagnosticRoot, "result.json"), Manifest(false));
                CapturePublication.Publish(diagnosticRoot, destination, []);
                published = true;
            }
            catch (Exception reportError)
            {
                failure += " Failure report could not be published: " + reportError.Message;
                try { DeleteRunDirectory(runs, diagnosticRoot); }
                catch (Exception cleanupError) { failure += " Diagnostic cleanup failed: " + cleanupError.Message; }
            }
        }
        finally { Console.CancelKeyPress -= Cancel; }
        if (published) Console.WriteLine("Run result: " + Path.GetRelativePath(repo, Path.Combine(destination, "result.json")));
        if (failure is not null) { Console.Error.WriteLine(Sanitize(failure, repo)); return 1; }
        Console.WriteLine("Direct HWND capture complete; all selected images published together.");
        return 0;

        object Manifest(bool success) => new
        {
            success, startedUtc = started, finishedUtc = DateTimeOffset.UtcNow, error = Sanitize(failure, repo),
            fixtureUnchanged, fixture = "tools/Screenshots/Fixtures/Crane Beam.spandraft", fixtureSha256 = CaptureFiles.FixtureHash,
            displaysBefore = before, displaysAfter = after,
            displaysUnchangedWithinPhases = phases.Count > 0 && phases.All(p => p.DisplaysUnchanged),
            phases, transitions, selection = batches, expectedFiles = batches.SelectMany(b => b.RelativeFiles).ToArray(),
            applicationStateAccesses = accessedPaths.ToArray(), sceneStates, captures = captures.ToArray()
        };
    }

    internal static async Task ConfirmScaleChange(int scalePercent, CancellationToken cancellationToken)
    {
        if (Console.IsInputRedirected)
            throw new InvalidOperationException("A native scale change requires an interactive terminal. Run from PowerShell without redirected input.");
        Console.WriteLine($"Capture phase complete. Manually set Windows display scaling to {scalePercent}%. "
            + "Keep display activation, resolution and position unchanged. Press Enter to validate and continue, or type q to cancel. "
            + "The runner changes no Windows settings.");
        string? answer = await Task.Run(Console.ReadLine, cancellationToken).WaitAsync(cancellationToken);
        if (answer is null || answer.Trim().Equals("q", StringComparison.OrdinalIgnoreCase))
            throw new OperationCanceledException("The scale transition was canceled; no images were published.");
    }

    public static void DeleteRunDirectory(string runsRoot, string runRoot)
    {
        string resolved = Path.GetFullPath(runRoot);
        if (!IsolatedFileStore.IsWithin(runsRoot, resolved) || Path.GetDirectoryName(resolved) != Path.GetFullPath(runsRoot)
            || !Guid.TryParseExact(Path.GetFileName(resolved), "N", out _))
            throw new InvalidOperationException("Refusing cleanup outside a uniquely identified screenshot run.");
        if (Directory.Exists(resolved))
        {
            IsolatedFileStore.EnsureNoReparsePoints(resolved);
            CapturePublication.CheckChildren(resolved);
            Directory.Delete(resolved, recursive: true);
        }
    }

    internal static async Task<string> RunProcess(string executable, string[] arguments, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start required process.");
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync(cancellationToken).WaitAsync(timeout, cancellationToken); }
        catch
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
            throw;
        }
        string output = await stdout, error = await stderr;
        if (process.ExitCode != 0) throw new InvalidOperationException($"Capture process failed ({process.ExitCode}): {error}\n{output}");
        return output;
    }

    private static string? Sanitize(string? message, string repo)
    {
        if (message is null) return null;
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return (string.IsNullOrEmpty(profile) ? message : message.Replace(profile, "<user-profile>", StringComparison.OrdinalIgnoreCase))
            .Replace(repo, "<repo>", StringComparison.OrdinalIgnoreCase);
    }

    private static int Worker(WorkerRequest request)
    {
        CaptureFiles.AssertFixture(request.Fixture);
        if (!request.Display.Devices.SequenceEqual(NativeWindows.Displays()))
            throw new InvalidOperationException("The display configuration changed before application startup.");
        string state = Path.Combine(request.RunRoot, "State"); Directory.CreateDirectory(state);
        string examples = Path.Combine(request.RunRoot, "Examples"); Directory.CreateDirectory(examples);
        string project = Path.Combine(examples, "Crane Beam.spandraft");
        File.Copy(request.Fixture, project);
        var files = new IsolatedFileStore(state, [project, request.Fixture]);
        var settings = UserSettings.Default with { UiLanguage = request.Language == "de-DE" ? UiLanguagePreference.German : UiLanguagePreference.English,
            RegionalFormat = RegionalFormatPreference.GermanGermany };
        var stores = DesktopStateStores.InDirectory(files, state);
        var startup = new DesktopStartupContext(files, new(files, Path.Combine(state, "settings.json")), settings)
            { StateStores = stores, UserNameProvider = static () => null };
        var recents = CaptureFiles.DemoRecents(request.BuildFinishedUtc);
        files.WriteAtomicAsync(stores.Recents.FilePath, RecentProjectsCodec.Serialize(recents))
            .GetAwaiter().GetResult();
        CultureConfigurator.Apply(CultureConfigurator.Resolve(settings, CultureInfo.GetCultureInfo("de-DE"), CultureInfo.GetCultureInfo(request.Language)));
        var capture = new CaptureApplication(request, files, project, recents);
        return AppBuilder.Configure(() => new App(startup)).UsePlatformDetect().LogToTrace()
            .StartWithClassicDesktopLifetime([], capture.Start);
    }
}
