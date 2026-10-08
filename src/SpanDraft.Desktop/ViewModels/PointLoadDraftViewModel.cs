using SpanDraft.Core.Units;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.Presentation;

namespace SpanDraft.Desktop.ViewModels;

/// <summary>Transactional text buffers with a separate, pointer-controlled canvas position.</summary>
public sealed class PointLoadDraftViewModel : ObservableObject
{
    private readonly Func<EditorDocument> _read;
    private readonly UnitDefinition _positionUnit, _valueUnit;
    private Length _referencePosition;
    private string _referencePositionText;
    private readonly double _referenceValue;
    private readonly string _referenceValueText;
    private string _positionText;
    private string _valueText;
    private string _nameText;
    private string? _nameError;
    private Length _inputPosition;
    private double _inputValue;
    private string? _positionError;
    private string? _valueError;
    private PointLoadPreview _preview;

    public PointLoadDraftViewModel(Func<EditorDocument> read, Guid? originalId,
        PointLoadKind kind, Length position, double value, UnitProfile? profile = null)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
        _read = read;
        profile ??= UnitProfile.Default;
        _positionUnit = profile[QuantityKind.BeamLength];
        _valueUnit = profile[kind == PointLoadKind.Force ? QuantityKind.TransverseForce : QuantityKind.Moment];
        OriginalId = originalId;
        Kind = kind;
        var document = read();
        AutoCandidate = originalId is null ? EntityNaming.Peek(document,
            kind == PointLoadKind.Force ? AutoNameKind.Force : AutoNameKind.Moment) : null;
        _nameText = AutoCandidate?.Name ?? document.Loads.First(l => l.Id == originalId).Name;
        _referencePosition = position;
        _positionText = _referencePositionText = InputQuantityFormatter.Format(position.Meters, _positionUnit);
        _referenceValue = value;
        _valueText = _referenceValueText = InputQuantityFormatter.Format(value, _valueUnit);
        _preview = new(position, kind, value);
        Validate();
    }

    public Guid? OriginalId { get; }
    public bool IsExisting => OriginalId.HasValue;
    public AutoNameCandidate? AutoCandidate { get; }
    public string NameText
    {
        get => _nameText;
        set { if (Set(ref _nameText, value)) Validate(); }
    }
    public string? NameError => _nameError;
    public bool HasNameError => NameError is not null;
    public PointLoadKind Kind { get; }
    public string ValueLabel => Kind == PointLoadKind.Force ? Strings.ForceValueLabel : Strings.MomentValueLabel;
    public string Unit => _valueUnit.Symbol;
    public string PositionUnit => _positionUnit.Symbol;
    public string PositionText
    {
        get => _positionText;
        set { if (Set(ref _positionText, value)) Validate(); }
    }
    public string ValueText
    {
        get => _valueText;
        set { if (Set(ref _valueText, value)) Validate(); }
    }
    public string? PositionError => _positionError;
    public string? ValueError => _valueError;
    public bool HasPositionError => PositionError is not null;
    public bool HasValueError => ValueError is not null;
    public bool IsValid => !HasPositionError && !HasValueError && !HasNameError;
    public PointLoadPreview Preview => _preview;
    public Length CanvasPosition => Preview.Position;

    public bool TryGetValues(out Length position, out double value)
    {
        Validate();
        position = _inputPosition;
        value = _inputValue;
        return IsValid;
    }

    internal void ApplyDragPosition(Length position)
    {
        _referencePosition = position;
        _referencePositionText = InputQuantityFormatter.Format(position.Meters, _positionUnit);
        _preview = _preview with { Position = position };
        Set(ref _positionText, _referencePositionText, nameof(PositionText));
        Validate();
        Notify(nameof(CanvasPosition));
    }

    private void Validate()
    {
        var document = _read();
        EntityNaming.TryNormalize(document, NameText, OriginalId, out _, out var nameError);
        _nameError = nameError switch
        {
            NameValidationError.Empty => Strings.EmptyEntityName,
            NameValidationError.Duplicate => Strings.DuplicateEntityName,
            _ => null
        };
        bool positionValid;
        Length position;
        if (PositionText == _referencePositionText)
        {
            position = _referencePosition;
            positionValid = position.Meters >= 0 && position.Meters <= document.Length.Meters;
        }
        else positionValid = InputQuantityFormatter.TryParsePosition(PositionText, _positionUnit, document.Length, out position);
        _positionError = positionValid
            ? null : Strings.PositionInsideBeam;
        if (_positionError is null)
            _inputPosition = PositionText == _referencePositionText ? _referencePosition : position;
        double value = _referenceValue;
        _valueError = ValueText == _referenceValueText || InputQuantityFormatter.TryParse(ValueText, _valueUnit, out value)
            ? null : Strings.InvalidLoadValue;
        if (_valueError is null)
        {
            _inputValue = ValueText == _referenceValueText ? _referenceValue : value;
            _preview = _preview with { Value = _inputValue };
        }
        _preview = _preview with { IsInvalid = !IsValid };
        foreach (string name in new[] { nameof(PositionError), nameof(ValueError), nameof(HasPositionError),
            nameof(HasValueError), nameof(NameError), nameof(HasNameError), nameof(IsValid), nameof(Preview) }) Notify(name);
    }
}
