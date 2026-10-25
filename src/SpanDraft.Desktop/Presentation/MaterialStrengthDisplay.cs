using SpanDraft.Core.Materials;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.State;
using SpanDraft.Reporting.Notation;

namespace SpanDraft.Desktop.Presentation;

internal static class MaterialStrengthDisplay
{
    internal static string Symbol(MaterialStrengthType? type) => SymbolText(type).PlainText;

    internal static TechnicalText SymbolText(MaterialStrengthType? type) => type switch
    {
        MaterialStrengthType.Re => TechnicalSymbols.YieldStrength,
        MaterialStrengthType.Rp02 => TechnicalSymbols.ProofStrength,
        null => Strings.MaterialStrengthUnspecified,
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    internal static string Label(MaterialStrengthType? type) => LabelText(type).PlainText;

    internal static TechnicalText LabelText(MaterialStrengthType? type) => type switch
    {
        MaterialStrengthType.Re => Strings.MaterialStrengthRe + " " + SymbolText(type),
        MaterialStrengthType.Rp02 => Strings.MaterialStrengthRp02 + " " + SymbolText(type),
        null => Strings.MaterialStrength + " (" + Strings.MaterialStrengthUnspecified + ")",
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    internal static string Summary(Material material) => SummaryText(material).PlainText;
    internal static TechnicalText SummaryText(Material material) =>
        SymbolText(material.StrengthType) + " " + UiNumbers.Indicator(material.YieldStrength.Megapascals);
}
