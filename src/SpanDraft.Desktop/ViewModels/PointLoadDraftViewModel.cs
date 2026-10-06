using SpanDraft.Core.Units;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.State;

namespace SpanDraft.Desktop.ViewModels;

/// <summary>Transactional text buffers with a separate, pointer-controlled canvas position.</summary>
public sealed class PointLoadDraftViewModel : ObservableObject
{
    private readonly Func<EditorDocument> _read;
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
        PointLoadKind kind, Length position, double value)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
        _read = read;
        OriginalId = originalId;
        Kind = kind;
        var document = read();
        AutoCandidate = originalId is null ? EntityNaming.Peek(document,
            kind == PointLoadKind.Force ? AutoNameKind.Force : AutoNameKind.Moment) : null;
        _nameText = AutoCandidate?.Name ?? document.Loads.First(l => l.Id == originalId).Name;
        _referencePosition = position;
        _positionText = _referencePositionText = UiNumbers.Format(position.Millimeters);
        _referenceValue = value;
        _valueText = _referenceValueText = UiNumbers.Format(value);
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
    public string Unit => Kind == PointLoadKind.Force ? "N" : "Nm";
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
        _referencePositionText = UiNumbers.Format(position.Millimeters);
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
        _positionError = UiNumbers.TryParsePosition(PositionText, document.Length, out var position)
            ? null : Strings.PositionInsideBeam;
        if (_positionError is null)
            _inputPosition = PositionText == _referencePositionText ? _referencePosition : position;
        _valueError = UiNumbers.TryParseSignedValue(ValueText, out double value) ? null : Strings.InvalidLoadValue;
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
