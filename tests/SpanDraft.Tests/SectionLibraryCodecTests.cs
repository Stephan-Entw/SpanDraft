using System.Text;
using System.Text.Json.Nodes;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Sections.Parametric;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Libraries;
using SpanDraft.Desktop.Persistence;
using Xunit;
using static SpanDraft.Tests.LibraryTestSupport;

namespace SpanDraft.Tests;

public sealed class SectionLibraryCodecTests
{
    private static readonly string[] Types = ["rectangle", "rectangularHollow", "circle", "circularHollow", "iSection", "uSection", "tSection", "angle"];
    private static readonly string[][] Fields = [
        ["width", "height"], ["width", "height", "wallThickness", "outerRadius"], ["diameter"],
        ["outerDiameter", "wallThickness"], ["height", "width", "webThickness", "flangeThickness", "radius"],
        ["height", "width", "webThickness", "flangeThickness", "radius"],
        ["height", "width", "webThickness", "flangeThickness", "radius"], ["width", "height", "thickness", "innerRadius"]];

    private static IParametricSectionDefinition Shape(int kind, double r) => ParametricSectionTestSupport.Shape(kind,
        b: .125, h: .25, t: .015625, tf: .03125, r: r);

    private static JsonNode Json(int kind = 4) => JsonNode.Parse(SectionLibraryCodec.Serialize(
        new([new("Fixture section", Shape(kind, 0))])))!;

