using System.Security;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Threading;
using SpanDraft.Core.Sections.Parametric;
using SpanDraft.Desktop;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.ViewModels;
using SpanDraft.StoreScreenshots;
using Xunit;
using static SpanDraft.Tests.ProjectTestSupport;

namespace SpanDraft.Tests;

public sealed class StoreScreenshotTests
{
    private static string Fixture => Path.Combine(AppContext.BaseDirectory, "Fixtures", "Store", "Crane Beam.spandraft");

    [Theory]
    [InlineData("Start", "de-DE", "01-start-de-DE.png")]
    [InlineData("Project", "de-DE", "02-project-de-DE.png")]
    [InlineData("Section", "de-DE", "03-section-de-DE.png")]
    [InlineData("Report", "de-DE", "04-report-de-DE.png")]
    [InlineData("Materials", "de-DE", "05-materials-de-DE.png")]
    [InlineData("Start", "en-US", "01-start-en-US.png")]
    [InlineData("Project", "en-US", "02-project-en-US.png")]
    [InlineData("Section", "en-US", "03-section-en-US.png")]
    [InlineData("Report", "en-US", "04-report-en-US.png")]
    [InlineData("Materials", "en-US", "05-materials-en-US.png")]
    public void CaptureNamesIdentifyMotifAndCultureInOneFlatDirectory(string scene, string culture, string expected)
    {
        string name = CaptureFiles.FileName(scene, culture);
        Assert.Equal(expected, name);
        Assert.Equal(name, Path.GetFileName(name));
    }

    [Theory]
    [InlineData("Unknown", "de-DE")]
    [InlineData("Start", "../en-US")]
    public void CaptureNamesRejectUnknownMotifsAndCultures(string scene, string culture) =>
        Assert.Throws<ArgumentException>(() => CaptureFiles.FileName(scene, culture));

