using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using SkiaSharp;

namespace SpanDraft.MicrosoftStore;

internal sealed record SourceInfo(string ProductVersion, string MsixVersion, string Commit, bool Dirty, string Mode);
internal sealed record PackageInfo(string Name, string Publisher, string Version, string Architecture, string ApplicationId);

internal static class PackageRules
{
    internal const string IdentityName = "S.Antipin.SpanDraft";
    internal const string Publisher = "CN=AC57B091-093F-4A75-87A8-A6DB7BD6BBFF";
    internal static readonly XNamespace Foundation = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
    internal static readonly XNamespace Uap = "http://schemas.microsoft.com/appx/manifest/uap/windows10";
    internal static readonly XNamespace Uap3 = "http://schemas.microsoft.com/appx/manifest/uap/windows10/3";
    internal static readonly XNamespace Uap10 = "http://schemas.microsoft.com/appx/manifest/uap/windows10/10";
    internal static readonly XNamespace Rescap = "http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities";
    internal static readonly (string Path, int Size)[] Assets = CreateAssets();
    internal static readonly string[] RequiredFiles =
    [
        "SpanDraft.Desktop.exe", "SpanDraft.Desktop.dll", "SpanDraft.Desktop.deps.json", "SpanDraft.Desktop.runtimeconfig.json",
        "coreclr.dll", "hostfxr.dll", "hostpolicy.dll", "System.Private.CoreLib.dll", "Avalonia.Desktop.dll", "libSkiaSharp.dll",
        "LICENSE", "THIRD_PARTY_NOTICES.txt", "Help/index.html", "Help/js/spandraft-help.js", "Help/js/iframe-worker-shim.js"
    ];

    private static (string Path, int Size)[] CreateAssets()
    {
        var assets = new List<(string Path, int Size)> { ("Assets/ProjectFileLogo.png", 256) };
        foreach (var (name, size) in new[] { ("StoreLogo", 50), ("Square44x44Logo", 44), ("Square150x150Logo", 150) })
            foreach (int scale in new[] { 100, 125, 150, 200, 250, 300, 400 })
            {
                string qualifier = scale == 100 ? "" : $".scale-{scale}";
                assets.Add(($"Assets/{name}{qualifier}.png", (size * scale + 99) / 100));
            }
        foreach (int size in new[] { 16, 20, 24, 30, 32, 36, 40, 48, 60, 64, 72, 80, 96, 256 })
            foreach (string form in new[] { "", "_altform-unplated", "_altform-lightunplated" })
                assets.Add(($"Assets/Square44x44Logo.targetsize-{size}{form}.png", size));
        return [.. assets];
    }

    internal static string MsixVersion(string version)
    {
        Require(Regex.IsMatch(version, @"\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\z", RegexOptions.CultureInvariant)
            && version.Split('.').All(part => ushort.TryParse(part, out _)),
            "Product version must be canonical X.Y.Z with each component between 0 and 65535.");
        return version + ".0";
    }

    internal static SourceInfo ValidateSource(string root, string effectiveVersion, string mode, string? expectedHead = null)
    {
        Require(mode is "Submission" or "Development", "Expected Submission or Development mode.");
        string version = XDocument.Load(Path.Combine(root, "Directory.Build.props")).Descendants("Version").Single().Value;
        string msixVersion = MsixVersion(version);
        Require(version == effectiveVersion, $"Effective Desktop MSBuild version '{effectiveVersion}' differs from Directory.Build.props '{version}'.");
        string head = Git(root, "rev-parse", "--verify", "HEAD");
        Require(expectedHead is null || head == expectedHead, "HEAD changed during packaging.");
        bool dirty = Git(root, "status", "--porcelain=v1", "--untracked-files=all").Length != 0;
        if (mode == "Submission")
        {
            Require(!dirty, "Submission requires a clean Git working tree, including untracked files. Use -Development for local test packages.");
            string tag = "v" + version;
            string tagHead;
            try { tagHead = Git(root, "rev-parse", "--verify", $"refs/tags/{tag}^{{commit}}"); }
            catch (InvalidOperationException error)
            {
                throw new InvalidDataException($"Submission requires release tag {tag}. Fetch the published tag, or use -Development for local test packages.", error);
            }
            Require(tagHead == head,
                $"Submission requires HEAD to equal release tag {tag}. Use -Development for local test packages.");
        }
        return new(version, msixVersion, head, dirty, mode);
    }

