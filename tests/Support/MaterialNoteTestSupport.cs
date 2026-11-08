using SpanDraft.Core.Materials;
using SpanDraft.Desktop.Persistence;

namespace SpanDraft.Tests;

internal static class MaterialNoteTestSupport
{
    internal static Material WithNote(Material material, MaterialNote? note) => MaterialStrengthWire.Read(
        material.Name, material.YoungsModulus, material.YieldStrength, MaterialStrengthWire.Write(material.StrengthType),
        material.Density, material.PoissonRatio, material.Category, material.MaterialNumber, material.OtherStandards, note);
}