    [Fact]
    public async Task DemoRecentsRoundTripAsFiveNeutralPathsWithoutReadingTheExampleFiles()
    {
        var reference = new DateTimeOffset(2026, 10, 26, 18, 0, 0, TimeSpan.FromHours(1));
        var recents = CaptureFiles.DemoRecents(reference);
        Assert.Equal(5, recents.Length);
        Assert.Equal(5, recents.Select(e => Path.GetFileName(e.Path)).Distinct().Count());
        Assert.All(recents, entry =>
        {
            Assert.StartsWith(@"C:\SpanDraft\Examples\", entry.Path);
            Assert.True(Path.IsPathFullyQualified(entry.Path));
            Assert.Equal(TimeSpan.Zero, entry.LastUsedUtc.Offset);
            Assert.True(entry.LastUsedUtc < reference);
        });
        Assert.Equal(new[] { new TimeSpan(0, 1, 20, 0), new(0, 6, 45, 0), new(1, 3, 10, 0), new(4, 8, 35, 0), new(11, 2, 50, 0) },
            recents.Select(e => reference - e.LastUsedUtc).ToArray());
        Assert.True(recents.Select(e => e.LastUsedUtc.UtcDateTime.Date).Distinct().Count() >= 3);
        var files = new Files(); var reads = new List<string>();
        files.Data["isolated-recents.json"] = RecentProjectsCodec.Serialize(recents);
        files.BeforeRead = path => { reads.Add(path); return Task.CompletedTask; };
        var store = new RecentProjectsStore(files, "isolated-recents.json");
        await store.LoadAsync();
        Assert.Equal(recents, store.Entries);
        Assert.Equal("isolated-recents.json", Assert.Single(reads));
        Assert.Empty(files.Writes);
        CaptureFiles.AssertDemoRecents(store.Entries.ToArray(), reference);
        var invalid = recents.ToArray(); invalid[0] = invalid[0] with { Path = TestPath("artifacts/internal.spandraft") };
        Assert.Throws<InvalidDataException>(() => CaptureFiles.AssertDemoRecents(invalid, reference));
    }

    [Fact]
    public void CentralSizeFitsRequiredDisplayAndOriginalFixtureIsUnchanged()
    {
        var size = CaptureFiles.Read<CaptureSize>(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Store", "capture-size.json"));
        Assert.Equal(new CaptureSize(1600, 900), size);
        Assert.Equal((3200, 1800), size.Pixels);
        CaptureFiles.AssertFixture(Fixture);
        var section = Assert.IsType<ISectionGeometry>(ProjectFileCodec.Deserialize(File.ReadAllBytes(Fixture)).Document.Section);
        Assert.Equal(.1, section.Height.Meters); Assert.Equal(.08, section.Width.Meters);
        Assert.Equal(.012, section.FlangeThickness.Meters); Assert.Equal(.01, section.WebThickness.Meters); Assert.Equal(.006, section.Radius.Meters);
    }

    [Theory]
    [InlineData(0)] [InlineData(-1)] [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)]
    [InlineData(.1)] [InlineData(double.MaxValue)]
    public void InvalidDimensionsCannotBecomePixelSizes(double invalid)
    {
        Assert.Throws<ArgumentException>(() => new CaptureSize(invalid, 1000).Pixels);
        Assert.Throws<ArgumentException>(() => new CaptureSize(1830, invalid).Pixels);
    }

    [Theory]
    [InlineData(11, 0, 11, 11)] [InlineData(8, 2, 8, 8)] [InlineData(0, 0, 0, 0)]
    public void OuterSizingAccountsForMeasuredInvisibleBorders(int left, int top, int right, int bottom)
    {
        var outer = new NativeRect(200, 70, 1200, 870);
        var visible = new NativeRect(200 + left, 70 + top, 1200 - right, 870 - bottom);
        Assert.Equal((3660 + left + right, 2000 + top + bottom), WindowSizing.OuterSize(new(1830, 1000), outer, visible));
        if (outer != visible) Assert.Throws<ArgumentException>(() => WindowSizing.OuterSize(new(1830, 1000), visible, outer));
    }

    [Theory]
    [InlineData(2940, 1846)] [InlineData(1920, 1080)] [InlineData(4480, 2520)]
    public void CaptureDisplaySelectionDoesNotRequireTheWindowToFitTheDesktop(int width, int height)
    {
        var primary = new DisplayDevice("primary", "display", "PCI", 5, width, height, 0, 0, 200);
        Assert.Equal(primary, DisplaySnapshot.RequireTarget([primary]).CaptureDisplay);
        Assert.Equal((3200, 1800), new CaptureSize(1600, 900).Pixels);
    }

    [Theory]
    [InlineData(4, 200)] [InlineData(5, 175)] [InlineData(5, 0)]
    public void CaptureDisplayRejectsInactiveWrongAndUnknownScaling(uint state, int scale)
    {
        var primary = new DisplayDevice("primary", "display", "PCI", state, 2940, 1846, 0, 0, scale);
        var error = Assert.Throws<InvalidOperationException>(() => DisplaySnapshot.RequireTarget([primary]));
        Assert.Contains("200% (192 DPI)", error.Message);
        Assert.Contains($"2940 x 1846 px, {scale}%", error.Message);
        Assert.Contains("No display was changed", error.Message);
    }

    [Fact]
    public void CaptureDisplayPrefersPrimaryThenUsesAnAlreadyScaledExternalDisplay()
    {
        var primary = new DisplayDevice("primary", "display", "PCI", 5, 2940, 1846, 0, 0, 200);
        var external = primary with { Name = "external", State = 1, X = -1920, Width = 1920, Height = 1080 };
        Assert.Equal(primary, DisplaySnapshot.RequireTarget([external, primary]).CaptureDisplay);
        Assert.Equal(external, DisplaySnapshot.RequireTarget([primary with { ScalePercent = 175 }, external]).CaptureDisplay);
        Assert.Equal(external, DisplaySnapshot.RequireTarget([primary, external], external.Name).CaptureDisplay);
        Assert.Throws<InvalidOperationException>(() => DisplaySnapshot.RequireTarget([primary], "missing"));
        Assert.Throws<InvalidOperationException>(() => DisplaySnapshot.RequireTarget([primary, external with { State = 5 }]));
    }

    [Theory]
    [InlineData(2940, 1846, 3222, 1846)]
    [InlineData(4096, 2160, 4096, 2160)]
    [InlineData(1920, 1080, 3222, 1811)]
    public void WindowTrackSizeAllowsCompleteOffscreenWindowAndPreservesOtherNativeLimits(
        int originalWidth, int originalHeight, int expectedWidth, int expectedHeight)
    {
        var memory = Marshal.AllocHGlobal(40); // MINMAXINFO: five native POINT structures.
        try
        {
            var original = Enumerable.Range(0, 40).Select(i => (byte)i).ToArray();
            Marshal.Copy(original, 0, memory, original.Length);
            Marshal.WriteInt32(memory, 32, originalWidth);
            Marshal.WriteInt32(memory, 36, originalHeight);
            var outer = new NativeRect(0, 0, 1022, 811);
            var visible = new NativeRect(11, 0, 1011, 800);
            var size = WindowSizing.OuterSize(new(1600, 900), outer, visible);
            WindowSizeOverride.ApplyTrackSize(memory, size.Width, size.Height);
            var actual = new byte[40]; Marshal.Copy(memory, actual, 0, actual.Length);
            Assert.Equal(original[..32], actual[..32]); // Reserved/maximize/minimum values are untouched.
            Assert.Equal(expectedWidth, Marshal.ReadInt32(memory, 32));
            Assert.Equal(expectedHeight, Marshal.ReadInt32(memory, 36));
        }
        finally { Marshal.FreeHGlobal(memory); }
    }

    [Fact]
    public async Task FileBoundaryAllowsOnlyStateAndExplicitReadOnlyProjects()
    {
        string root = TestPath("isolated"), fixture = TestPath("Fixture.spandraft");
        var disk = new Files(); var store = new IsolatedFileStore(root, [fixture], disk);
        string settings = Path.Combine(root, "settings.json");
        await store.WriteAtomicAsync(settings, [1], TestContext.Current.CancellationToken);
        Assert.Equal(new byte[] { 1 }, await store.ReadAsync(settings));
        await store.ReadAsync(fixture);
        await store.DeleteAsync(settings);
        await Assert.ThrowsAsync<SecurityException>(() => store.ReadAsync(TestPath("isolated-other/settings.json")));
        await Assert.ThrowsAsync<SecurityException>(() => store.ReadAsync(Path.Combine(root, "..", "private.json")));
        await Assert.ThrowsAsync<SecurityException>(() => store.WriteAtomicAsync(fixture, [], TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<SecurityException>(() => store.DeleteAsync(fixture));
        await Assert.ThrowsAsync<SecurityException>(() => store.WriteAtomicAsync(Path.Combine(root, "new.spandraft"), [], TestContext.Current.CancellationToken));
        Assert.Equal(4, store.AccessedPaths.Length);
    }

    [Theory]
    [InlineData("C:\\Users\\Somebody\\Desktop\\Private.spandraft")]
    [InlineData("C:\\\\Users\\\\Somebody\\\\Private.spandraft")]
    [InlineData("/home/somebody/private.spandraft")]
    public void PersonalPathsCannotEnterVisibleInputs(string path)
    {
        Assert.Throws<InvalidOperationException>(() => CaptureFiles.AssertNoPersonalPaths(path));
        CaptureFiles.AssertNoPersonalPaths("C:\\dev\\SpanDraft\\Examples\\Crane Beam.spandraft");
    }

    [Fact]
    public void CleanupRefusesParentsSiblingsAndUnnamedDirectories()
    {
        string root = TestPath("runs");
        Assert.Throws<InvalidOperationException>(() => SpanDraft.StoreScreenshots.Program.DeleteRunDirectory(root, root));
        Assert.Throws<InvalidOperationException>(() => SpanDraft.StoreScreenshots.Program.DeleteRunDirectory(root, TestPath("elsewhere")));
        Assert.Throws<InvalidOperationException>(() => SpanDraft.StoreScreenshots.Program.DeleteRunDirectory(root, Path.Combine(root, "not-a-run")));
    }

    [Fact]
    public void CleanupRemovesOnlyTheOwnedRunIncludingPartialFailureOutputs()
    {
        string runs = Path.Combine(AppContext.BaseDirectory, "cleanup-test-" + Guid.NewGuid().ToString("N"));
        string owned = Path.Combine(runs, Guid.NewGuid().ToString("N"));
        string other = Path.Combine(runs, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(owned, "State")); Directory.CreateDirectory(other);
        try
        {
            File.WriteAllText(Path.Combine(owned, "State", "recovery.json"), "partial state");
            File.WriteAllText(Path.Combine(owned, CaptureFiles.FileName("Start", "de-DE")), "partial capture");
            string keep = Path.Combine(other, "keep.txt"); File.WriteAllText(keep, "other run");
            SpanDraft.StoreScreenshots.Program.DeleteRunDirectory(runs, owned);
            Assert.False(Directory.Exists(owned)); Assert.Equal("other run", File.ReadAllText(keep));
            SpanDraft.StoreScreenshots.Program.DeleteRunDirectory(runs, other);
        }
        finally { if (Directory.Exists(runs)) Directory.Delete(runs); }
    }
}

[Collection("Schematic text")]
public sealed class DesktopIsolatedStartupTests
{
    [Fact]
    public void InitializationWaitsForRecentsAndShellOpenBeforeSignalingReadiness()
    {
        using var environment = new DesktopControlEnvironment();
        var files = new Files();
        string root = Path.Combine(AppContext.BaseDirectory, "startup-gate-" + Guid.NewGuid().ToString("N"));
        string fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Store", "Crane Beam.spandraft");
        var stores = DesktopStateStores.InDirectory(files, root);
        var gate = new TaskCompletionSource();
        files.BeforeRead = path => path == stores.Recents.FilePath ? gate.Task : Task.CompletedTask;
        files.Data[fixture] = File.ReadAllBytes(fixture);
        var window = new MainWindow(new(files, new(files, Path.Combine(root, "settings.json")), UserSettings.Default)
            { StateStores = stores, UserNameProvider = static () => null }, [fixture]);
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            Assert.False(window.Initialization.IsCompleted);
            var model = Assert.IsType<MainWindowViewModel>(window.DataContext); Assert.Null(model.Session);
            Assert.Null(model.Setup.AuthorInput.ConfirmedValue);
            gate.SetResult(); Dispatcher.UIThread.RunJobs();
            Assert.True(window.Initialization.IsCompletedSuccessfully);
            Assert.Equal(fixture, model.Session!.FilePath);
            Assert.Equal(fixture, Assert.Single(model.StartPage.RecentProjects).FilePath);
        }
        finally
        {
            gate.TrySetResult(); window.Close(); Dispatcher.UIThread.RunJobs();
            if (Directory.Exists(root)) Directory.Delete(root);
        }
    }

    [Fact]
    public async Task RealWindowInitializesAndNavigatesUsingOnlyInjectedStoresAndFixture()
    {
        using var environment = new DesktopControlEnvironment();
        var files = new Files();
        string root = Path.Combine(AppContext.BaseDirectory, "isolated-window-" + Guid.NewGuid().ToString("N"));
        string fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Store", "Crane Beam.spandraft");
        var reads = new List<string>(); files.BeforeRead = path => { reads.Add(path); return Task.CompletedTask; };
        files.Data[fixture] = File.ReadAllBytes(fixture);
        var stores = DesktopStateStores.InDirectory(files, root);
        var settings = new LocalSettingsStore(files, Path.Combine(root, "settings.json"));
        var startup = new DesktopStartupContext(files, settings, UserSettings.Default)
            { StateStores = stores, UserNameProvider = static () => "Neutral author" };
        var window = new MainWindow(startup);
        Assert.False(window.Initialization.IsCompleted);
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            Assert.True(window.Initialization.IsCompletedSuccessfully);
            Assert.Equal(new[] { stores.Materials.FilePath, stores.Sections.FilePath, stores.Recovery.SlotPath,
                stores.SetupRecovery.SlotPath, stores.Recents.FilePath }, reads);
            var main = Assert.IsType<MainWindowViewModel>(window.DataContext);
            Assert.Equal("Neutral author", main.Setup.AuthorInput.ConfirmedValue);
            Assert.True(await main.OpenAsync(fixture));
            main.EditProject(); main.Setup.ShowSections(); Dispatcher.UIThread.RunJobs();
            Assert.Equal(MainWorkspaceKind.SectionDefinition, main.Workspace);
            Assert.NotNull(main.Setup.Definition);
            main.Setup.Definition!.CancelCommand.Execute(null);
            Assert.True(await main.CancelSetupAsync());
            Assert.True(await main.ReportAsync()); Dispatcher.UIThread.RunJobs();
            Assert.Equal(MainWorkspaceKind.ReportPreview, main.Workspace);
            Assert.False(main.Session!.IsDirty);
            Assert.True(main.CloseReport());
            main.EditProject(); main.Setup.ShowMaterials();
            var materials = Assert.IsType<MaterialSelectionViewModel>(main.Setup.CurrentStep);
            materials.SelectedFilter = materials.BuiltInFilter;
            materials.SelectedEntry = materials.BuiltInEntries.Single(e => e.Name == "S355JR" && e.MaterialNumber == "1.0045");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(MainWorkspaceKind.MaterialSelection, main.Workspace);
            Assert.True(materials.IsBuiltInFilter); Assert.True(materials.SelectedEntry.IsBuiltIn);
            Assert.True(materials.FilteredEntries.Count > 1); Assert.Empty(materials.SearchText);
            materials.BackCommand.Execute(null);
            Assert.True(await main.CancelSetupAsync());
            Assert.False(main.Session.IsDirty);
            Assert.True(ProjectFileCodec.Deserialize(files.Data[fixture]).ContentEquals(main.Session.CurrentRevision.State));
            Assert.All(reads.Where(p => p != fixture), p => Assert.True(IsolatedFileStore.IsWithin(root, p)));
            Assert.Equal(File.ReadAllBytes(fixture), files.Data[fixture]);
        }
        finally
        {
            window.Close(); Dispatcher.UIThread.RunJobs();
            if (Directory.Exists(root)) Directory.Delete(root);
        }
    }
}
