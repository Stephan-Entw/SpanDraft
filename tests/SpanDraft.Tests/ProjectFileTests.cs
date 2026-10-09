using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.State;
using Xunit;
using static SpanDraft.Tests.ProjectTestSupport;

namespace SpanDraft.Tests;

public sealed class ProjectFileTests
{
    [Theory]
    [InlineData(0, "de-DE")]
    [InlineData(1, "en-US")]
    [InlineData(2, "de-DE")]
    [InlineData(3, "en-US")]
    [InlineData(4, "de-DE")]
    [InlineData(4, "en-US")]
    public void FullRoundtripPreservesEveryValueAndTypeAcrossCultures(int section, string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        var previousUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            var state = State(section);
            var bytes = ProjectFileCodec.Serialize(state);
            var loaded = ProjectFileCodec.Deserialize(bytes);
            Assert.True(state.ContentEquals(loaded));
            Assert.Equal(2, JsonNode.Parse(bytes)!["formatVersion"]!.GetValue<int>());
            Assert.Equal(BitConverter.DoubleToInt64Bits(state.Document.Length.Meters), BitConverter.DoubleToInt64Bits(loaded.Document.Length.Meters));
            Assert.Equal(state.Document.NamedEntities, loaded.Document.NamedEntities);
            Assert.Equal(state.Document.NamingState, loaded.Document.NamingState);
            var json = Encoding.UTF8.GetString(bytes);
            Assert.Contains("\n", json); Assert.Contains("\"format\": \"SpanDraft.Project\"", json);
            Assert.Contains("210000000000", json);
            foreach (string excluded in new[] { "revisionId", "savedRevision", "filePath", "analysis", "$type", "SpanDraft.Core" })
                Assert.DoesNotContain(excluded, json);
        }
        finally { CultureInfo.CurrentCulture = previous; CultureInfo.CurrentUICulture = previousUi; }
    }

    [Theory]
    [InlineData("format")]
    [InlineData("formatVersion")]
    [InlineData("document")]
    [InlineData("presentation")]
    [InlineData("document.bendingAxis")]
    [InlineData("document.length")]
    [InlineData("document.material")]
    [InlineData("document.material.name")]
    [InlineData("document.material.youngsModulus")]
    [InlineData("document.material.yieldStrength")]
    [InlineData("document.section.type")]
    [InlineData("document.section.width")]
    [InlineData("document.section.height")]
    [InlineData("document.supports")]
    [InlineData("document.supports.0.id")]
    [InlineData("document.supports.0.name")]
    [InlineData("document.supports.0.type")]
    [InlineData("document.supports.0.position")]
    [InlineData("document.pointLoads")]
    [InlineData("document.pointLoads.0.id")]
    [InlineData("document.pointLoads.0.name")]
    [InlineData("document.pointLoads.0.type")]
    [InlineData("document.pointLoads.0.position")]
    [InlineData("document.pointLoads.0.value")]
    [InlineData("document.distributedLoads")]
    [InlineData("document.distributedLoads.0.id")]
    [InlineData("document.distributedLoads.0.name")]
    [InlineData("document.distributedLoads.0.startPosition")]
    [InlineData("document.distributedLoads.0.endPosition")]
    [InlineData("document.distributedLoads.0.intensity")]
    [InlineData("document.namingState")]
    [InlineData("document.namingState.nextSupportOrdinal")]
    [InlineData("document.namingState.nextForceNumber")]
    [InlineData("document.namingState.nextMomentNumber")]
    [InlineData("document.namingState.nextDistributedLoadNumber")]
    [InlineData("presentation.annotationOffsets")]
    [InlineData("presentation.annotationOffsets.0.entityId")]
    [InlineData("presentation.annotationOffsets.0.dx")]
    [InlineData("presentation.annotationOffsets.0.dy")]
    public void MissingRequiredFieldIsRejectedEvenWhenItsDefaultWouldBeValid(string path)
    {
        var root = JsonNode.Parse(ProjectFileCodec.Serialize(State()))!;
        Remove(root, path);
        Assert.Throws<ProjectFormatException>(() => Read(root));
    }

    [Theory]
    [InlineData("format", "Other")]
    [InlineData("formatVersion", 0)]
    [InlineData("formatVersion", 3)]
    [InlineData("document.length", 0)]
    [InlineData("document.length", -1)]
    [InlineData("document.material.youngsModulus", 0)]
    [InlineData("document.material.yieldStrength", -1)]
    [InlineData("document.section.type", "future")]
    [InlineData("document.section.width", 0)]
    [InlineData("document.supports.0.id", "00000000-0000-0000-0000-000000000000")]
    [InlineData("document.supports.0.id", "invalid")]
    [InlineData("document.supports.0.type", "future")]
    [InlineData("document.supports.0.name", " ")]
    [InlineData("document.supports.0.name", " A ")]
    [InlineData("document.supports.0.position", -1)]
    [InlineData("document.supports.0.position", 2)]
    [InlineData("document.pointLoads.0.type", "future")]
    [InlineData("document.pointLoads.0.position", -1)]
    [InlineData("document.pointLoads.0.position", 2)]
    [InlineData("document.distributedLoads.0.startPosition", -1)]
    [InlineData("document.distributedLoads.0.endPosition", 0)]
    [InlineData("document.distributedLoads.0.endPosition", 2)]
    [InlineData("document.namingState.nextSupportOrdinal", 0)]
    [InlineData("document.namingState.nextForceNumber", -1)]
    [InlineData("document.namingState.nextMomentNumber", 0)]
    [InlineData("document.namingState.nextDistributedLoadNumber", 0)]
    [InlineData("presentation.annotationOffsets.0.entityId", "00000000-0000-0000-0000-000000000000")]
    public void InvalidKnownDataIsRejected(string path, object value)
    {
        var root = JsonNode.Parse(ProjectFileCodec.Serialize(State()))!;
        Set(root, path, JsonValue.Create(value));
        Assert.Throws<ProjectFormatException>(() => Read(root));
    }

    [Theory]
    [InlineData("document")]
    [InlineData("document.supports")]
    [InlineData("document.supports.0")]
    [InlineData("document.material.name")]
    [InlineData("document.section.width")]
    [InlineData("document.pointLoads.0.value")]
    [InlineData("presentation.annotationOffsets")]
    [InlineData("presentation.annotationOffsets.0.dx")]
    public void NullIsNotTreatedAsMissingDefaults(string path)
    {
        var root = JsonNode.Parse(ProjectFileCodec.Serialize(State()))!;
        Set(root, path, null);
        Assert.Throws<ProjectFormatException>(() => Read(root));
    }

    [Theory]
    [InlineData("id")]
    [InlineData("name")]
    [InlineData("position")]
    [InlineData("offset")]
    public void CrossEntityInvariantsRejectDuplicateIdsNamesPositionsAndOffsets(string collision)
    {
        var root = JsonNode.Parse(ProjectFileCodec.Serialize(State()))!;
        if (collision == "id") Set(root, "document.pointLoads.0.id", JsonValue.Create(SupportId));
        if (collision == "name") Set(root, "document.pointLoads.0.name", JsonValue.Create("a"));
        if (collision == "position") Set(root, "document.supports.1.position", JsonValue.Create(0));
        if (collision == "offset") Set(root, "presentation.annotationOffsets.1.entityId", JsonValue.Create(SupportId));
        Assert.Throws<ProjectFormatException>(() => Read(root));
    }

    [Theory]
    [InlineData("document.length")]
    [InlineData("document.material.youngsModulus")]
    [InlineData("document.pointLoads.0.value")]
    [InlineData("document.distributedLoads.0.intensity")]
    [InlineData("presentation.annotationOffsets.0.dx")]
    public void OverflowingNumbersAreRejected(string path)
    {
        var root = JsonNode.Parse(ProjectFileCodec.Serialize(State()))!;
        Set(root, path, JsonValue.Create("OVERFLOW"));
        var json = root.ToJsonString().Replace("\"OVERFLOW\"", "1e999");
        Assert.Throws<ProjectFormatException>(() => ProjectFileCodec.Deserialize(Encoding.UTF8.GetBytes(json)));
    }

    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{\"format\":\"SpanDraft.Project\",\"format\":\"Other\"}")]
    public void MalformedRootsAreRejected(string json) => Assert.Throws<ProjectFormatException>(() =>
        ProjectFileCodec.Deserialize(Encoding.UTF8.GetBytes(json)));

    [Fact]
    public void ExtraFieldsAreIgnoredAndUnsupportedSolverStateCanBePersisted()
    {
        var state = State();
        state = state with { Document = state.Document.WithSupports([]), Presentation = new() };
        var root = JsonNode.Parse(ProjectFileCodec.Serialize(state))!;
        root["future"] = "extra"; root["document"]!["material"]!["future"] = 123;
        Assert.True(state.ContentEquals(Read(root)));
    }

    [Theory]
    [InlineData(1, "wallThickness")]
    [InlineData(2, "diameter")]
    [InlineData(3, "outerDiameter")]
    [InlineData(3, "wallThickness")]
    [InlineData(4, "area")]
    [InlineData(4, "axes")]
    public void EverySectionVariantRequiresItsConstructiveData(int section, string field)
    {
        var root = JsonNode.Parse(ProjectFileCodec.Serialize(State(section)))!;
        Remove(root, "document.section." + field);
        Assert.Throws<ProjectFormatException>(() => Read(root));
    }

    [Fact]
    public async Task PhysicalAtomicStoreReplacesCompleteFilesAndCleansTemporaryFilesOnFailure()
    {
        string directory = Path.Combine(Path.GetTempPath(), "SpanDraft-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store = new ProjectFileStore(); var path = Path.Combine(directory, "test.spandraft");
            await store.WriteAtomicAsync(path, ProjectFileCodec.Serialize(State(0)), TestContext.Current.CancellationToken);
            await store.WriteAtomicAsync(path, ProjectFileCodec.Serialize(State(1)), TestContext.Current.CancellationToken);
            Assert.True(State(1).ContentEquals(ProjectFileCodec.Deserialize((await store.ReadAsync(path))!)));
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.WriteAtomicAsync(path, [1, 2, 3], cancelled.Token));
            Assert.True(State(1).ContentEquals(ProjectFileCodec.Deserialize((await store.ReadAsync(path))!)));
            var destinationDirectory = Path.Combine(directory, "blocked"); Directory.CreateDirectory(destinationDirectory);
            var failure = await Record.ExceptionAsync(() =>
                store.WriteAtomicAsync(destinationDirectory, [1, 2, 3], TestContext.Current.CancellationToken));
            Assert.True(failure is IOException or UnauthorizedAccessException,
                $"Expected a filesystem write failure, got {failure?.GetType().Name ?? "no exception"}.");
            Assert.True(State(1).ContentEquals(ProjectFileCodec.Deserialize((await store.ReadAsync(path))!)));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
            await store.DeleteAsync(path); Assert.Null(await store.ReadAsync(path));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static ProjectState Read(JsonNode root) => ProjectFileCodec.Deserialize(Encoding.UTF8.GetBytes(root.ToJsonString()));
    private static (JsonNode Parent, string Key) Parent(JsonNode root, string path)
    {
        var parts = path.Split('.');
        foreach (string part in parts[..^1]) root = root is JsonArray array ? array[int.Parse(part)]! : root[part]!;
        return (root, parts[^1]);
    }
    private static void Remove(JsonNode root, string path)
    {
        var (parent, key) = Parent(root, path);
        ((JsonObject)parent).Remove(key);
    }
    private static void Set(JsonNode root, string path, JsonNode? value)
    {
        var (parent, key) = Parent(root, path);
        if (parent is JsonArray array) array[int.Parse(key)] = value; else parent[key] = value;
    }
}
