using System.Text;
using System.Text.Json.Nodes;
using SpanDraft.Analysis;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Sections.Parametric;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Libraries;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.State;
using Xunit;
using static SpanDraft.Tests.LibraryTestSupport;
using static SpanDraft.Tests.ProjectTestSupport;

namespace SpanDraft.Tests;

public sealed class LibrarySnapshotTests
{
    [Fact]
    public async Task MaterialSnapshotSurvivesLibraryReplaceRenameDeleteAndPersistence()
    {
        var entry = MaterialLibraryCodec.Deserialize(Encoding.UTF8.GetBytes(MaterialFixture)).All[0];
        var library = new UserMaterialLibrary([entry]);
        var state = CalculableState();
        state = state with { Document = state.Document with { Material = library.Find("Fixture A")!.Material } };
        byte[] snapshot = ProjectFileCodec.Serialize(state);
        var result = Analyze(state);
        library.Replace("Fixture A", MaterialEntry("Fixture A", 987654321));
        Assert.Equal(snapshot, ProjectFileCodec.Serialize(state));
        library.Replace("Fixture A", MaterialEntry("Renamed", 345678901));
        Assert.Equal(snapshot, ProjectFileCodec.Serialize(state));
        Assert.True(library.Remove("Renamed")); Assert.Empty(library.All);
        var files = new Files(); await new MaterialLibraryStore(files, "materials").SaveAsync(library, TestContext.Current.CancellationToken);
        AssertIndependentWire(snapshot);
        await VerifyProject(files, state, snapshot, result);
        Assert.NotNull(ProjectFileCodec.Deserialize(snapshot).Document.Material.Density);
        Assert.NotNull(ProjectFileCodec.Deserialize(snapshot).Document.Material.PoissonRatio);
    }

    public static IEnumerable<object[]> Definitions() => Enumerable.Range(0, 11).Select(kind => new object[] { kind });

    [Theory]
    [MemberData(nameof(Definitions))]
    public async Task SectionSnapshotSurvivesLibraryReplaceRenameDeleteAndPersistence(int kind)
    {
        var section = Definition(kind);
        var library = new UserSectionLibrary([new("Fixture section", section)]);
        var state = CalculableState();
        state = state with { Document = state.Document.WithSection(library.Find("FIXTURE SECTION")!.Section,
            section.Axes[0].AxisDesignation) };
        byte[] snapshot = ProjectFileCodec.Serialize(state); var result = Analyze(state);
        library.Replace("Fixture section", new("Fixture section", ParametricSectionTestSupport.Shape(2)));
        Assert.Equal(snapshot, ProjectFileCodec.Serialize(state));
        library.Replace("Fixture section", new("Renamed", ParametricSectionTestSupport.Shape(3)));
        Assert.Equal(snapshot, ProjectFileCodec.Serialize(state));
        Assert.True(library.Remove("Renamed")); Assert.Empty(library.All);
        var files = new Files(); await new SectionLibraryStore(files, "sections").SaveAsync(library, TestContext.Current.CancellationToken);
        AssertIndependentWire(snapshot);
        await VerifyProject(files, state, snapshot, result);
    }

    [Fact]
    public void EqualMaterialNamesDoNotIdentifyValuesAcrossProjects()
    {
        var state = CalculableState();
        var first = state with { Document = state.Document with { Material = MaterialEntry("Same name", 123456789).Material } };
        var second = state with { Document = state.Document with { Material = MaterialEntry("Same name", 987654321).Material } };
        var loadedFirst = ProjectFileCodec.Deserialize(ProjectFileCodec.Serialize(first));
        var loadedSecond = ProjectFileCodec.Deserialize(ProjectFileCodec.Serialize(second));
        Assert.Equal(loadedFirst.Document.Material.Name, loadedSecond.Document.Material.Name);
        Assert.NotEqual(loadedFirst.Document.Material.YoungsModulus, loadedSecond.Document.Material.YoungsModulus);
        Assert.False(loadedFirst.ContentEquals(loadedSecond));
        Assert.True(BeamAnalysis.Analyze(loadedFirst.Document.ToBeamModel()).IsSuccess);
        Assert.True(BeamAnalysis.Analyze(loadedSecond.Document.ToBeamModel()).IsSuccess);
    }

    private static ProjectState CalculableState()
    {
        var state = State();
        // The persistence fixture deliberately includes a one-ULP overhang at x = 1.
        // Use an exactly supported beam end and the current parametric definition for analysis.
        var document = state.Document.WithSection(new RectangleSectionGeometry(M(.1), M(.2)), SectionAxisDesignation.Y);
        return state with { Document = document with { Length = M(1) } };
    }

    private static ISectionDefinition Definition(int kind)
    {
        if (kind < 8) return ParametricSectionTestSupport.Shape(kind, r: kind is 1 or >= 4 ? .5 : 0);
        return new ManualSectionDefinition(Area.FromSquareMeters(.01),
            new(kind == 10 ? SectionAxisDesignation.V : SectionAxisDesignation.Z,
                SecondMomentOfArea.FromMetersToTheFourth(1e-5), SectionModulus.FromCubicMeters(2e-4)),
            kind == 8 ? null : new(kind == 10 ? SectionAxisDesignation.U : SectionAxisDesignation.Y,
                SecondMomentOfArea.FromMetersToTheFourth(3e-5), SectionModulus.FromCubicMeters(4e-4)));
    }

    private static BeamAnalysisResult Analyze(ProjectState state)
    {
        var outcome = BeamAnalysis.Analyze(state.Document.ToBeamModel());
        Assert.True(outcome.IsSuccess, outcome.Failure?.TechnicalMessage); return outcome.Result!;
    }

    private static async Task VerifyProject(Files files, ProjectState expected, byte[] snapshot, BeamAnalysisResult result)
    {
        await files.WriteAtomicAsync("project.spandraft", ProjectFileCodec.Serialize(expected), TestContext.Current.CancellationToken);
        var loaded = ProjectFileCodec.Deserialize((await files.ReadAsync("project.spandraft"))!);
        Assert.True(expected.ContentEquals(loaded)); Assert.Equal(snapshot, ProjectFileCodec.Serialize(loaded));
        var actual = Analyze(loaded);
        SolverTestSupport.SameResults(result.Solution, actual.Solution);
        Assert.Equivalent(result.Engineering, actual.Engineering, strict: true);
    }

    private static void AssertIndependentWire(byte[] bytes)
    {
        var root = JsonNode.Parse(bytes)!;
        Assert.Equal(new[] { "format", "formatVersion", "document", "presentation" }.Order(), root.AsObject().Select(p => p.Key).Order());
        Assert.Equal(new[] { "name", "youngsModulus", "yieldStrength", "density", "poissonRatio" }.Where(key =>
            root["document"]!["material"]!.AsObject().ContainsKey(key)).Order(), root["document"]!["material"]!.AsObject().Select(p => p.Key).Order());
        string json = Encoding.UTF8.GetString(bytes);
        foreach (var forbidden in new[] { "library", "preset", "source", "provenance", "origin", "category", "materialNumber", "otherStandards" })
            Assert.DoesNotContain(forbidden, json, StringComparison.OrdinalIgnoreCase);
    }
}
