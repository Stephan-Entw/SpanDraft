using SpanDraft.Core.Units;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Reporting.Notation;

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
        _referenceValue = value;
        _valueText = _referenceValueText = InputQuantityFormatter.FormatExact(value, _valueUnit);
        _preview = new(position, kind, value);
        var entity = originalId is null ? null : document.Loads.FirstOrDefault(l => l.Id == originalId);
        Reference = new(read, entity?.EffectiveReference, entity?.Position ?? position, _positionUnit);
        // A move that happened before the draft opened (a drag of an existing load) is applied to the reference first.
        Reference.PositionChanged(position);
        _referencePosition = Reference.Distance;
        _positionText = _referencePositionText = InputQuantityFormatter.FormatExact(Reference.Distance.Meters, _positionUnit);
        Reference.DistanceChanged += ShowReferenceDistance;
        Validate();
    }

    // The distance shown follows a choice of the reference and a pointer move, never what the user is typing.
    private void ShowReferenceDistance()
    {
        _referencePosition = Reference.Distance;
        _referencePositionText = InputQuantityFormatter.FormatExact(Reference.Distance.Meters, _positionUnit);
        Set(ref _positionText, _referencePositionText, nameof(PositionText));
        Notify(nameof(PositionLabel));
        Validate();
    }

    /// <summary>What the position is measured from, and whether it is bound to the end of a segment.</summary>
    public PositionReferenceDraft Reference { get; }

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
    /// <summary>The label names what the field holds: the position, or the distance once it is measured from a segment.</summary>
    public TechnicalText PositionLabel => Reference.IsSegmentBased ? (TechnicalText)Strings.PositionDistance
        : Strings.Position + " " + TechnicalSymbols.Axis("x");
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
        _preview = _preview with { Position = position };
        Reference.PositionChanged(position);
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
            NameValidationError.Reserved => Strings.ReservedEntityName,
            _ => null
        };
        // Preserve the exact SI distance when the displayed text is untouched (or restored).
        Length distance = _referencePosition, position = default;
        bool positionValid = PositionText == _referencePositionText
            ? distance.Meters <= Reference.MaxDistance.Meters : Reference.TryParseDistance(PositionText, out distance);
        if (positionValid)
        {
            Reference.TrySetDistance(distance);
            position = Reference.Position;
        }
        _positionError = !positionValid ? Reference.IsSegmentBased ? Strings.DistanceBeyondSegment : Strings.PositionInsideBeam
            : Kind == PointLoadKind.Moment && MomentReleaseRules.MomentConflicts(document, position) ? Strings.MomentAtMomentRelease
            : null;
        if (_positionError is null) _inputPosition = position;
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
