using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;
using Xunit;
using static SpanDraft.Tests.ProjectTestSupport;

namespace SpanDraft.Tests;

public sealed class ProjectIdentityTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NameEqualityFollowsTheResolvedDirectoryMode(bool caseSensitive)
    {
        var resolver = new TestIdentityResolver(path => new(path, caseSensitive));
        var lower = resolver.Resolve(TestPath("beam.spandraft"));
        var upper = resolver.Resolve(TestPath("Beam.spandraft"));
        Assert.Equal(!caseSensitive, lower.Equals(upper));
        Assert.Equal(!caseSensitive, new HashSet<ProjectIdentity> { lower }.Contains(upper));
    }

    [Fact]
    public void ExistingAndProspectivePathsFollowTheActualDirectoryCaseSemantics()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            string existing = Path.Combine(directory.FullName, "CaseWitness.spandraft");
            string variant = Path.Combine(directory.FullName, "caseWitness.spandraft");
            File.WriteAllText(existing, "witness");
            bool insensitive = File.Exists(variant);
            var resolver = new ProjectIdentityResolver();

            Assert.Equal(insensitive, resolver.Resolve(existing).Equals(resolver.Resolve(variant)));
            var one = resolver.Resolve(Path.Combine(directory.FullName, "New.spandraft"));
            var two = resolver.Resolve(Path.Combine(directory.FullName, "new.spandraft"));
            Assert.Equal(insensitive, one.Equals(two));
            Assert.False(File.Exists(one.CanonicalPath));
            File.WriteAllText(one.CanonicalPath, "new");
            Assert.Equal(one, resolver.Resolve(one.CanonicalPath));
            if (insensitive) Assert.Equal(one, resolver.Resolve(two.CanonicalPath));
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public async Task NativeCaseQueryAllowsSavingInAnEmptyDirectory()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            if (OperatingSystem.IsLinux() && new DriveInfo(directory.FullName).DriveFormat is not ("ext2" or "ext3" or "ext4"))
                return;

            var resolver = new ProjectIdentityResolver();
            string path = Path.Combine(directory.FullName, "New.spandraft");
            var identity = resolver.Resolve(path);
            var variant = resolver.Resolve(Path.Combine(directory.FullName, "new.spandraft"));
            Assert.Empty(directory.EnumerateFileSystemInfos());

            var dialogs = new Dialogs { SavePath = path };
            using var main = new MainWindowViewModel(dialogs: dialogs, projectIdentities: resolver);
            var state = State();
            main.ActivatePathlessSession(ProjectSession.Create(state));
            Assert.True(await main.SaveAsync());
            Assert.True(state.ContentEquals(ProjectFileCodec.Deserialize(File.ReadAllBytes(path))));
            Assert.Equal(File.Exists(variant.CanonicalPath), identity.Equals(variant));
            Assert.Equal(identity, resolver.Resolve(path));
            Assert.Single(directory.EnumerateFileSystemInfos());
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public void ApfsCanonicalUnicodeAliasesShareASaveReservationBeforeAndAfterWriting()
    {
        if (!OperatingSystem.IsMacOS()) return;
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            if (new DriveInfo(directory.FullName).DriveFormat != "apfs") return;
            const string composed = "Caf\u00e9.spandraft", decomposed = "Cafe\u0301.spandraft";
            var resolver = new ProjectIdentityResolver();
            string path = Path.Combine(directory.FullName, composed), alias = Path.Combine(directory.FullName, decomposed);
            var identity = resolver.Resolve(path);
            var aliasBeforeSave = resolver.Resolve(alias);
            Assert.Equal(identity, aliasBeforeSave);
            Assert.Equal(composed, Path.GetFileName(identity.CanonicalPath));
            Assert.Equal(decomposed, Path.GetFileName(aliasBeforeSave.CanonicalPath));

            var registry = new ProjectPathRegistry();
            var owner = WindowId.Create();
            Assert.Equal(SaveTargetDecision.Reserved, registry.ArbitrateSave(identity, owner, out var reservation));
            Assert.Equal(SaveTargetDecision.Rejected, registry.ArbitrateSave(aliasBeforeSave, WindowId.Create(), out _));
            var pending = registry.ArbitrateOpen(aliasBeforeSave);
            Assert.Equal(ProjectPathState.SaveTargetReserved, pending.State);

            File.WriteAllText(identity.CanonicalPath, "project");
            Assert.True(File.Exists(alias));
            Assert.Equal(identity, resolver.Resolve(alias));
            var session = ProjectSession.Create(State());
            Assert.True(registry.TryCommitSave(reservation!, session.PrepareSave(session.CurrentRevision, identity.CanonicalPath)));
            Assert.Equal(owner, registry.ArbitrateOpen(resolver.Resolve(alias)).Owner);
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public void DirectoryAndFileSymlinksResolveToTheSameIdentityIncludingNewTargets()
    {
        // Win32 symlink creation can require Developer Mode or elevation. Path aliases on Windows
        // are also covered by the deterministic workflow tests.
        if (OperatingSystem.IsWindows()) return;
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            string realParent = directory.CreateSubdirectory("real").FullName;
            string linkParent = Path.Combine(directory.FullName, "link");
            Directory.CreateSymbolicLink(linkParent, "real");
            string file = Path.Combine(realParent, "beam.spandraft");
            string linkFile = Path.Combine(directory.FullName, "linked.spandraft");
            File.WriteAllText(file, "project");
            File.CreateSymbolicLink(linkFile, Path.Combine("link", "beam.spandraft"));
            var resolver = new ProjectIdentityResolver();
            var identity = resolver.Resolve(file);

            Assert.Equal(identity, resolver.Resolve(linkFile));
            Assert.Equal(identity, resolver.Resolve(Path.Combine(linkParent, ".", "beam.spandraft")));
            var newTarget = resolver.Resolve(Path.Combine(linkParent, "new.spandraft"));
            Assert.Equal(resolver.Resolve(Path.Combine(realParent, "new.spandraft")), newTarget);
            Assert.Equal(Path.GetDirectoryName(identity.CanonicalPath), Path.GetDirectoryName(newTarget.CanonicalPath));
            Assert.False(File.Exists(newTarget.CanonicalPath));
        }
        finally { directory.Delete(recursive: true); }
    }
}
