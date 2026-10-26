using System.Diagnostics;
using System.Globalization;
using Avalonia;
using SpanDraft.Desktop;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.Presentation;

namespace SpanDraft.StoreScreenshots;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (!OperatingSystem.IsWindows()) { Console.Error.WriteLine("Native Store screenshots require Windows."); return 1; }
        NativeWindows.SetThreadDpiAwarenessContext(new(-4));
        try
        {
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
        string[] allowed = ["--repo", "--winapp", "--language", "--scene", "--build-finished-utc", "--width-dip", "--height-dip"];
        for (int i = 0; i < args.Length; i += 2)
            if (!allowed.Contains(args[i]) || !options.TryAdd(args[i], args[i + 1])) throw new ArgumentException("Unknown or duplicate argument.");
        string repo = Path.GetFullPath(options["--repo"]);
        string output = Path.Combine(repo, "artifacts", "store-screenshots");
        CaptureFiles.AssertNoPersonalPaths(output);
        IsolatedFileStore.EnsureNoReparsePoints(output);
        Directory.CreateDirectory(output);
        // Keep two invocations from publishing over each other or mixing language sets.
        using var runLock = new FileStream(Path.Combine(output, ".capture.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var started = DateTimeOffset.UtcNow;
        DisplayDevice[]? before = null;
        string? runRoot = null;
        DisplaySnapshot? display = null;
        string fixture = Path.Combine(repo, "tools", "StoreScreenshots", "Fixtures", "Crane Beam.spandraft");
        var captures = new List<CaptureEvidence>();
        var accessedPaths = new List<string>();
        var sceneStates = new Dictionary<string, object>();
        string? failure = null;
        bool displaysUnchanged = false, fixtureUnchanged = false;
        DisplayDevice[]? after = null;
        using var cancellation = new CancellationTokenSource();
        void Cancel(object? sender, ConsoleCancelEventArgs e) { e.Cancel = true; cancellation.Cancel(); }
        Console.CancelKeyPress += Cancel;
        try
        {
            before = NativeWindows.Displays();
            IsolatedFileStore.EnsureNoReparsePoints(fixture);
            CaptureFiles.AssertFixture(fixture);
            var size = CaptureFiles.Read<CaptureSize>(Path.Combine(repo, "tools", "StoreScreenshots", "capture-size.json"));
            if (options.TryGetValue("--width-dip", out var width)) size = size with { WidthDip = double.Parse(width, CultureInfo.InvariantCulture) };
            if (options.TryGetValue("--height-dip", out var height)) size = size with { HeightDip = double.Parse(height, CultureInfo.InvariantCulture) };
            var pixels = size.Pixels;
            display = DisplaySnapshot.RequireTarget(before);
            string[] languages = CaptureFiles.Select(options.GetValueOrDefault("--language", "All"), CaptureFiles.Languages);
            string[] scenes = CaptureFiles.Select(options.GetValueOrDefault("--scene", "All"), CaptureFiles.Scenes);
            var built = DateTimeOffset.Parse(options["--build-finished-utc"], CultureInfo.InvariantCulture);
            var executable = Path.GetFullPath(Environment.ProcessPath!);
            using (var current = Process.GetCurrentProcess())
                if (current.StartTime.ToUniversalTime() <= built.UtcDateTime)
                    throw new InvalidOperationException("The screenshot harness was not started after the current build.");
            // Availability check before any application state is created.
            await RunProcess(options["--winapp"], ["--version"], TimeSpan.FromSeconds(20), cancellation.Token);
            Console.WriteLine($"Display {display.Target}: {display.CaptureDisplay.Width} x {display.CaptureDisplay.Height} / 200%; direct HWND capture, no display changes.");
            cancellation.Token.ThrowIfCancellationRequested();
            string runs = Path.Combine(output, ".runs"); Directory.CreateDirectory(runs);
            runRoot = Path.Combine(runs, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(runRoot);
            foreach (string language in languages)
            {
                string languageRoot = Path.Combine(runRoot, language); Directory.CreateDirectory(languageRoot);
                string requestPath = Path.Combine(languageRoot, "request.json");
                CaptureFiles.Write(requestPath, new WorkerRequest(repo, languageRoot, fixture, options["--winapp"], language, scenes, size, built, display));
                await RunProcess(executable, ["worker", requestPath], TimeSpan.FromMinutes(3), cancellation.Token);
                var result = CaptureFiles.Read<WorkerResult>(Path.Combine(languageRoot, "worker-result.json"));
                CaptureFiles.AssertDemoRecents(result.StartRecents, built);
                if (scenes.Contains("Materials") && result.SelectedBuiltInMaterial != "S355JR (1.0045)")
                    throw new InvalidDataException("The materials motif did not capture the selected built-in S355JR entry.");
                sceneStates[language] = new { result.StartRecents, result.SelectedBuiltInMaterial };
                foreach (string path in result.AccessedPaths) CaptureFiles.AssertNoPersonalPaths(path);
                accessedPaths.AddRange(result.AccessedPaths);
                if (result.FixtureHash != CaptureFiles.FixtureHash || result.Captures.Length != scenes.Length)
                    throw new InvalidDataException("Worker did not produce the expected captures from the unchanged fixture.");
                foreach (var capture in result.Captures)
                {
                    if (!scenes.Contains(capture.Scene) || capture.File != CaptureFiles.FileName(capture.Scene, language))
                        throw new InvalidDataException("Unexpected screenshot filename or motif.");
                    string path = Path.Combine(languageRoot, capture.File);
                    if (CaptureFiles.PngSize(path) != pixels || capture.Width != pixels.Width || capture.Height != pixels.Height
                        || capture.WindowDpi != 192 || capture.RenderScaling != 2
                        || capture.VisibleBounds.Width != pixels.Width || capture.VisibleBounds.Height != pixels.Height
                        || capture.ProcessStartedUtc <= built || capture.Language != language
                        || File.GetLastWriteTimeUtc(path) < started.UtcDateTime)
                        throw new InvalidDataException("Native rasterization, dimensions or capture provenance validation failed.");
                    captures.Add(capture);
                }
                if (result.Captures.Select(c => c.Scene).Distinct().Count() != scenes.Length)
                    throw new InvalidDataException("Duplicate or missing scene.");
            }
            CaptureFiles.AssertFixture(fixture);
            after = NativeWindows.Displays();
            displaysUnchanged = before.SequenceEqual(after);
            fixtureUnchanged = true;
            if (!displaysUnchanged) throw new InvalidOperationException("The display configuration changed during the capture run.");
            // Publish only a completely verified selection; failed runs never pass stale PNGs as new captures.
            foreach (var capture in captures)
            {
                string languageRoot = Path.Combine(runRoot, capture.Language);
                File.Move(Path.Combine(languageRoot, capture.File), Path.Combine(output, capture.File), overwrite: true);
                Console.WriteLine($"{capture.File}: {capture.Width} x {capture.Height} px, native 192 DPI");
            }
        }
        catch (Exception error) { failure = error.Message; }
        finally
        {
            try
            {
                after = NativeWindows.Displays();
                if (before is not null) displaysUnchanged = before.SequenceEqual(after);
                fixtureUnchanged = File.Exists(fixture) && CaptureFiles.Hash(fixture) == CaptureFiles.FixtureHash;
                if (!displaysUnchanged || !fixtureUnchanged) failure ??= "Display or fixture preservation check failed.";
            }
            catch (Exception error) { failure ??= error.Message; }
            try { if (runRoot is not null) DeleteRunDirectory(Path.Combine(output, ".runs"), runRoot); }
            catch (Exception error) { failure ??= "Cleanup failed: " + error.Message; }
            // Diagnostic manifests intentionally use repo-relative paths, never the WinApp alias/profile path.
            CaptureFiles.Write(Path.Combine(output, "result.json"), new
            {
                success = failure is null, startedUtc = started, finishedUtc = DateTimeOffset.UtcNow,
                error = Sanitize(failure, repo), displaysUnchanged, fixtureUnchanged, captureDisplay = display?.Target,
                fixture = "tools/StoreScreenshots/Fixtures/Crane Beam.spandraft", fixtureSha256 = CaptureFiles.FixtureHash,
                displaysBefore = before, displaysAfter = after,
                applicationStateAccesses = accessedPaths.ToArray(),
                sceneStates,
                captures = captures.ToArray()
            });
            Console.CancelKeyPress -= Cancel;
        }
        if (failure is not null) { Console.Error.WriteLine(Sanitize(failure, repo)); return 1; }
        Console.WriteLine("Direct HWND capture complete; display configuration unchanged.");
        return 0;
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
            CheckChildren(resolved);
            Directory.Delete(resolved, recursive: true);
        }

        static void CheckChildren(string directory)
        {
            foreach (string path in Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("Refusing recursive cleanup through a reparse point.");
                if ((attributes & FileAttributes.Directory) != 0) CheckChildren(path);
            }
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
