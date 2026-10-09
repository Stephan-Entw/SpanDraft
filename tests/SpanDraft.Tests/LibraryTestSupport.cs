using System.Text;
using System.Text.Json.Nodes;
using SpanDraft.Core.Materials;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Libraries;

namespace SpanDraft.Tests;

internal static class LibraryTestSupport
{
    // Synthetic input, not production material reference data.
    internal const string MaterialFixture = """
        {
          "format": "SpanDraft.MaterialLibrary",
          "formatVersion": 1,
          "entries": [{
            "material": { "name": "Fixture A", "youngsModulus": 123456789.01234567,
              "yieldStrength": 2345678.901234567, "density": 1234.5, "poissonRatio": 0 },
            "category": "stainlessSteel", "materialNumber": "1.2345",
            "otherStandards": ["Fixture standard A", "Fixture standard B"]
          }]
        }
        """;

    internal static MaterialLibraryEntry MaterialEntry(string name = "Fixture A", double modulus = 123456789.01234567) =>
        new(new Material(name, Pressure.FromPascals(modulus), Pressure.FromPascals(2345678.901234567)),
            MaterialCategory.Other);

    internal static byte[] Bytes(JsonNode json) => Encoding.UTF8.GetBytes(json.ToJsonString());

    internal static JsonNode MaterialJson(bool catalog = false)
    {
        var json = JsonNode.Parse(MaterialFixture)!;
        if (catalog) json["format"] = "SpanDraft.MaterialCatalog";
        return json;
    }
}
