using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Reporting.Notation;

namespace SpanDraft.Desktop.ViewModels;

public sealed record SupportTypeOption(SupportType Type)
{
    public string Name => Type switch
    {
        SupportType.Fixed => Strings.FixedSupport,
        SupportType.Pinned => Strings.PinnedSupport,
        SupportType.Roller => Strings.RollerSupport,
        SupportType.Spring => Strings.SpringSupport,
        _ => throw new ArgumentOutOfRangeException(nameof(Type))
    };
}

/// <summary>A transactional buffer; changing it never changes the committed document.</summary>
public sealed class SupportDraftViewModel : ObservableObject
{
    private readonly Func<EditorDocument> _read;
    private readonly UnitDefinition _positionUnit, _translationalUnit, _rotationalUnit;
    private bool _hasTranslational, _hasRotational, _rigidTranslation;
    private readonly string _referenceTranslationalText, _referenceRotationalText;
    private readonly double? _referenceTranslational, _referenceRotational;
    private string _translationalText, _rotationalText;
    private string? _translationalError, _rotationalError, _springError;
    private SpringData? _spring;
    private Length _bufferReferencePosition;
    private string _bufferReferenceText;
    private Length _inputPosition;
    private string _positionText;
    private string _nameText;
    private string? _nameError;
    private SupportType _type;
    private string? _errorText;
    private SupportPreview _preview;

    public SupportDraftViewModel(Func<EditorDocument> read, Guid? originalId, SupportType type, Length position,
        UnitProfile? profile = null, SpringData? spring = null)
    {
        _read = read;
        profile ??= UnitProfile.Default;
        _positionUnit = profile[QuantityKind.BeamLength];
        _translationalUnit = profile[QuantityKind.TranslationalStiffness];
        _rotationalUnit = profile[QuantityKind.RotationalStiffness];
        // A new spring support starts with a translational spring whose stiffness the user has to give.
        bool fresh = type == SupportType.Spring && spring is null;
        _hasTranslational = fresh || spring?.Translational is not null;
        _hasRotational = spring?.Rotational is not null;
        _rigidTranslation = spring?.RigidTranslation == true;
        _referenceTranslational = spring?.Translational?.NewtonsPerMeter;
        _referenceRotational = spring?.Rotational?.NewtonMetersPerRadian;
        _referenceTranslationalText = _translationalText = _referenceTranslational is { } w
            ? InputQuantityFormatter.FormatExact(w, _translationalUnit) : "";
        _referenceRotationalText = _rotationalText = _referenceRotational is { } t
            ? InputQuantityFormatter.FormatExact(t, _rotationalUnit) : "";
        OriginalId = originalId;
        var document = read();
        AutoCandidate = originalId is null ? EntityNaming.Peek(document, AutoNameKind.Support) : null;
        _nameText = AutoCandidate?.Name ?? document.Supports.First(s => s.Id == originalId).Name;
        _type = type;
        _preview = new(position, type, false, null);
        var entity = originalId is null ? null : document.Supports.FirstOrDefault(s => s.Id == originalId);
        Reference = new(read, entity?.EffectiveReference, entity?.Position ?? position, _positionUnit);
        // A move that happened before the draft opened (a drag of an existing support) is applied to the reference first.
        Reference.PositionChanged(position);
        _bufferReferencePosition = Reference.Distance;
        _bufferReferenceText = InputQuantityFormatter.FormatExact(Reference.Distance.Meters, _positionUnit);
        _positionText = _bufferReferenceText;
        Reference.DistanceChanged += ShowReferenceDistance;
        Validate();
    }

    // The distance shown follows a choice of the reference and a pointer move, never what the user is typing.
    private void ShowReferenceDistance()
    {
        _bufferReferencePosition = Reference.Distance;
        _bufferReferenceText = InputQuantityFormatter.FormatExact(Reference.Distance.Meters, _positionUnit);
        Set(ref _positionText, _bufferReferenceText, nameof(PositionText));
        Notify(nameof(PositionLabel));
        Validate();
    }

