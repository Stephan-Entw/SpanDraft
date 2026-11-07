using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SpanDraft.Desktop.Persistence;

namespace SpanDraft.Screenshots;

public sealed record CaptureSize(int Width, int Height)
{
    public (int Width, int Height) Pixels => Width > 0 && Height > 0
        ? (Width, Height) : throw new ArgumentException("Capture dimensions must be positive pixel counts.");
}

public sealed record CaptureProfile(string Name, string Directory, CaptureSize Size, string[] Scenes)
{
    public static readonly CaptureProfile Store = new("Store", "store", new(3312, 1863), CaptureFiles.Scenes);
    public static readonly CaptureProfile PenpotHero = new("PenpotHero", "penpot", new(2400, 1834), ["Project"]);
}

public sealed record CaptureBatch(CaptureProfile Profile, string Language, string[] Scenes, int ScalePercent)
{
    public static CaptureBatch[] Select(string target, string language, string scene, int scalePercent, int penpotScalePercent)
    {
        CaptureScaling.ExpectedDpi(scalePercent);
        CaptureScaling.ExpectedDpi(penpotScalePercent);
        string[] targets = CaptureFiles.Select(target, ["Store", "Penpot"]);
        string[] languages = CaptureFiles.Select(language, CaptureFiles.Languages);
        string[] scenes = CaptureFiles.Select(scene, CaptureFiles.Scenes);
        if (targets.Contains("Penpot") && scenes.Any(s => s != "Project") && !scene.Equals("All", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Penpot supports only Project. Use -Target Store for other individual scenes.");
        return targets.SelectMany(t =>
        {
            var profile = t == "Store" ? CaptureProfile.Store : CaptureProfile.PenpotHero;
            return languages.Select(l => new CaptureBatch(profile, l, scenes.Where(profile.Scenes.Contains).ToArray(),
                t == "Store" ? scalePercent : penpotScalePercent));
        }).ToArray();
    }

    public string RelativeDirectory => Profile.Name == "Store"
        ? Path.Combine(Profile.Directory, Language switch
        {
            "de-DE" => "DE",
            "en-US" => "EN",
            _ => throw new ArgumentException("Unknown screenshot culture.")
        }) : Profile.Directory;

    public string[] RelativeFiles => Scenes.Select(s => Path.Combine(RelativeDirectory, CaptureFiles.FileName(Profile, s, Language))).ToArray();
}

public static class CaptureScaling
{
    public static uint ExpectedDpi(int scalePercent) => scalePercent > 0
        ? checked((uint)Math.Round(scalePercent * 96d / 100, MidpointRounding.AwayFromZero))
        : throw new ArgumentOutOfRangeException(nameof(scalePercent), "ScalePercent must be a positive integer.");

    public static bool Matches(uint dpi, double renderScaling, int scalePercent) => dpi == ExpectedDpi(scalePercent)
        && double.IsFinite(renderScaling) && Math.Abs(renderScaling - dpi / 96d) < 0.000001;
}

public static class CaptureFiles
{
    public const string FixtureHash = "46EEA4FECFAE14FEAA059F5E3CCD3C6A623191C0FF3E3647754C608B9E046931";
    public static readonly string[] Languages = ["de-DE", "en-US"];
    public static readonly string[] Scenes = ["Start", "Project", "Section", "Materials", "Report"];
    public static readonly string[] ExampleNames = ["Crane Beam.spandraft", "Cantilever Beam.spandraft", "Simply Supported Beam.spandraft", "Overhang Beam.spandraft", "Fixed-End Beam.spandraft"];
    public static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    public static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    public static string FileName(CaptureProfile profile, string scene, string culture)
    {
        int index = Array.IndexOf(Scenes, scene);
        if (index < 0 || !profile.Scenes.Contains(scene)) throw new ArgumentException("Unknown screenshot motif for this profile.", nameof(scene));
        if (!Languages.Contains(culture)) throw new ArgumentException("Unknown screenshot culture.", nameof(culture));
        return profile.Name switch
        {
            "PenpotHero" => $"hero-project-{culture}.png",
            // Capture Start before opening the project, but number it last in the output.
            "Store" => $"{(index == 0 ? Scenes.Length : index):00}-{scene.ToLowerInvariant()}-{culture}.png",
            _ => throw new ArgumentException("Unknown capture profile.", nameof(profile))
        };
    }

    public static RecentProjectEntry[] DemoRecents(DateTimeOffset referenceTime)
    {
        TimeSpan[] ages = [new(0, 1, 20, 0), new(0, 6, 45, 0), new(1, 3, 10, 0), new(4, 8, 35, 0), new(11, 2, 50, 0)];
        return ExampleNames.Select((name, i) => new RecentProjectEntry(@"C:\SpanDraft\Examples\" + name,
            referenceTime.ToUniversalTime() - ages[i])).ToArray();
    }

    public static void AssertDemoRecents(RecentProjectEntry[] entries, DateTimeOffset referenceTime)
    {
        if (!entries.SequenceEqual(DemoRecents(referenceTime)))
            throw new InvalidDataException("The start page must contain the five neutral demo recent paths and their prepared timestamps.");
        foreach (var entry in entries) AssertNoPersonalPaths(entry.Path);
    }

    public static void AssertFixture(string path)
    {
        if (Hash(path) != FixtureHash) throw new InvalidOperationException("The supplied Crane Beam fixture has changed.");
        AssertNoPersonalPaths(File.ReadAllText(path));
    }

    public static void AssertNoPersonalPaths(string text)
    {
        string normalized = text.Replace("\\\\", "\\").Replace('/', '\\');
        if (normalized.Contains("\\Users\\", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("\\Documents and Settings\\", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("\\home\\", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrEmpty(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)))
            throw new InvalidOperationException("Personal paths are not allowed in screenshot inputs or visible paths.");
    }

    public static void AssertVisibleText(string text, string repo)
    {
        AssertNoPersonalPaths(text);
        if (text.Contains(repo, StringComparison.OrdinalIgnoreCase) || text.Contains("artifacts", StringComparison.OrdinalIgnoreCase)
            || text.Contains(".runs", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("An internal tooling path is visible in the captured application.");
    }

    public static (int Width, int Height) PngSize(string path)
    {
        using var stream = File.OpenRead(path);
        Span<byte> header = stackalloc byte[24]; stream.ReadExactly(header);
        if (!header[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
            || !header.Slice(12, 4).SequenceEqual("IHDR"u8)) throw new InvalidDataException("Invalid PNG header.");
        return (BinaryPrimitives.ReadInt32BigEndian(header[16..20]), BinaryPrimitives.ReadInt32BigEndian(header[20..24]));
    }

    public static string[] Select(string value, string[] allowed) => value.Equals("All", StringComparison.OrdinalIgnoreCase)
        ? allowed : [allowed.FirstOrDefault(v => v.Equals(value, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"Unsupported selection: {value}.")];

    public static T Read<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json)
        ?? throw new InvalidDataException("Missing JSON object.");
    public static void Write<T>(string path, T value) => File.WriteAllText(path, JsonSerializer.Serialize(value, Json), new UTF8Encoding(false));
}

public sealed record WorkerRequest(string RepoRoot, string RunRoot, string Fixture, string WinApp,
    string Language, string[] Scenes, CaptureProfile Profile, int ScalePercent, DateTimeOffset BuildFinishedUtc, DisplaySnapshot Display);

public sealed record CaptureEvidence(string Profile, string Language, string Scene, string File, int Width, int Height,
    uint WindowDpi, double RenderScaling, NativeRect WindowBounds, NativeRect VisibleBounds, NativeRect ClientBounds,
    CaptionEvidence Chrome, long Hwnd, int ProcessId, string Executable, DateTimeOffset ProcessStartedUtc,
    DateTimeOffset CapturedUtc);

public sealed record CapturePhase(int ScalePercent, DateTimeOffset StartedUtc, DisplaySnapshot Display)
{
    public DateTimeOffset? FinishedUtc { get; set; }
    public DisplayDevice[]? DisplaysAfter { get; set; }
    public bool DisplaysUnchanged => DisplaysAfter is not null && Display.Devices.SequenceEqual(DisplaysAfter);
}

public sealed record WorkerResult(CaptureEvidence[] Captures, string FixtureHash, string[] AccessedPaths,
    RecentProjectEntry[] StartRecents, string? SelectedBuiltInMaterial);
