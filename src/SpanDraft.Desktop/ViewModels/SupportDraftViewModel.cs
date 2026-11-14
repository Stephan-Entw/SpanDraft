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
        _bufferReferencePosition = position;
        _bufferReferenceText = InputQuantityFormatter.FormatExact(position.Meters, _positionUnit);
        _positionText = _bufferReferenceText;
        _preview = new(position, type, false, null);
        Validate();
    }

    public static IReadOnlyList<SupportTypeOption> Types { get; } = Array.AsReadOnly(new[]
    {
        new SupportTypeOption(SupportType.Fixed), new(SupportType.Pinned), new(SupportType.Roller), new(SupportType.Spring)
    });
    public Guid? OriginalId { get; }
    public string PositionUnit => _positionUnit.Symbol;
    public TechnicalText PositionLabel => Strings.Position + " " + TechnicalSymbols.Axis("x");
    public bool IsExisting => OriginalId.HasValue;
    public AutoNameCandidate? AutoCandidate { get; }
    public string NameText
    {
        get => _nameText;
        set { if (Set(ref _nameText, value)) Validate(); }
    }
    public string? NameError => _nameError;
    public bool HasNameError => NameError is not null;
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
        _bufferReferencePosition = position;
        _bufferReferenceText = InputQuantityFormatter.FormatExact(position.Meters, _positionUnit);
        _preview = _preview with { Position = position };
        Set(ref _positionText, _bufferReferenceText, nameof(PositionText));
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
        string? error = null;
        if (!Enum.IsDefined(Type)) error = Strings.InvalidSupportType;
        else if (!TryPosition(document.Length, out var position))
            error = Strings.PositionInsideBeam;
        else
        {
            // Preserve exact SI coordinates when the displayed text is untouched (or restored).
            if (PositionText == _bufferReferenceText) position = _bufferReferencePosition;
            if (document.Supports.Any(s => s.Id != OriginalId && s.Position == position))
                error = Strings.SupportAlreadyExists;
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

    private bool TryPosition(Length beamLength, out Length position)
    {
        if (PositionText == _bufferReferenceText)
        {
            position = _bufferReferencePosition;
            return position.Meters >= 0 && position.Meters <= beamLength.Meters;
        }
        return InputQuantityFormatter.TryParsePosition(PositionText, _positionUnit, beamLength, out position);
    }
}
