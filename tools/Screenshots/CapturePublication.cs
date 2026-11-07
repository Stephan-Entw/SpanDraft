using System.Globalization;

namespace SpanDraft.Screenshots;

public static class CapturePublication
{
    public static string Destination(string output, DateTimeOffset started)
    {
        string name = started.UtcDateTime.ToString("yyyy-MM-dd_HH-mm-ss.fffffff'Z'", CultureInfo.InvariantCulture);
        string path = Path.Combine(output, name);
        for (int suffix = 1; Directory.Exists(path) || File.Exists(path); suffix++)
            path = Path.Combine(output, name + "-" + suffix.ToString("D2", CultureInfo.InvariantCulture));
        return path;
    }

    public static void ValidateWorker(WorkerRequest request, WorkerResult result, DateTimeOffset runStarted)
    {
        CaptureFiles.AssertDemoRecents(result.StartRecents, request.BuildFinishedUtc);
        if (request.Scenes.Contains("Materials") && result.SelectedBuiltInMaterial != "S355JR (1.0045)")
            throw new InvalidDataException("The materials motif did not capture the selected built-in S355JR entry.");
        foreach (string path in result.AccessedPaths) CaptureFiles.AssertNoPersonalPaths(path);
        if (result.FixtureHash != CaptureFiles.FixtureHash || result.Captures.Length != request.Scenes.Length
            || !result.Captures.Select(c => c.Scene).Order().SequenceEqual(request.Scenes.Order()))
            throw new InvalidDataException("Worker did not produce exactly the expected captures from the unchanged fixture.");
        var pixels = request.Profile.Size.Pixels;
        foreach (var capture in result.Captures)
        {
            if (capture.Profile != request.Profile.Name || capture.Language != request.Language
                || capture.File != CaptureFiles.FileName(request.Profile, capture.Scene, request.Language))
                throw new InvalidDataException("Unexpected screenshot profile, language, filename or motif.");
            string path = Path.Combine(request.RunRoot, capture.File);
            IsolatedFileStore.EnsureNoReparsePoints(path);
            if (CaptureFiles.PngSize(path) != pixels || (capture.Width, capture.Height) != pixels
                || !CaptureScaling.Matches(capture.WindowDpi, capture.RenderScaling, request.ScalePercent)
                || capture.WindowDpi != request.Display.CaptureDisplay.Dpi
                || (capture.VisibleBounds.Width, capture.VisibleBounds.Height) != pixels
                || !capture.WindowBounds.Contains(capture.VisibleBounds)
                || capture.ProcessStartedUtc <= request.BuildFinishedUtc || capture.ProcessStartedUtc < runStarted
                || capture.CapturedUtc < capture.ProcessStartedUtc || capture.CapturedUtc > DateTimeOffset.UtcNow
                || File.GetLastWriteTimeUtc(path) < runStarted.UtcDateTime)
                throw new InvalidDataException("Native rasterization, dimensions or capture provenance validation failed.");
            CaptureFiles.AssertNoPersonalPaths(capture.Executable);
        }
    }

    public static void ValidateImages(string staging, CaptureBatch[] batches, CaptureEvidence[] captures)
    {
        if (captures.Length != batches.Sum(b => b.Scenes.Length))
            throw new InvalidDataException("Incomplete capture evidence.");
        foreach (var batch in batches)
        foreach (string scene in batch.Scenes)
        {
            var matching = captures.Where(c => c.Profile == batch.Profile.Name && c.Language == batch.Language && c.Scene == scene).ToArray();
            if (matching.Length != 1 || matching[0].File != CaptureFiles.FileName(batch.Profile, scene, batch.Language)
                || CaptureFiles.PngSize(Path.Combine(staging, batch.RelativeDirectory, matching[0].File)) != batch.Profile.Size.Pixels)
                throw new InvalidDataException("Missing, duplicate or invalid final capture.");
        }
    }

    public static void Publish(string staging, string destination, string[] expectedImages)
    {
        IsolatedFileStore.EnsureNoReparsePoints(staging);
        IsolatedFileStore.EnsureNoReparsePoints(destination);
        CheckChildren(staging);
        string[] expected = [.. expectedImages, "result.json"];
        string[] actual = Directory.GetFiles(staging, "*", SearchOption.AllDirectories).Select(p => Path.GetRelativePath(staging, p)).ToArray();
        string[] directories = Directory.GetDirectories(staging, "*", SearchOption.AllDirectories).Select(p => Path.GetRelativePath(staging, p)).ToArray();
        var expectedDirectories = new HashSet<string>(StringComparer.Ordinal);
        foreach (string image in expectedImages)
            for (string? parent = Path.GetDirectoryName(image); !string.IsNullOrEmpty(parent); parent = Path.GetDirectoryName(parent))
                expectedDirectories.Add(parent);
        if (expected.Distinct(StringComparer.OrdinalIgnoreCase).Count() != expected.Length
            || !actual.Order(StringComparer.Ordinal).SequenceEqual(expected.Order(StringComparer.Ordinal))
            || !directories.Order(StringComparer.Ordinal).SequenceEqual(expectedDirectories.Order(StringComparer.Ordinal)))
            throw new InvalidDataException("Refusing to publish an incomplete or contaminated screenshot set.");
        // Both directories are on the output volume; a single rename commits the complete set.
        Directory.Move(staging, destination);
    }

    internal static void CheckChildren(string directory)
    {
        foreach (string path in Directory.EnumerateFileSystemEntries(directory))
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Refusing traversal through a reparse point.");
            if ((attributes & FileAttributes.Directory) != 0) CheckChildren(path);
        }
    }
}