    /// <summary>What the position is measured from, and whether it is bound to the end of a segment.</summary>
    public PositionReferenceDraft Reference { get; }

    public static IReadOnlyList<SupportTypeOption> Types { get; } = Array.AsReadOnly(new[]
    {
        new SupportTypeOption(SupportType.Fixed), new(SupportType.Pinned), new(SupportType.Roller), new(SupportType.Spring)
    });
    public Guid? OriginalId { get; }
    public string PositionUnit => _positionUnit.Symbol;
    /// <summary>The label names what the field holds: the position, or the distance once it is measured from a segment.</summary>
    public TechnicalText PositionLabel => Reference.IsSegmentBased ? (TechnicalText)Strings.PositionDistance
        : Strings.Position + " " + TechnicalSymbols.Axis("x");
    public bool IsExisting => OriginalId.HasValue;
    public AutoNameCandidate? AutoCandidate { get; }
    public string NameText
    {
        get => _nameText;
        set { if (Set(ref _nameText, value)) Validate(); }
    }
    public string? NameError => _nameError;
    public bool HasNameError => NameError is not null;
    /// <summary>The distance from the reference the flyout shows: the position itself while it is measured from the beam start.</summary>
    public string PositionText
    {
        get => _positionText;
        set { if (Set(ref _positionText, value)) Validate(); }
    }
    public SupportType Type
    {
        get => _type;
        set
        {
            if (!Set(ref _type, value)) return;
            // Switching to a spring support offers a translational spring first, as for a freshly placed one.
            if (value == SupportType.Spring && !_hasTranslational && !_hasRotational)
            {
                _hasTranslational = true;
                Notify(nameof(HasTranslational));
                Notify(nameof(CanRigidTranslation));
            }
            Notify(nameof(SelectedType));
            Notify(nameof(IsSpring));
            Validate();
        }
    }
    public SupportTypeOption? SelectedType
    {
        get => Types.FirstOrDefault(t => t.Type == Type);
        set { if (value is not null) Type = value.Type; }
    }
    public bool IsSpring => Type == SupportType.Spring;
    public string TranslationalUnit => _translationalUnit.Symbol;
    public string RotationalUnit => _rotationalUnit.Symbol;
    public bool HasTranslational
    {
        get => _hasTranslational;
        set
        {
            if (!Set(ref _hasTranslational, value)) return;
            // A translational spring replaces the rigid translation of a partial restraint.
            if (value && _rigidTranslation) { _rigidTranslation = false; Notify(nameof(RigidTranslation)); }
            Notify(nameof(CanRigidTranslation));
            Validate();
        }
    }
    public bool HasRotational
    {
        get => _hasRotational;
        set
        {
            if (!Set(ref _hasRotational, value)) return;
            if (!value && _rigidTranslation) { _rigidTranslation = false; Notify(nameof(RigidTranslation)); }
            Notify(nameof(CanRigidTranslation));
            Validate();
        }
    }
    public bool RigidTranslation
    {
        get => _rigidTranslation;
        set { if (value ? CanRigidTranslation : true) { if (Set(ref _rigidTranslation, value)) Validate(); } }
    }
    /// <summary>The translation is rigid only in combination with a rotational spring and without a translational one.</summary>
    public bool CanRigidTranslation => _hasRotational && !_hasTranslational;
    public string TranslationalText
    {
        get => _translationalText;
        set { if (Set(ref _translationalText, value)) Validate(); }
    }
    public string RotationalText
    {
        get => _rotationalText;
        set { if (Set(ref _rotationalText, value)) Validate(); }
    }
    public string? TranslationalError => _translationalError;
    public bool HasTranslationalError => _translationalError is not null;
    public string? RotationalError => _rotationalError;
    public bool HasRotationalError => _rotationalError is not null;
    public string? SpringError => _springError;
    public bool HasSpringError => _springError is not null;

    /// <summary>The validated springs of a spring support; null for other types and while the input is invalid.</summary>
    public SpringData? Spring => IsSpring && IsValid ? _spring : null;
    public string? ErrorText => _errorText;
    public bool HasError => _errorText is not null;
    public bool IsValid => !HasError && !HasNameError && !HasTranslationalError && !HasRotationalError && !HasSpringError;
    public SupportPreview Preview => _preview;
    public Length CanvasPosition => Preview.Position;

