using System.Buffers.Binary;
using SpanDraft.Screenshots;
using Xunit;

namespace SpanDraft.Tests;

public sealed class ScreenshotPublicationTests
{
    [Fact]
    public void CompleteSetIsPublishedByMovingTheWholePreparedDirectory()
    {
        WithDirectory(root =>
        {
            var batches = CaptureBatch.Select("All", "All", "All", 250, 200);
            string staging = Path.Combine(root, ".runs", Guid.NewGuid().ToString("N"));
            var captures = StageImages(staging, batches);
            string destination = CapturePublication.Destination(root, DateTimeOffset.UtcNow);
            Assert.False(Directory.Exists(destination));
            CapturePublication.ValidateImages(staging, batches, captures);
            CaptureFiles.Write(Path.Combine(staging, "result.json"), new { success = true });
            CapturePublication.Publish(staging, destination, batches.SelectMany(b => b.RelativeFiles).ToArray());
            Assert.False(Directory.Exists(staging));
            Assert.Equal(12, Directory.GetFiles(destination, "*.png", SearchOption.AllDirectories).Length);
            Assert.True(CaptureFiles.Read<System.Text.Json.JsonElement>(Path.Combine(destination, "result.json")).GetProperty("success").GetBoolean());
        });
    }

    [Theory]
    [InlineData("missing-image")]
    [InlineData("duplicate-evidence")]
    [InlineData("wrong-size")]
    [InlineData("extra-image")]
    [InlineData("unclean-state")]
    [InlineData("empty-worker-directory")]
    [InlineData("missing-manifest")]
    public void FailedSetCannotPublishPartialImagesOrOverwriteAnEarlierRun(string fault)
    {
        WithDirectory(root =>
        {
            var started = new DateTimeOffset(2026, 11, 6, 18, 22, 31, TimeSpan.Zero);
            string earlier = CapturePublication.Destination(root, started.AddMinutes(-1));
            Directory.CreateDirectory(earlier);
            string keep = Path.Combine(earlier, "previous.png");
            File.WriteAllText(keep, "previous successful run");
            var batches = CaptureBatch.Select("All", "All", "All", 250, 200);
            string runs = Path.Combine(root, ".runs"), staging = Path.Combine(runs, Guid.NewGuid().ToString("N"));
            var captures = StageImages(staging, batches);
            string first = Path.Combine(staging, batches[0].RelativeFiles[0]);
            string destination = CapturePublication.Destination(root, started);
            switch (fault)
            {
                case "missing-image": File.Delete(first); break;
                case "duplicate-evidence": captures[^1] = captures[0]; break;
                case "wrong-size": WritePngHeader(first, new(2400, 1800)); break;
                case "extra-image": WritePngHeader(Path.Combine(staging, "stale.png"), new(3840, 2160)); break;
                case "unclean-state": Directory.CreateDirectory(Path.Combine(staging, "State")); File.WriteAllText(Path.Combine(staging, "State", "settings.json"), "{}"); break;
                case "empty-worker-directory": Directory.CreateDirectory(Path.Combine(staging, Guid.NewGuid().ToString("N"))); break;
            }
            if (fault != "missing-manifest") CaptureFiles.Write(Path.Combine(staging, "result.json"), new { success = true });
            var error = Record.Exception(() =>
            {
                CapturePublication.ValidateImages(staging, batches, captures);
                CapturePublication.Publish(staging, destination, batches.SelectMany(b => b.RelativeFiles).ToArray());
            });
            Assert.True(error is InvalidDataException or IOException, error?.ToString());
            Assert.False(Directory.Exists(destination));
            Assert.Equal("previous successful run", File.ReadAllText(keep));
            SpanDraft.Screenshots.Program.DeleteRunDirectory(runs, staging);
            string diagnostic = Path.Combine(runs, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(diagnostic);
            CaptureFiles.Write(Path.Combine(diagnostic, "result.json"), new { success = false, error = fault });
            CapturePublication.Publish(diagnostic, destination, []);
            Assert.Equal(new[] { "result.json" }, Directory.GetFiles(destination).Select(Path.GetFileName));
            Assert.Empty(Directory.GetFiles(destination, "*.png", SearchOption.AllDirectories));
            Assert.False(CaptureFiles.Read<System.Text.Json.JsonElement>(Path.Combine(destination, "result.json")).GetProperty("success").GetBoolean());
            Assert.Equal("previous successful run", File.ReadAllText(keep));
        });
    }

    [Fact]
    public void RunNamesUseUtcSortChronologicallyAndNeverReuseAnExistingDirectoryOrFile()
    {
        WithDirectory(root =>
        {
            var local = new DateTimeOffset(2026, 11, 6, 19, 22, 31, TimeSpan.FromHours(1));
            string first = CapturePublication.Destination(root, local);
            Assert.Equal("2026-11-06_18-22-31.0000000Z", Path.GetFileName(first));
            Assert.Equal(first, CapturePublication.Destination(root, local.ToUniversalTime()));
            Directory.CreateDirectory(first);
            string collision = CapturePublication.Destination(root, local);
            Assert.EndsWith("Z-01", collision);
            File.WriteAllText(collision, "occupied");
            Assert.EndsWith("Z-02", CapturePublication.Destination(root, local));
            Assert.True(StringComparer.Ordinal.Compare(first, CapturePublication.Destination(root, local.AddSeconds(1))) < 0);
            var staging = Path.Combine(root, "staging"); Directory.CreateDirectory(staging);
            CaptureFiles.Write(Path.Combine(staging, "result.json"), new { success = false });
            Assert.ThrowsAny<IOException>(() => CapturePublication.Publish(staging, first, []));
            Assert.True(File.Exists(Path.Combine(staging, "result.json")));
        });
    }

    [Theory]
    [InlineData("profile")]
    [InlineData("language")]
    [InlineData("dpi")]
    [InlineData("render-scaling")]
    [InlineData("bounds")]
    [InlineData("stale-process")]
    [InlineData("stale-file")]
    [InlineData("fixture")]
    [InlineData("recents")]
    public void WorkerValidationRejectsInvalidNativeEvidenceAndStaleOutputs(string fault)
    {
        WithDirectory(root =>
        {
            var batch = Assert.Single(CaptureBatch.Select("Store", "de-DE", "Project", 240, 200));
            var built = DateTimeOffset.UtcNow.AddMinutes(-2);
            var runStarted = built.AddSeconds(1);
            var display = new DisplayDevice("primary", "display", "PCI", 5, 2940, 1846, 0, 0, 230);
            var request = new WorkerRequest(root, root, "fixture", "winapp", batch.Language, batch.Scenes,
                batch.Profile, batch.ScalePercent, built, new([display], display.Name));
            var captures = StageImages(root, [batch]);
            File.Move(Path.Combine(root, batch.RelativeFiles[0]), Path.Combine(root, captures[0].File));
            var result = new WorkerResult(captures, CaptureFiles.FixtureHash, [], CaptureFiles.DemoRecents(built), null);
            CapturePublication.ValidateWorker(request, result, runStarted);
            captures[0] = fault switch
            {
                "profile" => captures[0] with { Profile = "PenpotHero" },
                "language" => captures[0] with { Language = "en-US" },
                "dpi" => captures[0] with { WindowDpi = 240 },
                "render-scaling" => captures[0] with { RenderScaling = 2 },
                "bounds" => captures[0] with { VisibleBounds = new(0, 0, 3800, 2160) },
                "stale-process" => captures[0] with { ProcessStartedUtc = built },
                _ => captures[0]
            };
            if (fault == "stale-file") File.SetLastWriteTimeUtc(Path.Combine(root, captures[0].File), built.UtcDateTime);
            if (fault == "fixture") result = result with { FixtureHash = "modified" };
            if (fault == "recents") result = result with { StartRecents = [] };
            Assert.Throws<InvalidDataException>(() => CapturePublication.ValidateWorker(request, result, runStarted));
        });
    }

    [Fact]
    public void PersonalAndInternalVisiblePathsAreRejectedForEveryProfile()
    {
        foreach (string text in new[] { @"C:\Users\Someone\secret", @"C:\dev\SpanDraft\tools", @"C:\neutral\artifacts\run", @"C:\neutral\.runs\run" })
            Assert.Throws<InvalidOperationException>(() => CaptureFiles.AssertVisibleText(text, @"C:\dev\SpanDraft"));
        CaptureFiles.AssertVisibleText(@"C:\SpanDraft\Examples\Crane Beam.spandraft", @"C:\dev\SpanDraft");
    }

    private static CaptureEvidence[] StageImages(string root, CaptureBatch[] batches) => batches.SelectMany(batch => batch.Scenes.Select(scene =>
    {
        string file = CaptureFiles.FileName(batch.Profile, scene, batch.Language);
        string path = Path.Combine(root, batch.Profile.Directory, file);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        WritePngHeader(path, batch.Profile.Size);
        var (width, height) = batch.Profile.Size.Pixels;
        uint dpi = CaptureScaling.ExpectedDpi(batch.ScalePercent);
        return new CaptureEvidence(batch.Profile.Name, batch.Language, scene, file, width, height, dpi, dpi / 96d,
            new(0, 0, width, height), new(0, 0, width, height), new(0, 0, width, height - 80),
            new(true, true, true, true, true, new(width - 300, 0, width, 80)), 1, 123, "harness.exe",
            DateTimeOffset.UtcNow.AddSeconds(-1), DateTimeOffset.UtcNow);
    })).ToArray();

    private static void WritePngHeader(string path, CaptureSize size)
    {
        // These unit fixtures exercise PNG dimension validation without a native renderer.
        byte[] header = new byte[24];
        new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(header, 0);
        "IHDR"u8.CopyTo(header.AsSpan(12));
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(16), size.Width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(20), size.Height);
        File.WriteAllBytes(path, header);
    }

    private static void WithDirectory(Action<string> action)
    {
        string root = Path.Combine(AppContext.BaseDirectory, "screenshot-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try { action(root); }
        finally { Directory.Delete(root, recursive: true); }
    }
}
