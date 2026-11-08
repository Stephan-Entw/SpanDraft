using System.Text;
using System.Text.Json.Nodes;
using SpanDraft.Core.Materials;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.State;

namespace SpanDraft.Tests;

public abstract class MaterialStrengthTestSupport
{
    protected static Material Typed(MaterialStrengthType type) =>
        new("Independent material", Pressure.FromPascals(210e9), Pressure.FromMegapascals(235), type);

    protected static ProjectState WithMaterial(Material material)
    {
        var state = ReportTestProjects.Create();
        return state with { Document = state.Document with { Material = material } };
    }

    protected static byte[] LegacyProject(int version = 3)
    {
        var root = JsonNode.Parse(ProjectFileCodec.Serialize(WithMaterial(Typed(MaterialStrengthType.Re))))!;
        root["formatVersion"] = version;
        root["document"]!["material"]!.AsObject().Remove("strengthType");
        if (version == 2) root.AsObject().Remove("info");
        return Encoding.UTF8.GetBytes(root.ToJsonString());
    }
}
