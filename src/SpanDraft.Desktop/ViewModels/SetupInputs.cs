using SpanDraft.Core.Sections;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Resources;
using SpanDraft.Reporting.Notation;

namespace SpanDraft.Desktop.ViewModels;

public enum SetupEditorMode { ProjectDraft, UserLibraryEdit, UserLibraryCreate }
public enum SectionEditorType { Rectangle, RectangularHollow, Circle, CircularHollow, ISection, USection, TSection, Angle, Manual }
public enum ManualAxisConfiguration { Y, Z, U, V, YZ, UV }
public abstract record SetupChoiceLabel(string Label)
{
    public TechnicalText DisplayText { get; init; } = Label;
}
public sealed record SetupChoice<T>(T Value, string Label) : SetupChoiceLabel(Label)
{
    public override string ToString() => Label;
}
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
    private TechnicalText _label;
    private bool _validationVisible;
    public SetupNumberInput(string key, TechnicalText label, UnitDefinition unit, Action changed, double? value = null,
        bool optional = false, bool nonnegative = false, bool signed = false, string? hint = null)
    {
        Key = key; _label = label; Unit = unit; _changed = changed;
        _optional = optional; _nonnegative = nonnegative; _signed = signed;
        _original = value;
        _text = _originalText = value is { } v ? InputQuantityFormatter.Format(v, unit) : "";
        Hint = hint;
    }
    public string Key { get; }
    public string Label => _label.PlainText;
    public TechnicalText LabelText => _label;
    internal void SetLabel(TechnicalText label)
    {
        if (Set(ref _label, label, nameof(Label))) { Notify(nameof(InputLabel)); Notify(nameof(LabelText)); }
    }
    public UnitDefinition Unit { get; }
    public string? Hint { get; }
    public bool HasHint => !string.IsNullOrEmpty(Hint);
    public string DisplayUnit => Hint ?? Unit.Symbol;
    public string InputLabel => Label + (Unit.Symbol.Length > 0 ? " [" + Unit.Symbol + "]" : "");
    public string Text
    {
        get => _text;
        set
        {
            if (!Set(ref _text, value)) return;
            Notify(nameof(Error)); Notify(nameof(HasError)); Notify(nameof(ShowError)); _changed();
        }
    }
    // Validity always gates the actions; field feedback starts after the first completed interaction.
    public bool ShowError => _validationVisible && HasError;
    public void CompleteInput()
    {
        _validationVisible = true;
        Notify(nameof(ShowError));
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
