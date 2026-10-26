using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SpanDraft.Desktop.Persistence;

namespace SpanDraft.StoreScreenshots;

public sealed record CaptureSize(double WidthDip, double HeightDip)
{
    public (int Width, int Height) Pixels
    {
        get
        {
            static int Dimension(double value)
            {
                double pixels = value * 2;
                if (!double.IsFinite(pixels) || pixels <= 0 || pixels > int.MaxValue || pixels != Math.Truncate(pixels))
                    throw new ArgumentException("Window dimensions must be positive, finite and represent whole pixels at 200%.");
                return (int)pixels;
            }
            return (Dimension(WidthDip), Dimension(HeightDip));
        }
    }
}

public static class CaptureFiles
{
    public const string FixtureHash = "9439FB06C3A36F1B063F4F37DAAD2C1B087A1DDBB3AE3A9DDCE2ED6D20108B8F";
    public static readonly string[] Languages = ["de-DE", "en-US"];
    public static readonly string[] Scenes = ["Start", "Project", "Section", "Report", "Materials"];
    public static readonly string[] ExampleNames = ["Crane Beam.spandraft", "Cantilever Beam.spandraft", "Simply Supported Beam.spandraft", "Overhang Beam.spandraft", "Fixed-End Beam.spandraft"];
    public static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    public static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    public static string FileName(string scene, string culture)
    {
        int index = Array.IndexOf(Scenes, scene);
        if (index < 0) throw new ArgumentException("Unknown screenshot motif.", nameof(scene));
        if (!Languages.Contains(culture)) throw new ArgumentException("Unknown screenshot culture.", nameof(culture));
        return $"{index + 1:00}-{scene.ToLowerInvariant()}-{culture}.png";
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
    string Language, string[] Scenes, CaptureSize Size, DateTimeOffset BuildFinishedUtc, DisplaySnapshot Display);

public sealed record CaptureEvidence(string Language, string Scene, string File, int Width, int Height,
    uint WindowDpi, double RenderScaling, NativeRect WindowBounds, NativeRect VisibleBounds, NativeRect ClientBounds,
    CaptionEvidence Chrome, long Hwnd, int ProcessId, string Executable, DateTimeOffset ProcessStartedUtc);

public sealed record WorkerResult(CaptureEvidence[] Captures, string FixtureHash, string[] AccessedPaths,
    RecentProjectEntry[] StartRecents, string? SelectedBuiltInMaterial);