    public bool TryGetValue(out Length position)
    {
        Validate();
        position = _inputPosition;
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
        _nameError = EntityNaming.Message(nameError);
        string? error = null;
        if (!Enum.IsDefined(Type)) error = Strings.InvalidSupportType;
        else if (!TryDistance(out var distance))
            error = Reference.IsSegmentBased ? Strings.DistanceBeyondSegment : Strings.PositionInsideBeam;
        else
        {
            Reference.TrySetDistance(distance);
            var position = Reference.Position;
            if (document.Supports.Any(s => s.Id != OriginalId && s.Position == position))
                error = Strings.SupportAlreadyExists;
            else if (MomentReleaseRules.SupportConflicts(document, position, Type, IsSpring && _hasRotational ? PreviewSpring() : null))
                error = Strings.SupportAtMomentRelease;
            _inputPosition = position;
        }
        _errorText = error;
        ValidateSpring();
        // Text is a commit buffer, never a movement control. Only pointer interaction
        // can move this preview; a valid type may still be visualized immediately.
        _preview = _preview with
        {
            Type = Enum.IsDefined(Type) ? Type : _preview.Type, IsInvalid = !IsValid,
            Spring = IsSpring ? PreviewSpring() : null
        };
        Notify(nameof(NameError));
        Notify(nameof(HasNameError));
        foreach (string name in new[] { nameof(TranslationalError), nameof(HasTranslationalError), nameof(RotationalError),
            nameof(HasRotationalError), nameof(SpringError), nameof(HasSpringError), nameof(Spring) }) Notify(name);
        Notify(nameof(ErrorText));
        Notify(nameof(HasError));
        Notify(nameof(IsValid));
        Notify(nameof(Preview));
    }

    private void ValidateSpring()
    {
        _translationalError = _rotationalError = _springError = null;
        _spring = null;
        if (!IsSpring) return;
        TranslationalStiffness? translational = null;
        RotationalStiffness? rotational = null;
        if (_hasTranslational && TryStiffness(_translationalText, _referenceTranslationalText, _referenceTranslational,
            _translationalUnit, out double w)) translational = TranslationalStiffness.FromNewtonsPerMeter(w);
        else if (_hasTranslational) _translationalError = Strings.InvalidStiffness;
        if (_hasRotational && TryStiffness(_rotationalText, _referenceRotationalText, _referenceRotational,
            _rotationalUnit, out double t)) rotational = RotationalStiffness.FromNewtonMetersPerRadian(t);
        else if (_hasRotational) _rotationalError = Strings.InvalidStiffness;
        if (!_hasTranslational && !_hasRotational) _springError = Strings.SpringRequired;
        if (IsValidInput()) _spring = new(translational, rotational, _rigidTranslation);

        bool IsValidInput() => _translationalError is null && _rotationalError is null && _springError is null;
    }

    /// <summary>The symbol follows the chosen springs while their values are still being typed.</summary>
    private SpringData PreviewSpring() => _spring ?? new(
        _hasTranslational ? TranslationalStiffness.FromNewtonsPerMeter(1) : null,
        _hasRotational ? RotationalStiffness.FromNewtonMetersPerRadian(1) : null,
        _rigidTranslation);

    private static bool TryStiffness(string text, string referenceText, double? reference, UnitDefinition unit, out double value)
    {
        if (reference is { } exact && text == referenceText) { value = exact; return true; }
        return InputQuantityFormatter.TryParse(text, unit, out value) && value > 0;
    }

    // Preserve the exact SI distance when the displayed text is untouched (or restored).
    private bool TryDistance(out Length distance)
    {
        if (PositionText == _bufferReferenceText)
        {
            distance = _bufferReferencePosition;
            return distance.Meters <= Reference.MaxDistance.Meters;
        }
        return Reference.TryParseDistance(PositionText, out distance);
    }
}
