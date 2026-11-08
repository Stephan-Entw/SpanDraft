using System.Text;
using System.Text.Json.Nodes;
using SpanDraft.Core.Materials;
using SpanDraft.Desktop.Libraries;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.State;
using SpanDraft.Engineering;
using Xunit;
using static SpanDraft.Tests.ProjectTestSupport;

namespace SpanDraft.Tests;

public sealed class MaterialStrengthTests : MaterialStrengthTestSupport
{

    [Fact]
    public void DomainExposesExactlyTwoTypesAndRequiresAnExplicitValidType()
    {
        Assert.Equal(new[] { MaterialStrengthType.Re, MaterialStrengthType.Rp02 }, Enum.GetValues<MaterialStrengthType>());
        Assert.All(typeof(Material).GetConstructors(), c =>
            Assert.Contains(c.GetParameters(), p => p.ParameterType == typeof(MaterialStrengthType) && !p.IsOptional));
        Assert.Throws<ArgumentOutOfRangeException>(() => Typed((MaterialStrengthType)99));
    }

    [Theory]
    [InlineData(MaterialStrengthType.Re, "re")]
    [InlineData(MaterialStrengthType.Rp02, "rp02")]
    public void BothTypesRoundtripWithoutChangingMechanics(MaterialStrengthType type, string wire)
    {
        var material = Typed(type);
        var state = WithMaterial(material);
        var bytes = ProjectFileCodec.Serialize(state);
        Assert.Equal(wire, JsonNode.Parse(bytes)!["document"]!["material"]!["strengthType"]!.GetValue<string>());
        var loaded = ProjectFileCodec.Deserialize(bytes);
        Assert.True(state.ContentEquals(loaded));
        Assert.Equal(type, loaded.Document.Material.StrengthType);
        var baseline = SolverTestSupport.Solve(WithMaterial(Typed(MaterialStrengthType.Re)).Document.ToBeamModel());
        var actual = SolverTestSupport.Solve(loaded.Document.ToBeamModel());
        SolverTestSupport.SameResults(baseline, actual);
        Assert.Equal(BeamEngineeringAnalysis.Analyze(baseline).SafetyFactor, BeamEngineeringAnalysis.Analyze(actual).SafetyFactor);

        foreach (var category in Enum.GetValues<MaterialCategory>())
        {
            var library = new UserMaterialLibrary([LibraryTestSupport.WithMetadata(material, category)]);
            var saved = MaterialLibraryCodec.Serialize(library);
            var entry = Assert.Single(MaterialLibraryCodec.Deserialize(saved).All);
            Assert.Equal(category, entry.Category);
            Assert.Equal(type, entry.StrengthType);
            Assert.Equal(material.YieldStrength, entry.YieldStrength);
            Assert.Equal(saved, MaterialLibraryCodec.Serialize(new([entry])));
        }
    }

    [Theory]
    [InlineData(2)] [InlineData(3)]
    public void HistoricalDataDoesNotInferTypeAndRegularWriterRejectsIt(int version)
    {
        var state = ProjectFileCodec.Deserialize(LegacyProject(version));
        Assert.Null(state.Document.Material.StrengthType);
        Assert.Throws<ProjectFormatException>(() => ProjectFileCodec.Serialize(state));
        Assert.Throws<ProjectFormatException>(() => ProjectFileCodec.ToDto(state));
        Assert.Contains(Strings.MaterialStrengthUnspecified, MaterialStrengthDisplay.Summary(state.Document.Material));
        var knownName = JsonNode.Parse(LegacyProject(version))!;
        knownName["document"]!["material"]!["name"] = "S235JR";
        Assert.Null(ProjectFileCodec.Deserialize(Encoding.UTF8.GetBytes(knownName.ToJsonString())).Document.Material.StrengthType);
    }

