using SpanDraft.Core.Materials;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.State;

namespace SpanDraft.Desktop.Presentation;

internal static class MaterialStrengthDisplay
{
    internal static string Symbol(MaterialStrengthType? type) => type switch
    {
        MaterialStrengthType.Re => "Re",
        MaterialStrengthType.Rp02 => "Rp0,2",
        null => Strings.MaterialStrengthUnspecified,
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    internal static string Label(MaterialStrengthType? type) => type switch
    {
        MaterialStrengthType.Re => Strings.MaterialStrengthRe,
        MaterialStrengthType.Rp02 => Strings.MaterialStrengthRp02,
        null => Strings.MaterialStrength + " (" + Strings.MaterialStrengthUnspecified + ")",
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    internal static string Summary(Material material) =>
        Symbol(material.StrengthType) + " " + UiNumbers.Indicator(material.YieldStrength.Megapascals);
}