    internal static PackageInfo Stage(string root, string publish, string staging, string version)
    {
        string msixVersion = MsixVersion(version);
        Require(!Directory.Exists(staging), "Staging directory must not already exist.");
        Directory.CreateDirectory(staging);
        foreach (string source in Directory.EnumerateFiles(publish, "*", SearchOption.AllDirectories))
        {
            string destination = Path.Combine(staging, Path.GetRelativePath(publish, source));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination);
        }
        File.Copy(Path.Combine(root, "LICENSE"), Path.Combine(staging, "LICENSE"));
        var manifest = XDocument.Load(Path.Combine(root, "packaging/MicrosoftStore/AppxManifest.xml"));
        manifest.Root!.Element(Foundation + "Identity")!.SetAttributeValue("Version", msixVersion);
        manifest.Save(Path.Combine(staging, "AppxManifest.xml"));
        // Derive manifest assets from the existing app and project file icons.
        using var icon = SKBitmap.Decode(Path.Combine(root, "src/SpanDraft.Desktop/Assets/AppIcon/app-icon-1024.png"))
            ?? throw new InvalidDataException("Cannot decode the existing SpanDraft app icon.");
        using var projectIcon = SKBitmap.Decode(Path.Combine(root, "src/SpanDraft.Desktop/Assets/ProjectFileIcon/spandraft-project.ico"))
            ?? throw new InvalidDataException("Cannot decode the existing SpanDraft project file icon.");
        foreach (var asset in Assets)
        {
            string destination = Path.Combine(staging, asset.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            var sourceIcon = asset.Path == "Assets/ProjectFileLogo.png" ? projectIcon : icon;
            using var resized = sourceIcon.Resize(new SKImageInfo(asset.Size, asset.Size), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
            using var data = resized.Encode(SKEncodedImageFormat.Png, 100);
            using var output = File.Create(destination);
            data.SaveTo(output);
        }
        return ValidateContents(path => File.ReadAllBytes(Path.Combine(staging, path)), version);
    }

    internal static PackageInfo Verify(string package, string? version = null, string? staging = null)
    {
        using var archive = ZipFile.OpenRead(package);
        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries.Where(entry => !entry.FullName.EndsWith('/')))
        {
            string path = entry.FullName.Replace('\\', '/');
            Require(!path.StartsWith('/') && !path.Split('/').Any(part => part is ".." or "." or ""), $"Invalid package path: {path}");
            Require(entries.TryAdd(path, entry), $"Duplicate package path: {path}");
        }
        byte[] Read(string path)
        {
            Require(entries.TryGetValue(path, out var entry), $"Missing package file: {path}");
            using var input = entry!.Open();
            using var output = new MemoryStream();
            input.CopyTo(output);
            return output.ToArray();
        }
        var result = ValidateContents(Read, version);
        foreach (string metadata in new[] { "AppxBlockMap.xml", "[Content_Types].xml", "resources.pri" })
            Require(Read(metadata).Length > 0, $"Missing or empty MSIX metadata: {metadata}");
        if (staging is not null)
        {
            foreach (string file in Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories))
            {
                string path = Path.GetRelativePath(staging, file).Replace('\\', '/');
                Require(SHA256.HashData(File.ReadAllBytes(file)).SequenceEqual(SHA256.HashData(Read(path))),
                    $"Package differs from staging: {path}");
            }
        }
        return result;
    }

    private static PackageInfo ValidateContents(Func<string, byte[]> read, string? version)
    {
        using var input = new MemoryStream(read("AppxManifest.xml"));
        var manifest = XDocument.Load(input);
        var identity = manifest.Root!.Element(Foundation + "Identity")!;
        string actualVersion = (string?)identity.Attribute("Version") ?? "";
        Require(actualVersion.EndsWith(".0", StringComparison.Ordinal), "MSIX revision must be zero.");
        string expectedVersion = MsixVersion(version ?? actualVersion[..^2]);
        Expect(identity, "Name", IdentityName);
        Expect(identity, "Publisher", Publisher);
        Expect(identity, "Version", expectedVersion);
        Expect(identity, "ProcessorArchitecture", "x64");
        var properties = manifest.Root.Element(Foundation + "Properties")!;
        Require((string?)properties.Element(Foundation + "PublisherDisplayName") == "S. Antipin", "Unexpected PublisherDisplayName.");
        Require((string?)properties.Element(Foundation + "Logo") == @"Assets\StoreLogo.png", "Unexpected package logo.");
        var family = manifest.Descendants(Foundation + "TargetDeviceFamily").Single();
        Expect(family, "Name", "Windows.Desktop");
        Expect(family, "MinVersion", "10.0.19041.0");
        Expect(family, "MaxVersionTested", "10.0.19041.0");
        var app = manifest.Descendants(Foundation + "Application").Single();
        Expect(app, "Id", "SpanDraft");
        Expect(app, "Executable", "SpanDraft.Desktop.exe");
        Expect(app, Uap10 + "RuntimeBehavior", "packagedClassicApp");
        Expect(app, Uap10 + "TrustLevel", "mediumIL");
        Require(app.Attribute("EntryPoint") is null, "Use modern packaged-classic activation without a redundant EntryPoint.");
        var capability = manifest.Descendants(Rescap + "Capability").Single();
        Expect(capability, "Name", "runFullTrust");
        var extension = app.Descendants(Uap3 + "Extension").Single();
        Expect(extension, "Category", "windows.fileTypeAssociation");
        var association = extension.Element(Uap3 + "FileTypeAssociation")!;
        Expect(association, "Name", "spandraft");
        Expect(association, "Parameters", "\"%1\"");
        Expect(association, "MultiSelectModel", "Single");
        Require((string?)association.Element(Uap + "Logo") == @"Assets\ProjectFileLogo.png", "Expected the project file icon for the .spandraft association.");
        Require(association.Element(Uap + "SupportedFileTypes")?.Elements(Uap + "FileType").Select(e => e.Value).SequenceEqual([".spandraft"]) == true,
            "Expected only the .spandraft association in the uap namespace.");
        Require(!manifest.Descendants().Attributes().Any(a => a.Name.LocalName == "AllowSilentDefaultTakeOver"), "Default-app takeover is not allowed.");
        var visual = app.Element(Uap + "VisualElements")!;
        Expect(visual, "Square44x44Logo", @"Assets\Square44x44Logo.png");
        Expect(visual, "Square150x150Logo", @"Assets\Square150x150Logo.png");
        foreach (string path in RequiredFiles) Require(read(path).Length > 0, $"Empty package file: {path}");
        foreach (var asset in Assets)
        {
            using var image = SKBitmap.Decode(read(asset.Path));
            Require(image is not null && image.Width == asset.Size && image.Height == asset.Size, $"Invalid asset size: {asset.Path}");
        }
        using var executable = new BinaryReader(new MemoryStream(read("SpanDraft.Desktop.exe")));
        Require(executable.ReadUInt16() == 0x5a4d, "Expected a Windows executable.");
        executable.BaseStream.Position = 0x3c;
        int peOffset = executable.ReadInt32();
        Require(peOffset >= 0x40 && peOffset <= executable.BaseStream.Length - 6, "Invalid PE header offset.");
        executable.BaseStream.Position = peOffset;
        Require(executable.ReadUInt32() == 0x4550 && executable.ReadUInt16() == 0x8664, "Expected an x64 Windows executable.");
        return new(IdentityName, Publisher, expectedVersion, "x64", "SpanDraft");
    }

    private static void Expect(XElement element, XName attribute, string value) =>
        Require((string?)element.Attribute(attribute) == value, $"Expected {element.Name.LocalName}/@{attribute.LocalName} = '{value}'.");

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }

    private static string Git(string root, params string[] arguments)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start git.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {error.GetAwaiter().GetResult().Trim()}");
        return output.GetAwaiter().GetResult().Trim();
    }
}