    [Theory]
    [InlineData("fy")] [InlineData("Re")] [InlineData("ReH")] [InlineData("rp01")] [InlineData("")]
    public void UnknownWireTypesAreRejectedByProjectLibraryAndCatalog(string type)
    {
        var project = JsonNode.Parse(LegacyProject())!;
        project["document"]!["material"]!["strengthType"] = type;
        Assert.Throws<ProjectFormatException>(() => ProjectFileCodec.Deserialize(Encoding.UTF8.GetBytes(project.ToJsonString())));
        var library = LibraryTestSupport.MaterialJson();
        library["entries"]![0]!["material"]!["strengthType"] = type;
        Assert.Throws<LibraryFormatException>(() => MaterialLibraryCodec.Deserialize(LibraryTestSupport.Bytes(library)));
        library["format"] = MaterialCatalogCodec.Format;
        Assert.Throws<LibraryFormatException>(() => MaterialCatalogCodec.Deserialize(LibraryTestSupport.Bytes(library)));
    }

    [Fact]
    public async Task MixedLibraryPreservesOnlyUntouchedLegacyEntriesAndPublishesAtomically()
    {
        var files = new Files();
        files.Data["materials"] = Encoding.UTF8.GetBytes(LibraryTestSupport.MaterialFixture);
        var store = new MaterialLibraryStore(files, "materials");
        var library = await store.LoadAsync();
        var legacy = Assert.Single(library.All);
        Assert.Null(legacy.StrengthType);
        Assert.Throws<ArgumentException>(() => library.Add(LibraryTestSupport.WithMetadata(legacy, MaterialCategory.Other)));
        Assert.Throws<ArgumentException>(() => library.Replace(legacy.Name, legacy));
        library.Add(LibraryTestSupport.WithMetadata(Typed(MaterialStrengthType.Rp02), MaterialCategory.StructuralSteel));
        await store.SaveAsync(library, TestContext.Current.CancellationToken);
        var loaded = await store.LoadAsync();
        Assert.Null(loaded.All[0].StrengthType);
        Assert.Equal(MaterialStrengthType.Rp02, loaded.All[1].StrengthType);
        var preserved = files.Data["materials"].ToArray();
        var changed = new UserMaterialLibrary([LibraryTestSupport.WithMetadata(legacy, MaterialCategory.Aluminium)]);
        await Assert.ThrowsAsync<LibraryFormatException>(() => store.SaveAsync(changed, TestContext.Current.CancellationToken));
        Assert.Equal(preserved, files.Data["materials"]);
        await Assert.ThrowsAsync<LibraryFormatException>(() => new MaterialLibraryStore(files, "new-library").SaveAsync(new([legacy]), TestContext.Current.CancellationToken));
        Assert.False(files.Data.ContainsKey("new-library"));
        var m = legacy;
        loaded.Replace(m.Name, new Material(m.Name, m.YoungsModulus, m.YieldStrength, MaterialStrengthType.Re, m.Density, m.PoissonRatio, legacy.Category, legacy.MaterialNumber, legacy.OtherStandards));
        files.FailWritePath = "materials";
        await Assert.ThrowsAsync<IOException>(() => store.SaveAsync(loaded, TestContext.Current.CancellationToken));
        Assert.Equal(preserved, files.Data["materials"]);
        files.FailWritePath = null;
        await store.SaveAsync(loaded, TestContext.Current.CancellationToken);
        Assert.All((await store.LoadAsync()).All, e => Assert.NotNull(e.StrengthType));
    }

#pragma warning restore xUnit1031

    [Theory]
    [InlineData(MaterialStrengthType.Re)] [InlineData(MaterialStrengthType.Rp02)]
    public async Task ProjectAndSetupRecoveryPreserveBothTypesAndSetupAlsoPreservesLegacy(MaterialStrengthType type)
    {
        var files = new Files();
        foreach (var state in new[] { WithMaterial(Typed(type)), ProjectFileCodec.Deserialize(LegacyProject()) })
        {
            var recovery = new ProjectRecovery(files, "project-recovery");
            await recovery.FlushAsync(state, null);
            Assert.True(state.ContentEquals((await recovery.ReadAsync())!.State));
            var draft = new ProjectSetupDraft(Material: state.Document.Material);
            files.Data["setup-recovery"] = ProjectSetupRecovery.Encode(new(draft, Now));
            var setup = await new ProjectSetupRecovery(files, "setup-recovery").ReadAsync();
            Assert.True(draft.ContentEquals(setup!.Draft));
        }
    }
}
