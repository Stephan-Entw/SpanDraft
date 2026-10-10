using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Resources;

namespace SpanDraft.Desktop.ViewModels;

/// <summary>Typing is transient. Only Commit can change the confirmed SI value.</summary>
public sealed class SectionDefinitionInput : ObservableObject
{
    private readonly Action<SectionDefinitionInput, bool> _changed;
    private readonly bool _nonnegative;
    private string _pendingText = "", _committedText = "";
    private string? _error;
    private bool _parseError;
    public SectionDefinitionInput(string key, UnitDefinition unit, Action<SectionDefinitionInput, bool> changed, bool nonnegative = false)
    { Key = key; Unit = unit; _changed = changed; _nonnegative = nonnegative; }
    public string Key { get; }
    public UnitDefinition Unit { get; }
    public string Symbol => Key.Length > 1 && char.IsUpper(Key[0]) ? Key[..1] + Key[1..].ToLowerInvariant() : Key;
    public string Label => Symbol + " [" + Unit.Symbol + "]";
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
    public bool Commit()
    {
        if (HasInteracted && !IsPending) return !HasError;
        HasInteracted = true;
        _committedText = PendingText;
        _parseError = !InputQuantityFormatter.TryParse(PendingText, Unit, out var value) || (_nonnegative ? value < 0 : value <= 0);
        if (_parseError) SetError(_nonnegative ? Strings.NonnegativeInput : Strings.PositiveInput);
        else { ConfirmedValue = value; SetError(null); }
        Notify(nameof(ConfirmedValue)); Notify(nameof(IsPending));
        _changed(this, true);
        return !HasError;
    }
    internal void SetGeometryError(string? error) { if (!_parseError) SetError(error); }
    private void SetError(string? error)
    { _error = error; Notify(nameof(Error)); Notify(nameof(HasError)); }
}