    public static IEnumerable<object[]> Shapes()
    {
        for (int kind = 0; kind < 8; kind++)
        {
            yield return [kind, 0d];
            if (kind is 1 or >= 4) yield return [kind, .03125];
        }
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void EveryShapePreservesOriginalParametersExactlyAndStoresNoDerivedProperties(int kind, double radius)
    {
        var section = Shape(kind, radius);
        var library = new UserSectionLibrary([new("Fixture section", section)]);
        var bytes = SectionLibraryCodec.Serialize(library); var root = JsonNode.Parse(bytes)!;
        Assert.Equal("SpanDraft.SectionLibrary", root["format"]!.GetValue<string>());
        Assert.Equal(1, root["formatVersion"]!.GetValue<int>());
        Assert.Equal(new[] { "name", "section" }, root["entries"]![0]!.AsObject().Select(p => p.Key).Order());
        var wire = root["entries"]![0]!["section"]!;
        Assert.Equal(Types[kind], wire["type"]!.GetValue<string>());
        Assert.Equal(Fields[kind].Append("type").Order(), wire.AsObject().Select(p => p.Key).Order());
        var loaded = Assert.Single(SectionLibraryCodec.Deserialize(bytes).All);
        var actual = Assert.IsAssignableFrom<IParametricSectionDefinition>(loaded.Section);
        Assert.Equal("Fixture section", loaded.Name); Assert.Equal(section.GetType(), actual.GetType());
        foreach (var property in section.GetType().GetProperties().Where(p => p.PropertyType == typeof(Length)))
        {
            double expectedValue = ((Length)property.GetValue(section)!).Meters;
            double actualValue = ((Length)property.GetValue(actual)!).Meters;
            string field = char.ToLowerInvariant(property.Name[0]) + property.Name[1..];
            Assert.Equal(BitConverter.DoubleToInt64Bits(expectedValue), BitConverter.DoubleToInt64Bits(actualValue));
            if (Fields[kind].Contains(field)) Assert.Equal(expectedValue, wire[field]!.GetValue<double>());
        }
        Assert.Equivalent(section.GeometryProperties, actual.GeometryProperties, strict: true);
        Assert.Equal(section.Axes, actual.Axes);
        Assert.Equal(bytes, SectionLibraryCodec.Serialize(new([loaded])));
        Assert.DoesNotContain("bendingAxis", Encoding.UTF8.GetString(bytes));
        Assert.DoesNotContain("$type", Encoding.UTF8.GetString(bytes));
        Assert.DoesNotContain("SpanDraft.Core", Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void NonBinaryInputParametersSurviveWithoutRounding()
    {
        var section = new RectangleSectionGeometry(Length.FromMeters(.12345678901234567),
            Length.FromMeters(.23456789012345678));
        var actual = Assert.IsType<RectangleSectionGeometry>(SectionLibraryCodec.Deserialize(
            SectionLibraryCodec.Serialize(new([new("Precision fixture", section)]))).All[0].Section);
        Assert.Equal(BitConverter.DoubleToInt64Bits(section.Width.Meters), BitConverter.DoubleToInt64Bits(actual.Width.Meters));
        Assert.Equal(BitConverter.DoubleToInt64Bits(section.Height.Meters), BitConverter.DoubleToInt64Bits(actual.Height.Meters));
    }

    [Theory]
    [InlineData(SectionAxisDesignation.Y, false)]
    [InlineData(SectionAxisDesignation.Z, false)]
    [InlineData(SectionAxisDesignation.U, false)]
    [InlineData(SectionAxisDesignation.V, false)]
    [InlineData(SectionAxisDesignation.Y, true)]
    [InlineData(SectionAxisDesignation.Z, true)]
    [InlineData(SectionAxisDesignation.U, true)]
    [InlineData(SectionAxisDesignation.V, true)]
    public void ManualRoundtripsOneAxisOrCanonicalPairsWithoutInventingGeometry(SectionAxisDesignation designation, bool pair)
    {
        var partner = designation switch { SectionAxisDesignation.Y => SectionAxisDesignation.Z,
            SectionAxisDesignation.Z => SectionAxisDesignation.Y, SectionAxisDesignation.U => SectionAxisDesignation.V,
            _ => SectionAxisDesignation.U };
        var section = new ManualSectionDefinition(Area.FromSquareMeters(.0012345678901234567),
            new(designation, SecondMomentOfArea.FromMetersToTheFourth(1.2345678901234567e-7), SectionModulus.FromCubicMeters(2.3456789012345678e-5)),
            pair ? new(partner, SecondMomentOfArea.FromMetersToTheFourth(3e-7), SectionModulus.FromCubicMeters(4e-5)) : null);
        var bytes = SectionLibraryCodec.Serialize(new([new("Manual fixture", section)]));
        var actual = Assert.IsType<ManualSectionDefinition>(SectionLibraryCodec.Deserialize(bytes).All[0].Section);
        Assert.Equal(section.Area, actual.Area); Assert.Equal(section.Axes, actual.Axes);
        var wire = JsonNode.Parse(bytes)!["entries"]![0]!["section"]!;
        Assert.Equal(new[] { "area", "axes", "type" }, wire.AsObject().Select(p => p.Key).Order());
        Assert.Equal(section.Axes.Select(a => a.AxisDesignation.ToString().ToLowerInvariant()),
            wire["axes"]!.AsArray().Select(a => a!["designation"]!.GetValue<string>()));
        foreach (var a in wire["axes"]!.AsArray())
            Assert.Equal(new[] { "designation", "secondMomentOfArea", "sectionModulus" }, a!.AsObject().Select(p => p.Key).Order());
    }

    public static IEnumerable<object[]> RequiredParameters() =>
        from kind in Enumerable.Range(0, 8)
        from field in Fields[kind]
        from nullValue in new[] { false, true }
        select new object[] { kind, field, nullValue };

    [Theory]
    [MemberData(nameof(RequiredParameters))]
    public void EveryOriginalParameterIsRequiredIncludingZeroRadius(int kind, string field, bool nullValue)
    {
        var root = Json(kind); var wire = root["entries"]![0]!["section"]!.AsObject();
        if (nullValue) wire[field] = null; else wire.Remove(field);
        Assert.Throws<LibraryFormatException>(() => SectionLibraryCodec.Deserialize(Bytes(root)));
    }

    public static IEnumerable<object[]> InvalidGeometry()
    {
        for (int kind = 0; kind < 8; kind++)
            foreach (var field in Fields[kind])
            {
                yield return [kind, field, -1d];
                yield return [kind, field, 1e300];
                if (!field.Contains("radius", StringComparison.OrdinalIgnoreCase)) yield return [kind, field, 0d];
            }
        yield return [1, "outerRadius", 1d];
        yield return [4, "radius", 1d];
        yield return [5, "radius", 1d];
        yield return [6, "radius", 1d];
        yield return [7, "innerRadius", 1d];
    }

    [Theory]
    [MemberData(nameof(InvalidGeometry))]
    public void InvalidDomainGeometryIsRejectedWithoutRepair(int kind, string field, double value)
    {
        var root = Json(kind); root["entries"]![0]!["section"]![field] = value;
        Assert.Throws<LibraryFormatException>(() => SectionLibraryCodec.Deserialize(Bytes(root)));
    }

    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{\"format\":\"SpanDraft.SectionLibrary\",\"formatVersion\":2,\"entries\":[]}")]
    [InlineData("{\"format\":\"SpanDraft.SectionLibrary\",\"formatVersion\":1,\"entries\":null}")]
    [InlineData("{\"format\":\"SpanDraft.Project\",\"formatVersion\":1,\"entries\":[]}")]
    [InlineData("{\"format\":\"SpanDraft.SectionLibrary\",\"formatVersion\":1}")]
    public void InvalidEnvelopeAndMalformedJsonAreRejected(string json) =>
        Assert.Throws<LibraryFormatException>(() => SectionLibraryCodec.Deserialize(Encoding.UTF8.GetBytes(json)));

    [Theory]
    [InlineData("future")]
    [InlineData("Rectangle")]
    [InlineData("0")]
    public void UnknownShapesAreRejected(string shape)
    {
        var root = Json(); root["entries"]![0]!["section"]!["type"] = shape;
        Assert.Throws<LibraryFormatException>(() => SectionLibraryCodec.Deserialize(Bytes(root)));
    }

    [Fact]
    public void NullEntriesDuplicateNamesAndDuplicateJsonFieldsAreRejected()
    {
        var root = Json(); var duplicate = root["entries"]![0]!.DeepClone(); duplicate["name"] = "FIXTURE SECTION";
        root["entries"]!.AsArray().Add(duplicate);
        Assert.Throws<LibraryFormatException>(() => SectionLibraryCodec.Deserialize(Bytes(root)));
        root = Json(); root["entries"]![0] = null;
        Assert.Throws<LibraryFormatException>(() => SectionLibraryCodec.Deserialize(Bytes(root)));
        var json = Json().ToJsonString().Replace("\"radius\":0", "\"radius\":0,\"radius\":0");
        Assert.Throws<LibraryFormatException>(() => SectionLibraryCodec.Deserialize(Encoding.UTF8.GetBytes(json)));
    }

    [Theory]
    [InlineData("none")]
    [InlineData("three")]
    [InlineData("duplicate")]
    [InlineData("mixed")]
    [InlineData("unknown")]
    [InlineData("zeroI")]
    [InlineData("negativeW")]
    [InlineData("missingW")]
    [InlineData("nullAxis")]
    public void InvalidManualDefinitionsAreRejected(string failure)
    {
        var root = JsonNode.Parse("""
            { "format": "SpanDraft.SectionLibrary", "formatVersion": 1, "entries": [
              { "name": "Manual fixture", "section": { "type": "manual", "area": 0.01,
                "axes": [{ "designation": "y", "secondMomentOfArea": 0.001, "sectionModulus": 0.002 }] } } ] }
            """)!;
        var axes = root["entries"]![0]!["section"]!["axes"]!.AsArray(); var axis = axes[0]!;
        switch (failure)
        {
            case "none": axes.Clear(); break;
            case "three": axes.Add(axis.DeepClone()); axes.Add(axis.DeepClone()); break;
            case "duplicate": axes.Add(axis.DeepClone()); break;
            case "mixed": var other = axis.DeepClone(); other["designation"] = "u"; axes.Add(other); break;
            case "unknown": axis["designation"] = "future"; break;
            case "zeroI": axis["secondMomentOfArea"] = 0; break;
            case "negativeW": axis["sectionModulus"] = -1; break;
            case "missingW": axis.AsObject().Remove("sectionModulus"); break;
            case "nullAxis": axes[0] = null; break;
        }
        Assert.Throws<LibraryFormatException>(() => SectionLibraryCodec.Deserialize(Bytes(root)));
    }

    [Fact]
    public void EmptyLibraryAndAdditionalFieldsAreSupported()
    {
        Assert.Empty(SectionLibraryCodec.Deserialize(SectionLibraryCodec.Serialize(new())).All);
        var root = Json(); root["future"] = true; root["entries"]![0]!["section"]!["future"] = 42;
        Assert.Equivalent(SectionLibraryCodec.Deserialize(Bytes(Json())).All,
            SectionLibraryCodec.Deserialize(Bytes(root)).All, strict: true);
    }
}
