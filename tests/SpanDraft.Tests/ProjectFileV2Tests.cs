using System.Text;
using System.Text.Json.Nodes;
using SpanDraft.Core.Materials;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Sections.Parametric;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.State;
using Xunit;
using static SpanDraft.Tests.ProjectTestSupport;

namespace SpanDraft.Tests;

public sealed class ProjectFileV2Tests
{
    private static readonly string[] WireTypes = ["rectangle", "rectangularHollow", "circle", "circularHollow",
        "iSection", "uSection", "tSection", "angle"];

    public static IEnumerable<object[]> Shapes()
    {
        for (int kind = 0; kind < 8; kind++)
        {
            var radii = kind switch { 1 => new[] { 0d, .5, 3 }, 4 or 6 => [0d, .5, 2.5],
                5 => [0d, .5, 4], 7 => [0d, .5, 5], _ => [0d] };
            foreach (var r in radii)
                foreach (var axis in kind == 7 ? new[] { SectionAxisDesignation.U, SectionAxisDesignation.V }
                    : [SectionAxisDesignation.Y, SectionAxisDesignation.Z])
                    yield return [kind, r, axis];
        }
    }

    private static Dictionary<string, double> Parameters(int kind, double radius) => kind switch
    {
        0 => new() { ["width"] = 6, ["height"] = 10 },
        1 => new() { ["width"] = 6, ["height"] = 10, ["wallThickness"] = 1, ["outerRadius"] = radius },
        2 => new() { ["diameter"] = 6 },
        3 => new() { ["outerDiameter"] = 6, ["wallThickness"] = 1 },
        4 or 5 or 6 => new() { ["height"] = 10, ["width"] = 6, ["webThickness"] = 1,
            ["flangeThickness"] = 1, ["radius"] = radius },
        7 => new() { ["width"] = 6, ["height"] = 10, ["thickness"] = 1, ["innerRadius"] = radius },
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    [Theory]
    [MemberData(nameof(Shapes))]
    public void EveryShapeRadiusAndAxisPreservesOriginalInputsAndDerivedProperties(int kind, double radius,
        SectionAxisDesignation axis)
    {
        var section = ParametricSectionTestSupport.Shape(kind, r: radius);
        var state = State() with { Document = State().Document.WithSection(section, axis) };
        var bytes = ProjectFileCodec.Serialize(state);
        var root = JsonNode.Parse(bytes)!;
        Assert.Equal(2, root["formatVersion"]!.GetValue<int>());
        Assert.Equal(axis.ToString().ToLowerInvariant(), root["document"]!["bendingAxis"]!.GetValue<string>());
        var wireSection = root["document"]!["section"]!.AsObject();
        Assert.Equal(WireTypes[kind], wireSection["type"]!.GetValue<string>());
        var parameters = Parameters(kind, radius);
        Assert.Equal(parameters.Keys.Append("type").Order(), wireSection.Select(x => x.Key).Order());
        foreach (var p in parameters) Assert.Equal(p.Value, wireSection[p.Key]!.GetValue<double>());
        var loaded = ProjectFileCodec.Deserialize(bytes);
        var actual = Assert.IsAssignableFrom<IParametricSectionDefinition>(loaded.Document.Section);
        Assert.Equal(section.GetType(), actual.GetType());
        Assert.Equal(section.ShapeKind, actual.ShapeKind);
        Assert.Equivalent(section.GeometryProperties, actual.GeometryProperties, strict: true);
        Assert.Equal(section.Axes, actual.Axes);
        Assert.Equal(axis, loaded.Document.BendingAxis);
        Assert.True(state.ContentEquals(loaded));
        Assert.True(loaded.ContentEquals(ProjectFileCodec.Deserialize(ProjectFileCodec.Serialize(loaded))));
        Assert.Equal(state.Document.NamedEntities, loaded.Document.NamedEntities);
        Assert.Equal(state.Document.NamingState, loaded.Document.NamingState);
        Assert.True(state.Presentation.ContentEquals(loaded.Presentation));
        foreach (string excluded in new[] { "libraryId", "presetId", "sourceId", "sourceLibrary", "origin", "provenance" })
            Assert.DoesNotContain(excluded, Encoding.UTF8.GetString(bytes));
    }

    public static IEnumerable<object[]> RequiredParameters()
    {
        for (int kind = 0; kind < 8; kind++)
            foreach (var field in Parameters(kind, 0).Keys)
                foreach (var nullValue in new[] { false, true }) yield return [kind, field, nullValue];
    }

    [Theory]
    [MemberData(nameof(RequiredParameters))]
    public void EachOriginalParameterIsRequiredIncludingZeroRadius(int kind, string field, bool nullValue)
    {
        var section = ParametricSectionTestSupport.Shape(kind);
        var root = JsonNode.Parse(ProjectFileCodec.Serialize(State() with
            { Document = State().Document.WithSection(section, section.Axes[0].AxisDesignation) }))!;
        var wire = root["document"]!["section"]!.AsObject();
        if (nullValue) wire[field] = null;
        else wire.Remove(field);
        Assert.Throws<ProjectFormatException>(() => Read(root));
    }

    public static IEnumerable<object[]> InvalidParameters()
    {
        for (int kind = 0; kind < 8; kind++)
            foreach (var field in Parameters(kind, 0).Keys)
            {
                yield return [kind, field, -1d];
                if (!field.Contains("Radius", StringComparison.OrdinalIgnoreCase)) yield return [kind, field, 0d];
                yield return [kind, field, 1e300];
            }
        yield return [1, "outerRadius", 3.000001];
        yield return [4, "radius", 2.500001];
        yield return [5, "radius", 4.000001];
        yield return [6, "radius", 2.500001];
        yield return [7, "innerRadius", 5.000001];
    }

    [Theory]
    [MemberData(nameof(InvalidParameters))]
    public void InvalidGeometryIsRejectedWithoutRepair(int kind, string field, double value)
    {
        var section = ParametricSectionTestSupport.Shape(kind);
        var root = JsonNode.Parse(ProjectFileCodec.Serialize(State() with
            { Document = State().Document.WithSection(section, section.Axes[0].AxisDesignation) }))!;
        root["document"]!["section"]![field] = value;
        Assert.Throws<ProjectFormatException>(() => Read(root));
    }

    [Theory]
    [InlineData(0, "u")]
    [InlineData(4, "u")]
    [InlineData(7, "y")]
    [InlineData(7, "z")]
    [InlineData(2, "future")]
    [InlineData(2, "Y")]
    [InlineData(2, "1")]
    public void UnavailableOrUnknownBendingAxisIsRejected(int kind, string axis)
    {
        var section = ParametricSectionTestSupport.Shape(kind);
        var root = JsonNode.Parse(ProjectFileCodec.Serialize(State() with
            { Document = State().Document.WithSection(section, section.Axes[0].AxisDesignation) }))!;
        root["document"]!["bendingAxis"] = axis;
        Assert.Throws<ProjectFormatException>(() => Read(root));
    }

    [Fact]
    public void MissingNullAndNumericBendingAxisAreRejected()
    {
        var root = JsonNode.Parse(ProjectFileCodec.Serialize(State()))!;
        root["document"]!["bendingAxis"] = null;
        Assert.Throws<ProjectFormatException>(() => Read(root));
        root["document"]!["bendingAxis"] = 0;
        Assert.Throws<ProjectFormatException>(() => Read(root));
        root["document"]!.AsObject().Remove("bendingAxis");
        Assert.Throws<ProjectFormatException>(() => Read(root));
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
    public void ManualAxesRoundtripWithCanonicalOrderAndOneTabulatedModulus(SectionAxisDesignation axis, bool pair)
    {
        var partner = axis switch { SectionAxisDesignation.Y => SectionAxisDesignation.Z, SectionAxisDesignation.Z => SectionAxisDesignation.Y,
            SectionAxisDesignation.U => SectionAxisDesignation.V, _ => SectionAxisDesignation.U };
        var section = new ManualSectionDefinition(Area.FromSquareMeters(.0012345678901234567),
            new(axis, SecondMomentOfArea.FromMetersToTheFourth(8e-7), SectionModulus.FromCubicMeters(2e-5)),
            pair ? new(partner, SecondMomentOfArea.FromMetersToTheFourth(4e-7), SectionModulus.FromCubicMeters(1e-5)) : null);
        var state = State() with { Document = State().Document.WithSection(section, axis) };
        var bytes = ProjectFileCodec.Serialize(state);
        var loaded = ProjectFileCodec.Deserialize(bytes);
        var actual = Assert.IsType<ManualSectionDefinition>(loaded.Document.Section);
        Assert.Equal(section.Area, actual.Area);
        Assert.Equal(section.Axes, actual.Axes);
        Assert.Equal(axis, loaded.Document.BendingAxis);
        Assert.True(state.ContentEquals(loaded));
        var wire = JsonNode.Parse(bytes)!["document"]!["section"]!;
        Assert.Equal("manual", wire["type"]!.GetValue<string>());
        Assert.Equal(new[] { "area", "axes", "type" }, wire.AsObject().Select(x => x.Key).Order());
        foreach (var a in wire["axes"]!.AsArray())
            Assert.Equal(new[] { "designation", "secondMomentOfArea", "sectionModulus" }, a!.AsObject().Select(x => x.Key).Order());
        Assert.All(actual.Axes, a => Assert.Equal(a.PositiveSectionModulus, a.NegativeSectionModulus));
    }

    [Theory]
    [InlineData("y", "y")]
    [InlineData("y", "u")]
    [InlineData("z", "v")]
    [InlineData("u", "z")]
    [InlineData("future", "v")]
    public void InvalidManualPairsAreRejected(string first, string second)
    {
        var root = ManualJson();
        var axes = root["document"]!["section"]!["axes"]!.AsArray();
        axes[0]!["designation"] = first;
        var extra = axes[0]!.DeepClone(); extra["designation"] = second; axes.Add(extra);
        Assert.Throws<ProjectFormatException>(() => Read(root));
    }

    [Theory]
    [InlineData("area", 0)]
    [InlineData("area", -1)]
    [InlineData("axes.0.secondMomentOfArea", 0)]
    [InlineData("axes.0.secondMomentOfArea", -1)]
    [InlineData("axes.0.sectionModulus", 0)]
    [InlineData("axes.0.sectionModulus", -1)]
    public void InvalidManualValuesAreRejected(string path, double value)
    {
        var root = ManualJson();
        Set(root["document"]!["section"]!, path, JsonValue.Create(value));
        Assert.Throws<ProjectFormatException>(() => Read(root));
    }

    [Theory]
    [InlineData("area")]
    [InlineData("axes")]
    [InlineData("axes.0.designation")]
    [InlineData("axes.0.secondMomentOfArea")]
    [InlineData("axes.0.sectionModulus")]
    public void ManualRequiredFieldsRejectMissingAndNull(string path)
    {
        var root = ManualJson();
        var (parent, key) = Parent(root["document"]!["section"]!, path);
        var saved = parent[key]?.DeepClone();
        parent[key] = null;
        Assert.Throws<ProjectFormatException>(() => Read(root));
        parent.Remove(key);
        Assert.Throws<ProjectFormatException>(() => Read(root));
        parent[key] = saved;
    }

    [Fact]
    public void ManualAxisCountAndSelectedAxisAreValidated()
    {
        var root = ManualJson(); root["document"]!["bendingAxis"] = "z";
        Assert.Throws<ProjectFormatException>(() => Read(root));
        root["document"]!["bendingAxis"] = "y";
        var axes = root["document"]!["section"]!["axes"]!.AsArray();
        axes.Add(null); Assert.Throws<ProjectFormatException>(() => Read(root));
        axes.Clear(); Assert.Throws<ProjectFormatException>(() => Read(root));
        for (int i = 0; i < 3; i++) axes.Add(new JsonObject());
        Assert.Throws<ProjectFormatException>(() => Read(root));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void MaterialSnapshotPreservesOptionalValuesExactly(bool density, bool poisson)
    {
        var old = State();
        var material = new Material(old.Document.Material.Name, old.Document.Material.YoungsModulus, old.Document.Material.YieldStrength,
            density ? MassDensity.FromKilogramsPerCubicMeter(7850.123456789) : null,
            poisson ? PoissonRatio.FromValue(.30000000000000004) : null);
        var state = old with { Document = old.Document with { Material = material } };
        var bytes = ProjectFileCodec.Serialize(state);
        var loaded = ProjectFileCodec.Deserialize(bytes);
        Assert.Equal(material.Density, loaded.Document.Material.Density);
        Assert.Equal(material.PoissonRatio, loaded.Document.Material.PoissonRatio);
        Assert.True(state.ContentEquals(loaded));
        var root = JsonNode.Parse(bytes)!;
        if (!density) root["document"]!["material"]!["density"] = null;
        if (!poisson) root["document"]!["material"]!["poissonRatio"] = null;
        Assert.True(state.ContentEquals(Read(root)));
    }

    [Theory]
    [InlineData("density", 0)]
    [InlineData("density", -1)]
    [InlineData("poissonRatio", -1)]
    [InlineData("poissonRatio", .5)]
    [InlineData("poissonRatio", 1)]
    public void InvalidOptionalMaterialValuesAreRejected(string field, double value)
    {
        var root = JsonNode.Parse(ProjectFileCodec.Serialize(State()))!;
        root["document"]!["material"]![field] = value;
        Assert.Throws<ProjectFormatException>(() => Read(root));
    }

    [Theory]
    [InlineData("density")]
    [InlineData("poissonRatio")]
    public void NonFiniteMaterialValuesAreRejected(string field)
    {
        foreach (var token in new[] { "NaN", "Infinity", "-Infinity", "1e999" })
        {
            var root = JsonNode.Parse(ProjectFileCodec.Serialize(State()))!;
            root["document"]!["material"]![field] = "TOKEN";
            var json = root.ToJsonString().Replace("\"TOKEN\"", token, StringComparison.Ordinal);
            Assert.Throws<ProjectFormatException>(() => ProjectFileCodec.Deserialize(Encoding.UTF8.GetBytes(json)));
        }
    }

    [Fact]
    public void SectionDiscriminatorMayAppearLastAndDuplicatesAreRejectedAtEveryDepth()
    {
        var root = JsonNode.Parse(ProjectFileCodec.Serialize(State()))!;
        var section = root["document"]!["section"]!.AsObject();
        var type = section["type"]!.DeepClone(); section.Remove("type"); section.Add("type", type);
        Assert.True(State().ContentEquals(Read(root)));
        foreach (var token in new[] { "\"type\":\"rectangle\",\"type\":\"rectangle\"", "\"width\":0.1,\"width\":0.1" })
        {
            var text = root.ToJsonString();
            text = token.StartsWith("\"type\"", StringComparison.Ordinal)
                ? text.Replace("\"type\":\"rectangle\"", token, StringComparison.Ordinal)
                : text.Replace("\"width\":0.1", token, StringComparison.Ordinal);
            Assert.Throws<ProjectFormatException>(() => ProjectFileCodec.Deserialize(Encoding.UTF8.GetBytes(text)));
        }
        root["future"] = new JsonObject { ["value"] = 1 };
        var duplicateUnknown = root.ToJsonString().Replace("\"value\":1", "\"value\":1,\"value\":2", StringComparison.Ordinal);
        Assert.Throws<ProjectFormatException>(() => ProjectFileCodec.Deserialize(Encoding.UTF8.GetBytes(duplicateUnknown)));
    }

    [Theory]
    [MemberData(nameof(RequiredParameters))]
    public void NonFiniteOriginalInputsAreRejected(int kind, string field, bool quoted)
    {
        var section = ParametricSectionTestSupport.Shape(kind);
        foreach (var token in new[] { "NaN", "Infinity", "-Infinity", "1e999" })
        {
            var root = JsonNode.Parse(ProjectFileCodec.Serialize(State() with
                { Document = State().Document.WithSection(section, section.Axes[0].AxisDesignation) }))!;
            root["document"]!["section"]![field] = "TOKEN";
            var json = root.ToJsonString().Replace("\"TOKEN\"", quoted ? "\"" + token + "\"" : token, StringComparison.Ordinal);
            Assert.Throws<ProjectFormatException>(() => ProjectFileCodec.Deserialize(Encoding.UTF8.GetBytes(json)));
        }
    }

    [Theory]
    [InlineData("area")]
    [InlineData("axes.0.secondMomentOfArea")]
    [InlineData("axes.0.sectionModulus")]
    public void NonFiniteManualInputsAreRejected(string path)
    {
        foreach (var token in new[] { "NaN", "Infinity", "-Infinity", "1e999" })
        {
            var root = ManualJson();
            Set(root["document"]!["section"]!, path, JsonValue.Create("TOKEN"));
            var json = root.ToJsonString().Replace("\"TOKEN\"", token, StringComparison.Ordinal);
            Assert.Throws<ProjectFormatException>(() => ProjectFileCodec.Deserialize(Encoding.UTF8.GetBytes(json)));
        }
    }

    [Theory]
    [InlineData("custom")]
    [InlineData("legacy")]
    [InlineData("oldSection")]
    [InlineData("customLegacy")]
    public void V2HasNoLegacySectionDiscriminator(string type)
    {
        var root = JsonNode.Parse(ProjectFileCodec.Serialize(State()))!;
        root["document"]!["section"]!["type"] = type;
        Assert.Throws<ProjectFormatException>(() => Read(root));
    }

    [Fact]
    public void WireManualAxisOrderIsCanonicalizedOnLoad()
    {
        var root = ManualJson(); var axes = root["document"]!["section"]!["axes"]!.AsArray();
        var z = axes[0]!.DeepClone(); z["designation"] = "z"; axes.Insert(0, z);
        var loaded = Read(root);
        Assert.Equal(new[] { SectionAxisDesignation.Y, SectionAxisDesignation.Z }, loaded.Document.Section.Axes.Select(a => a.AxisDesignation));
        var saved = JsonNode.Parse(ProjectFileCodec.Serialize(loaded))!;
        Assert.Equal("y", saved["document"]!["section"]!["axes"]![0]!["designation"]!.GetValue<string>());
        Assert.Equal("z", saved["document"]!["section"]!["axes"]![1]!["designation"]!.GetValue<string>());
    }

    private static JsonNode ManualJson()
    {
        var section = new ManualSectionDefinition(Area.FromSquareMeters(.0012),
            new(SectionAxisDesignation.Y, SecondMomentOfArea.FromMetersToTheFourth(8e-7), SectionModulus.FromCubicMeters(2e-5)));
        return JsonNode.Parse(ProjectFileCodec.Serialize(State() with { Document = State().Document.WithSection(section, SectionAxisDesignation.Y) }))!;
    }

    private static ProjectState Read(JsonNode root) => ProjectFileCodec.Deserialize(Encoding.UTF8.GetBytes(root.ToJsonString()));
    private static (JsonObject Parent, string Key) Parent(JsonNode root, string path)
    {
        var parts = path.Split('.');
        foreach (var part in parts[..^1]) root = root is JsonArray array ? array[int.Parse(part)]! : root[part]!;
        return (root.AsObject(), parts[^1]);
    }
    private static void Set(JsonNode root, string path, JsonNode? value)
    {
        var (parent, key) = Parent(root, path); parent[key] = value;
    }
}
