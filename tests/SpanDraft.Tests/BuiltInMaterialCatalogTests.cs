using System.Text.Json;
using SpanDraft.Desktop.Libraries;
using SpanDraft.Desktop.Persistence;
using Xunit;

namespace SpanDraft.Tests;

public sealed class BuiltInMaterialCatalogTests
{
    public static IEnumerable<object[]> StartMaterials()
    {
        yield return [MaterialCategory.StructuralSteel, "S235JR", "1.0038", null!, 210000000000d, 235000000d, 7850d, .30];
        yield return [MaterialCategory.StructuralSteel, "S355JR", "1.0045", null!, 210000000000d, 355000000d, 7850d, .30];
        yield return [MaterialCategory.StainlessSteel, "X5CrNi18-10", "1.4301", "AISI 304", 200000000000d, 190000000d, 7900d, .30];
        yield return [MaterialCategory.StainlessSteel, "X2CrNiMo17-12-2", "1.4404", "AISI 316L", 200000000000d, 200000000d, 8000d, .30];
        yield return [MaterialCategory.AlloySteel, "42CrMo4 +QT", "1.7225", null!, 210000000000d, 650000000d, 7800d, .30];
        yield return [MaterialCategory.AlloySteel, "34CrNiMo6 +QT", "1.6582", null!, 210000000000d, 800000000d, 7800d, .30];
        yield return [MaterialCategory.Aluminium, "AlMgSi0,5 T66", "3.3206", "EN AW-6060 T66", 69000000000d, 150000000d, 2700d, .33];
        yield return [MaterialCategory.Aluminium, "AlMg3 H22", "3.3535", "EN AW-5754 H22", 70000000000d, 130000000d, 2670d, .33];
    }

    [Theory]
    [MemberData(nameof(StartMaterials))]
    public void EmbeddedMaterialMatchesTheSpecifiedReferenceValues(MaterialCategory category, string name,
        string materialNumber, string? otherStandard, double youngsModulus, double yieldStrength,
        double density, double poissonRatio)
    {
        var entry = Assert.Single(MaterialCatalogCodec.LoadBuiltIn().All, entry => entry.Material.Name == name);
        Assert.Equal(category, entry.Category);
        Assert.Equal(name, entry.Material.Name);
        Assert.Equal(materialNumber, entry.MaterialNumber);
        Assert.Equal(otherStandard is null ? Array.Empty<string>() : [otherStandard], entry.OtherStandards);
        Assert.Equal(youngsModulus, entry.Material.YoungsModulus.Pascals);
        Assert.Equal(yieldStrength, entry.Material.YieldStrength.Pascals);
        Assert.Equal(density, entry.Material.Density?.KilogramsPerCubicMeter);
        Assert.Equal(poissonRatio, entry.Material.PoissonRatio?.Value);
    }

    [Fact]
    public void EmbeddedCatalogContainsExactlyTheEightStartMaterials()
    {
        var catalog = MaterialCatalogCodec.LoadBuiltIn();
        Assert.Equal(8, catalog.All.Count);
        Assert.Equal(StartMaterials().Select(row => (string)row[1]).Order(),
            catalog.All.Select(entry => entry.Material.Name).Order());
    }

    [Fact]
    public void EmbeddedResourceUsesOnlyV1FieldsAndOmitsAbsentOtherStandards()
    {
        using var resource = typeof(MaterialCatalogCodec).Assembly.GetManifestResourceStream(
            "SpanDraft.Desktop.Libraries.material-catalog.json");
        Assert.NotNull(resource);
        using var document = JsonDocument.Parse(resource);
        var root = document.RootElement;
        Assert.Equal(new[] { "entries", "format", "formatVersion" }, root.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal("SpanDraft.MaterialCatalog", root.GetProperty("format").GetString());
        Assert.Equal(1, root.GetProperty("formatVersion").GetInt32());
        foreach (var entry in root.GetProperty("entries").EnumerateArray())
        {
            var material = entry.GetProperty("material");
            var expected = Assert.Single(StartMaterials(), row => (string)row[1] == material.GetProperty("name").GetString());
            bool hasStandards = expected[3] is not null;
            Assert.Equal(hasStandards ? new[] { "category", "material", "materialNumber", "otherStandards" }
                : ["category", "material", "materialNumber"], entry.EnumerateObject().Select(p => p.Name).Order());
            Assert.Equal(new[] { "density", "name", "poissonRatio", "yieldStrength", "youngsModulus" },
                material.EnumerateObject().Select(p => p.Name).Order());
            Assert.Equal((string)expected[2], entry.GetProperty("materialNumber").GetString());
            if (hasStandards)
                Assert.Equal((string)expected[3], Assert.Single(entry.GetProperty("otherStandards").EnumerateArray()).GetString());
            Assert.Equal((double)expected[4], material.GetProperty("youngsModulus").GetDouble());
            Assert.Equal((double)expected[5], material.GetProperty("yieldStrength").GetDouble());
            Assert.Equal((double)expected[6], material.GetProperty("density").GetDouble());
            Assert.Equal((double)expected[7], material.GetProperty("poissonRatio").GetDouble());
        }
    }
}
