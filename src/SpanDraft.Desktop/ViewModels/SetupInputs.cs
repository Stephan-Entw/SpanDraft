using SpanDraft.Core.Sections;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Resources;

namespace SpanDraft.Desktop.ViewModels;

public enum SetupEditorMode { ProjectDraft, UserLibraryEdit }
public abstract record SetupChoiceLabel(string Label);
public sealed record SetupChoice<T>(T Value, string Label) : SetupChoiceLabel(Label);
public sealed record AxisChoice(SectionAxisDesignation Value)
{
    public string Label => Value.ToString().ToLowerInvariant() + "-" + Value.ToString().ToLowerInvariant();
}

/// <summary>Raw input and original SI values remain separate, including incomplete text.</summary>
public sealed class SetupNumberInput : ObservableObject
{
    private readonly Action _changed;
    private readonly double? _original;
    private readonly string _originalText;
    private readonly bool _optional, _nonnegative, _signed;
    private string _text;
    public SetupNumberInput(string key, string label, UnitDefinition unit, Action changed, double? value = null,
        bool optional = false, bool nonnegative = false, bool signed = false, string? hint = null)
    {
        Key = key; Label = label; Unit = unit; _changed = changed;
        _optional = optional; _nonnegative = nonnegative; _signed = signed;
        _original = value;
        _text = _originalText = value is { } v ? InputQuantityFormatter.Format(v, unit) : "";
        Hint = hint;
    }
    public string Key { get; }
    public string Label { get; }
    public UnitDefinition Unit { get; }
    public string? Hint { get; }
    public bool HasHint => !string.IsNullOrEmpty(Hint);
    public string InputLabel => Label + (Unit.Symbol.Length > 0 ? " [" + Unit.Symbol + "]" : "");
    public string Text
    {
        get => _text;
        set
        {
            if (!Set(ref _text, value)) return;
            Notify(nameof(Error)); Notify(nameof(HasError)); _changed();
        }
    }
    public bool TryRead(out double? value)
    {
        value = null;
        if (_optional && string.IsNullOrWhiteSpace(Text)) return true;
        double v;
        if (Text == _originalText && _original is { } original) v = original;
        else if (!InputQuantityFormatter.TryParse(Text, Unit, out v)) return false;
        if (!_signed && (_nonnegative ? v < 0 : v <= 0)) return false;
        value = v; return true;
    }
    public string? Error => TryRead(out _) ? null : _signed ? Strings.InvalidLoadValue
        : _nonnegative ? Strings.NonnegativeInput : Strings.PositiveInput;
    public bool HasError => Error is not null;
}

public sealed record SectionAxisValue(string Label, string Inertia, string Modulus, string PositiveModulus,
    string NegativeModulus, bool IsAsymmetric)
{
    public static IReadOnlyList<SectionAxisValue> From(ISectionDefinition section, ResultPresentationOptions options)
    {
        string Format(double si, QuantityKind kind) => QuantityFormatter.Format(si, kind, options.Profile, options.Mode);
        return section.Axes.Select(a => new SectionAxisValue(new AxisChoice(a.AxisDesignation).Label,
            Format(a.SecondMomentOfArea.MetersToTheFourth, QuantityKind.SecondMomentOfArea),
            Format(a.PositiveSectionModulus.CubicMeters, QuantityKind.SectionModulus),
            Format(a.PositiveSectionModulus.CubicMeters, QuantityKind.SectionModulus),
            Format(a.NegativeSectionModulus.CubicMeters, QuantityKind.SectionModulus),
            a.PositiveSectionModulus != a.NegativeSectionModulus)).ToArray();
    }
}
