using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using SpanDraft.MicrosoftStore;
using Xunit;

namespace SpanDraft.Tests;

public sealed class MicrosoftStorePackagingTests : IDisposable
{
    private readonly string _temporary = Path.Combine(Path.GetTempPath(), "SpanDraft MSIX tests " + Guid.NewGuid().ToString("N"));
    private static string Repository([CallerFilePath] string source = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "../.."));
    private string PathInTest(string name) => Path.Combine(_temporary, name);

    public MicrosoftStorePackagingTests() => Directory.CreateDirectory(_temporary);
    public void Dispose() => Directory.Delete(_temporary, true);

    [Theory]
    [InlineData("0.2.0", "0.2.0.0")]
    [InlineData("1.0.0", "1.0.0.0")]
    [InlineData("0.1.0", "0.1.0.0")]
    [InlineData("65535.65535.65535", "65535.65535.65535.0")]
    public void MapsOnlyTheProductVersion(string product, string expected) => Assert.Equal(expected, PackageRules.MsixVersion(product));

    [Theory]
    [InlineData("")]
    [InlineData("01.2.3")]
    [InlineData("1.02.3")]
    [InlineData("1.2.03")]
    [InlineData("1.2")]
    [InlineData("1.2.3.0")]
    [InlineData("1.2.3-rc.1")]
    [InlineData("1.2.3+hash")]
    [InlineData("1.2.3\n")]
    [InlineData(" 1.2.3")]
    [InlineData("-1.2.3")]
    [InlineData("65536.0.0")]
    [InlineData("0.65536.0")]
    [InlineData("0.0.65536")]
    [InlineData("999999999999999.0.0")]
    public void RejectsNonCanonicalOrUnrepresentableVersions(string version) =>
        Assert.Throws<InvalidDataException>(() => PackageRules.MsixVersion(version));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SubmissionAcceptsOnlyExactReleaseCommitIncludingAnnotatedTags(bool annotated)
    {
        string root = CreateGitRepository();
        if (annotated) Git(root, "tag", "-a", "v0.2.0", "-m", "Release");
        else Git(root, "tag", "v0.2.0");
        var source = PackageRules.ValidateSource(root, "0.2.0", "Submission");
        Assert.Equal("0.2.0.0", source.MsixVersion);
        Assert.Equal(Git(root, "rev-parse", "HEAD"), source.Commit);
        Assert.False(source.Dirty);
        Assert.Equal("Submission", source.Mode);
        Assert.Equal(source, PackageRules.ValidateSource(root, "0.2.0", "Submission", source.Commit));
        File.WriteAllText(Path.Combine(root, "next.txt"), "next commit");
        Git(root, "add", "."); Git(root, "commit", "-m", "Next commit");
        Assert.Throws<InvalidDataException>(() => PackageRules.ValidateSource(root, "0.2.0", "Submission"));
        Assert.Throws<InvalidDataException>(() => PackageRules.ValidateSource(root, "0.2.0", "Development", source.Commit));
    }

    [Fact]
    public void MissingTagRequiresExplicitDevelopmentAndEffectiveVersionMustAlwaysMatch()
    {
        string root = CreateGitRepository();
        Assert.Contains("v0.2.0", Assert.Throws<InvalidDataException>(() => PackageRules.ValidateSource(root, "0.2.0", "Submission")).Message);
        Assert.Equal("Development", PackageRules.ValidateSource(root, "0.2.0", "Development").Mode);
        foreach (string mode in new[] { "Submission", "Development" })
            Assert.Throws<InvalidDataException>(() => PackageRules.ValidateSource(root, "0.3.0", mode));
        Assert.Throws<InvalidDataException>(() => PackageRules.ValidateSource(root, "0.2.0", "Typo"));
    }

    [Theory]
    [InlineData("tracked")]
    [InlineData("staged")]
    [InlineData("untracked")]
    public void SubmissionRejectsAllWorkingTreeChangesButIgnoresBuildArtifacts(string change)
    {
        string root = CreateGitRepository();
        Git(root, "tag", "v0.2.0");
        Directory.CreateDirectory(Path.Combine(root, "artifacts"));
        File.WriteAllText(Path.Combine(root, "artifacts", "ignored.txt"), "ignored");
        Assert.False(PackageRules.ValidateSource(root, "0.2.0", "Submission").Dirty);
        File.WriteAllText(Path.Combine(root, change == "untracked" ? "new.txt" : ".gitignore"), "changed");
        if (change == "staged") Git(root, "add", ".gitignore");
        Assert.Throws<InvalidDataException>(() => PackageRules.ValidateSource(root, "0.2.0", "Submission"));
        Assert.True(PackageRules.ValidateSource(root, "0.2.0", "Development").Dirty);
    }

    [Fact]
    public void StageGeneratesManifestAndAssetsAndKeepsAllPublishFiles()
    {
        string staging = Stage();
        Assert.Equal(File.ReadAllBytes(Path.Combine(Repository(), "LICENSE")), File.ReadAllBytes(Path.Combine(staging, "LICENSE")));
        Assert.Equal("extra publish content", File.ReadAllText(Path.Combine(staging, "subfolder", "extra.txt")));
        var info = PackageRules.Verify(Pack(staging), "0.2.0", staging);
        Assert.Equal(new PackageInfo("S.Antipin.SpanDraft", "CN=AC57B091-093F-4A75-87A8-A6DB7BD6BBFF", "0.2.0.0", "x64", "SpanDraft"), info);
        Assert.Throws<InvalidDataException>(() => PackageRules.Stage(Repository(), PathInTest("publish"), staging, "0.2.0"));
    }

    [Theory]
    [InlineData("Identity", "Name", "Other.Application")]
    [InlineData("Identity", "Publisher", "CN=Other")]
    [InlineData("Identity", "Version", "0.2.0.1")]
    [InlineData("Identity", "Version", "0.3.0.0")]
    [InlineData("Identity", "ProcessorArchitecture", "arm64")]
    [InlineData("TargetDeviceFamily", "Name", "Windows.Universal")]
    [InlineData("TargetDeviceFamily", "MinVersion", "10.0.17763.0")]
    [InlineData("Application", "Executable", "Other.exe")]
    [InlineData("Application", "uap10:RuntimeBehavior", "windowsApp")]
    [InlineData("Application", "uap10:TrustLevel", "appContainer")]
    [InlineData("Capability", "Name", "internetClient")]
    [InlineData("Extension", "Category", "windows.protocol")]
    [InlineData("FileTypeAssociation", "Parameters", "%1")]
    [InlineData("FileTypeAssociation", "MultiSelectModel", "Player")]
    [InlineData("VisualElements", "Square44x44Logo", "Missing.png")]
    public void RejectsAlteredManifestContracts(string element, string attribute, string value)
    {
        string staging = Stage();
        string path = Path.Combine(staging, "AppxManifest.xml");
        var manifest = XDocument.Load(path);
        XName name = attribute.StartsWith("uap10:") ? PackageRules.Uap10 + attribute[6..] : attribute;
        manifest.Descendants().Single(e => e.Name.LocalName == element).SetAttributeValue(name, value);
        manifest.Save(path);
        Assert.Throws<InvalidDataException>(() => PackageRules.Verify(Pack(staging), "0.2.0", staging));
    }

    [Theory]
    [InlineData("PublisherDisplayName", "Other publisher")]
    [InlineData("FileType", ".other")]
    public void RejectsAlteredManifestText(string element, string value)
    {
        string staging = Stage();
        string path = Path.Combine(staging, "AppxManifest.xml");
        var manifest = XDocument.Load(path);
        manifest.Descendants().Single(e => e.Name.LocalName == element).Value = value;
        manifest.Save(path);
        Assert.Throws<InvalidDataException>(() => PackageRules.Verify(Pack(staging)));
    }

    [Theory]
    [InlineData("SpanDraft.Desktop.exe")]
    [InlineData("coreclr.dll")]
    [InlineData("Help/index.html")]
    [InlineData("LICENSE")]
    [InlineData("THIRD_PARTY_NOTICES.txt")]
    [InlineData("Assets/StoreLogo.png")]
    [InlineData("AppxBlockMap.xml")]
    [InlineData("[Content_Types].xml")]
    public void RejectsMissingPackageInputs(string missing)
    {
        string staging = Stage();
        string package = Pack(staging);
        using (var archive = ZipFile.Open(package, ZipArchiveMode.Update))
            archive.GetEntry(missing)!.Delete();
        Assert.Throws<InvalidDataException>(() => PackageRules.Verify(package));
    }

    [Fact]
    public void DetectsChangedPublishContentInTheActualArchive()
    {
        string staging = Stage();
        string package = Pack(staging);
        using (var archive = ZipFile.Open(package, ZipArchiveMode.Update))
        {
            archive.GetEntry("subfolder/extra.txt")!.Delete();
            using var writer = new StreamWriter(archive.CreateEntry("subfolder/extra.txt").Open());
            writer.Write("tampered");
        }
        Assert.Throws<InvalidDataException>(() => PackageRules.Verify(package, "0.2.0", staging));
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("architecture")]
    [InlineData("asset")]
    [InlineData("duplicate")]
    public void DetectsInvalidPayloads(string mutation)
    {
        string staging = Stage();
        if (mutation == "empty") File.WriteAllBytes(Path.Combine(staging, "LICENSE"), []);
        if (mutation == "architecture") File.WriteAllBytes(Path.Combine(staging, "SpanDraft.Desktop.exe"), Executable(0xaa64));
        if (mutation == "asset") File.Copy(Path.Combine(staging, "Assets/StoreLogo.png"), Path.Combine(staging, "Assets/Square44x44Logo.png"), true);
        string package = Pack(staging);
        if (mutation == "duplicate")
        {
            using var archive = ZipFile.Open(package, ZipArchiveMode.Update);
            using var writer = new StreamWriter(archive.CreateEntry("license").Open());
            writer.Write("duplicate ignoring case");
        }
        Assert.Throws<InvalidDataException>(() => PackageRules.Verify(package));
    }

    private string Stage()
    {
        string publish = PathInTest("publish");
        Directory.CreateDirectory(publish);
        foreach (string path in PackageRules.RequiredFiles.Where(p => p != "LICENSE"))
        {
            string file = Path.Combine(publish, path);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, "fixture content");
        }
        File.WriteAllBytes(Path.Combine(publish, "SpanDraft.Desktop.exe"), Executable(0x8664));
        Directory.CreateDirectory(Path.Combine(publish, "subfolder"));
        File.WriteAllText(Path.Combine(publish, "subfolder", "extra.txt"), "extra publish content");
        string staging = PathInTest("staging");
        PackageRules.Stage(Repository(), publish, staging, "0.2.0");
        return staging;
    }

    private string Pack(string staging)
    {
        string package = PathInTest("test.msix");
        ZipFile.CreateFromDirectory(staging, package);
        using var archive = ZipFile.Open(package, ZipArchiveMode.Update);
        foreach (string name in new[] { "AppxBlockMap.xml", "[Content_Types].xml" })
        {
            using var writer = new StreamWriter(archive.CreateEntry(name).Open());
            writer.Write("Container metadata fixture; MakeAppx validation runs only on Windows.");
        }
        return package;
    }

    // A minimal PE header fixture, not an executable MSIX or an installation test.
    private static byte[] Executable(ushort machine)
    {
        using var stream = new MemoryStream(new byte[128], true);
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0x5a4d);
        stream.Position = 0x3c; writer.Write(0x40);
        stream.Position = 0x40; writer.Write(0x4550); writer.Write(machine);
        return stream.ToArray();
    }

    private string CreateGitRepository()
    {
        string root = PathInTest("repo");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "Directory.Build.props"), "<Project><PropertyGroup><Version>0.2.0</Version></PropertyGroup></Project>");
        File.WriteAllText(Path.Combine(root, ".gitignore"), "artifacts/\n");
        Git(root, "init"); Git(root, "add", "."); Git(root, "commit", "-m", "Initial");
        return root;
    }

    private static string Git(string root, params string[] arguments)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in new[] { "-c", "user.name=MSIX Tests", "-c", "user.email=msix-tests@example.invalid", "-c", "commit.gpgsign=false", "-c", "tag.gpgsign=false" }.Concat(arguments))
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, error);
        return output.Trim();
    }
}
