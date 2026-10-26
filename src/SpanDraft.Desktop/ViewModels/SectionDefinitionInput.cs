using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Resources;
using SpanDraft.Reporting.Notation;

namespace SpanDraft.Desktop.ViewModels;

/// <summary>Typing is transient. Only Commit can change the confirmed SI value.</summary>
public sealed class SectionDefinitionInput : ObservableObject
{
    internal sealed record Snapshot(double? ConfirmedValue, bool HasInteracted);
    internal event Action? BeforeCommittedValueChange;
    private readonly Action<SectionDefinitionInput, bool> _changed;
    private readonly bool _nonnegative;
    private string _pendingText = "", _committedText = "";
    private string? _error;
    private bool _parseError;
    public SectionDefinitionInput(string key, UnitDefinition unit, Action<SectionDefinitionInput, bool> changed, bool nonnegative = false, TechnicalText? displayLabel = null)
    { Key = key; Unit = unit; _changed = changed; _nonnegative = nonnegative; DisplayText = displayLabel ?? TechnicalSymbols.SectionInput(key); }
    public string Key { get; }
    public UnitDefinition Unit { get; }
    public string Symbol => TechnicalSymbols.SectionInput(Key).PlainText;
    public TechnicalText DisplayText { get; }
    public string DisplayLabel => DisplayText.PlainText;
    public bool HasRadiusHint => Key == "r";
    public bool LastCommitChangedValue { get; private set; }
    public string Label => DisplayLabel + " [" + Unit.Symbol + "]";
    public string PendingText
    {
        get => _pendingText;
        set { if (Set(ref _pendingText, value)) { Notify(nameof(IsPending)); _changed(this, false); } }
    }
    public double? ConfirmedValue { get; private set; }
    public bool IsPending => PendingText != _committedText;
    public bool HasInteracted { get; private set; }
    public bool HasParseError => _parseError;
    public string? Error => _error;
    public bool HasError => Error is not null;
    internal Snapshot Capture() => new(ConfirmedValue, HasInteracted);
    internal void Restore(Snapshot snapshot)
    {
        ConfirmedValue = snapshot.ConfirmedValue;
        HasInteracted = snapshot.HasInteracted;
        _pendingText = _committedText = ConfirmedValue is { } value ? InputQuantityFormatter.FormatExact(value, Unit) : "";
        _parseError = false;
        LastCommitChangedValue = false;
        SetError(null);
        Notify(nameof(PendingText)); Notify(nameof(ConfirmedValue)); Notify(nameof(IsPending)); Notify(nameof(HasInteracted));
    }
    // Loading a preset keeps the original SI value; display-unit round trips must not alter it.
    internal void Load(double value)
    {
        LastCommitChangedValue = ConfirmedValue != value;
        ConfirmedValue = value;
        _pendingText = _committedText = InputQuantityFormatter.FormatExact(value, Unit);
        _parseError = false;
        HasInteracted = true;
        SetError(null);
        Notify(nameof(PendingText)); Notify(nameof(ConfirmedValue)); Notify(nameof(IsPending));
    }
    public bool Commit()
    {
        if (HasInteracted && !IsPending) return !HasError;
        LastCommitChangedValue = false;
        var previousValue = ConfirmedValue;
        bool invalid = !InputQuantityFormatter.TryParse(PendingText, Unit, out var value) || (_nonnegative ? value < 0 : value <= 0);
        if (!invalid && ConfirmedValue != value) BeforeCommittedValueChange?.Invoke();
        HasInteracted = true;
        _committedText = PendingText;
        _parseError = invalid;
        if (_parseError) SetError(_nonnegative ? Strings.NonnegativeInput : Strings.PositiveInput);
        else { ConfirmedValue = value; SetError(null); }
        LastCommitChangedValue = previousValue != ConfirmedValue;
        Notify(nameof(ConfirmedValue)); Notify(nameof(IsPending));
        _changed(this, true);
        return !HasError;
    }
    internal void SetGeometryError(string? error) { if (!_parseError) SetError(error); }
    private void SetError(string? error)
    { _error = error; Notify(nameof(Error)); Notify(nameof(HasError)); }
}
