using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using SpanDraft.Core.Materials;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Libraries;
using SpanDraft.Desktop.Persistence;
using Xunit;
using static SpanDraft.Tests.LibraryTestSupport;

namespace SpanDraft.Tests;

public sealed class MaterialLibraryCodecTests
{
    public static IEnumerable<object[]> Variants() =>
        from category in Enum.GetValues<MaterialCategory>()
        from density in new[] { false, true }
        from poisson in new[] { false, true }
        select new object[] { category, density, poisson };

    [Theory]
    [MemberData(nameof(Variants))]
    public void RoundtripPreservesAllCategoriesOptionalPropertiesMetadataAndSI(MaterialCategory category,
        bool density, bool poisson)
    {
        var material = new Material("Fixture A", Pressure.FromPascals(123456789.01234567),
            Pressure.FromPascals(2345678.901234567), density ? MassDensity.FromKilogramsPerCubicMeter(1234.5) : null,
            poisson ? PoissonRatio.FromValue(0) : null);
        var expected = new MaterialLibraryEntry(material, category, "1.2345", ["Fixture A standard", "Fixture B standard"]);
        var bytes = MaterialLibraryCodec.Serialize(new([expected]));
        var actual = Assert.Single(MaterialLibraryCodec.Deserialize(bytes).All);
        Assert.Equivalent(expected, actual, strict: true);
        Assert.Equal(bytes, MaterialLibraryCodec.Serialize(new([actual])));
        var root = JsonNode.Parse(bytes)!;
        Assert.Equal("SpanDraft.MaterialLibrary", root["format"]!.GetValue<string>());
        Assert.Equal(1, root["formatVersion"]!.GetValue<int>());
        var entry = root["entries"]![0]!;
        Assert.Equal(new[] { "category", "material", "materialNumber", "otherStandards" }, entry.AsObject().Select(p => p.Key).Order());
        Assert.Equal(category switch
        {
            MaterialCategory.StructuralSteel => "structuralSteel", MaterialCategory.StainlessSteel => "stainlessSteel",
            MaterialCategory.AlloySteel => "alloySteel", MaterialCategory.Aluminium => "aluminium", _ => "other"
        }, entry["category"]!.GetValue<string>());
        Assert.Equal(material.YoungsModulus.Pascals, entry["material"]!["youngsModulus"]!.GetValue<double>());
        Assert.Equal(material.YieldStrength.Pascals, entry["material"]!["yieldStrength"]!.GetValue<double>());
        Assert.Equal(density, entry["material"]!.AsObject().ContainsKey("density"));
        Assert.Equal(poisson, entry["material"]!.AsObject().ContainsKey("poissonRatio"));
        Assert.DoesNotContain("$type", Encoding.UTF8.GetString(bytes));
        Assert.DoesNotContain("SpanDraft.Core", Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void SyntheticFixtureLoadsWithoutDependingOnWriterOrProjectDTOs()
    {
        var actual = Assert.Single(MaterialLibraryCodec.Deserialize(Encoding.UTF8.GetBytes(MaterialFixture)).All);
        Assert.Equal("Fixture A", actual.Material.Name);
        Assert.Equal(123456789.01234567, actual.Material.YoungsModulus.Pascals);
        Assert.Equal(2345678.901234567, actual.Material.YieldStrength.Pascals);
        Assert.Equal(1234.5, actual.Material.Density?.KilogramsPerCubicMeter);
        Assert.Equal(0d, actual.Material.PoissonRatio?.Value);
        Assert.Equal(MaterialCategory.StainlessSteel, actual.Category);
        Assert.Equal("1.2345", actual.MaterialNumber);
        Assert.Equal(new[] { "Fixture standard A", "Fixture standard B" }, actual.OtherStandards);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingOrNullOptionalValuesStayUnknownAndEmptyMetadataIsOmitted(bool explicitNull)
    {
        var root = MaterialJson(); var entry = root["entries"]![0]!;
        foreach (string key in new[] { "density", "poissonRatio" })
            if (explicitNull) entry["material"]![key] = null; else entry["material"]!.AsObject().Remove(key);
        foreach (string key in new[] { "materialNumber", "otherStandards" })
            if (explicitNull) entry[key] = null; else entry.AsObject().Remove(key);
        var actual = MaterialLibraryCodec.Deserialize(Bytes(root)); var material = Assert.Single(actual.All);
        Assert.Null(material.Material.Density); Assert.Null(material.Material.PoissonRatio);
        Assert.Null(material.MaterialNumber); Assert.Empty(material.OtherStandards);
        var wire = JsonNode.Parse(MaterialLibraryCodec.Serialize(actual))!["entries"]![0]!;
        Assert.Equal(new[] { "category", "material" }, wire.AsObject().Select(p => p.Key).Order());
        Assert.Equal(new[] { "name", "yieldStrength", "youngsModulus" }, wire["material"]!.AsObject().Select(p => p.Key).Order());
        entry["materialNumber"] = " "; entry["otherStandards"] = new JsonArray("", " ");
        var empty = Assert.Single(MaterialLibraryCodec.Deserialize(Bytes(root)).All);
        Assert.Null(empty.MaterialNumber); Assert.Empty(empty.OtherStandards);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("en-US")]
    public void WireNumbersAreIndependentOfCulture(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            var bytes = MaterialLibraryCodec.Serialize(new([MaterialEntry()]));
            Assert.Contains("123456789.01234567", Encoding.UTF8.GetString(bytes));
            Assert.Equal(MaterialEntry().Material.YoungsModulus, MaterialLibraryCodec.Deserialize(bytes).All[0].Material.YoungsModulus);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    public static IEnumerable<object[]> InvalidFiles()
    {
        foreach (bool catalog in new[] { false, true })
        {
            foreach (string raw in new[] { "{", "null", "[]", "{}", MaterialFixture.Replace("123456789.01234567", "1e400"),
                MaterialFixture.Replace("\"category\": \"stainlessSteel\"", "\"category\": \"other\", \"category\": \"stainlessSteel\""),
                MaterialFixture.Replace("\"formatVersion\": 1", "\"formatVersion\": 1, \"formatVersion\": 1") })
                yield return [catalog, catalog ? raw.Replace("SpanDraft.MaterialLibrary", "SpanDraft.MaterialCatalog") : raw];
            foreach (string failure in new[] { "version", "format", "missingVersion", "missingEntries", "nullEntry", "duplicateName",
                "category", "numericCategory", "missingCategory", "nullMaterial", "missingE", "zeroE", "negativeFy",
                "zeroDensity", "invalidPoisson", "blankName", "untrimmedName", "numericNumber", "nullStandard", "standardsString" })
            {
                var root = MaterialJson(catalog); var entry = root["entries"]![0]!; var m = entry["material"]!;
                switch (failure)
                {
                    case "version": root["formatVersion"] = 2; break;
                    case "format": root["format"] = "SpanDraft.Project"; break;
                    case "missingVersion": root.AsObject().Remove("formatVersion"); break;
                    case "missingEntries": root.AsObject().Remove("entries"); break;
                    case "nullEntry": root["entries"]![0] = null; break;
                    case "duplicateName":
                        var copy = entry.DeepClone(); copy["material"]!["name"] = "fixture a";
                        root["entries"]!.AsArray().Add(copy); break;
                    case "category": entry["category"] = "future"; break;
                    case "numericCategory": entry["category"] = 0; break;
                    case "missingCategory": entry.AsObject().Remove("category"); break;
                    case "nullMaterial": entry["material"] = null; break;
                    case "missingE": m.AsObject().Remove("youngsModulus"); break;
                    case "zeroE": m["youngsModulus"] = 0; break;
                    case "negativeFy": m["yieldStrength"] = -1; break;
                    case "zeroDensity": m["density"] = 0; break;
                    case "invalidPoisson": m["poissonRatio"] = .5; break;
                    case "blankName": m["name"] = " "; break;
                    case "untrimmedName": m["name"] = " Fixture A "; break;
                    case "numericNumber": entry["materialNumber"] = 1.2345; break;
                    case "nullStandard": entry["otherStandards"] = new JsonArray((JsonNode?)null); break;
                    case "standardsString": entry["otherStandards"] = "Fixture standard"; break;
                }
                yield return [catalog, root.ToJsonString()];
            }
        }
    }

    [Theory]
    [MemberData(nameof(InvalidFiles))]
    public void LibraryAndCatalogRejectInvalidDataAsAWhole(bool catalog, string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        Assert.Throws<LibraryFormatException>(() =>
        {
            if (catalog) MaterialCatalogCodec.Deserialize(bytes); else MaterialLibraryCodec.Deserialize(bytes);
        });
    }

    [Fact]
    public void CatalogLoadsEmptyResourceAndSyntheticEntriesAndRejectsLibraryEnvelope()
    {
        Assert.Empty(MaterialCatalogCodec.LoadBuiltIn().All);
        var root = MaterialJson(true);
        var catalog = MaterialCatalogCodec.Deserialize(Bytes(root));
        Assert.Equivalent(MaterialLibraryCodec.Deserialize(Encoding.UTF8.GetBytes(MaterialFixture)).All[0], catalog.All[0], strict: true);
        Assert.Throws<LibraryFormatException>(() => MaterialCatalogCodec.Deserialize(Encoding.UTF8.GetBytes(MaterialFixture)));
        Assert.Throws<LibraryFormatException>(() => MaterialLibraryCodec.Deserialize(Bytes(root)));
        root["entries"] = new JsonArray(); Assert.Empty(MaterialCatalogCodec.Deserialize(Bytes(root)).All);
    }

    [Fact]
    public void AdditionalFieldsAreIgnoredWithoutBecomingLibraryOrProjectData()
    {
        var root = MaterialJson();
        root["future"] = 42; root["entries"]![0]!["future"] = "extra";
        root["entries"]![0]!["material"]!["future"] = 123;
        Assert.Equivalent(MaterialLibraryCodec.Deserialize(Encoding.UTF8.GetBytes(MaterialFixture)).All,
            MaterialLibraryCodec.Deserialize(Bytes(root)).All, strict: true);
    }
}
