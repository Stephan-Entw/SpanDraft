using SpanDraft.Core.Units;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Reporting.Notation;

namespace SpanDraft.Desktop.ViewModels;

/// <summary>Transactional range buffers; only endpoint gestures move the canvas range before OK.</summary>
public sealed class DistributedLoadDraftViewModel : ObservableObject
{
    private readonly Func<EditorDocument> _read;
    private readonly UnitDefinition _positionUnit, _intensityUnit;
    private Length _referenceStart, _referenceEnd;
    private string _referenceStartText, _referenceEndText;
    private readonly double _referenceStartIntensity, _referenceEndIntensity;
    private readonly string _referenceStartIntensityText, _referenceEndIntensityText;
    private string _startText, _endText, _startIntensityText, _endIntensityText, _nameText;
    private Length _inputStart, _inputEnd;
    private double _inputStartIntensity, _inputEndIntensity;
    private string? _startError, _endError, _startIntensityError, _endIntensityError, _nameError;
    private bool _endIntensityFollowsStart;
    private DistributedLoadPreview _preview;

    /// <summary>Opens a constant load.</summary>
    public DistributedLoadDraftViewModel(Func<EditorDocument> read, Guid? originalId,
        Length start, Length end, double intensity, UnitProfile? profile = null)
        : this(read, originalId, start, end, intensity, intensity, profile)
    {
    }

    public DistributedLoadDraftViewModel(Func<EditorDocument> read, Guid? originalId,
        Length start, Length end, double startIntensity, double endIntensity, UnitProfile? profile = null)
    {
        if (!double.IsFinite(startIntensity)) throw new ArgumentOutOfRangeException(nameof(startIntensity));
        if (!double.IsFinite(endIntensity)) throw new ArgumentOutOfRangeException(nameof(endIntensity));
        _read = read;
        profile ??= UnitProfile.Default;
        _positionUnit = profile[QuantityKind.BeamLength];
        _intensityUnit = profile[QuantityKind.DistributedLoad];
        OriginalId = originalId;
        AutoCandidate = originalId is null ? EntityNaming.Peek(read(), AutoNameKind.DistributedLoad) : null;
        _nameText = AutoCandidate?.Name ?? read().DistributedLoads.First(l => l.Id == originalId).Name;
        _referenceStart = start;
        _referenceEnd = end;
        _startText = _referenceStartText = InputQuantityFormatter.FormatExact(start.Meters, _positionUnit);
        _endText = _referenceEndText = InputQuantityFormatter.FormatExact(end.Meters, _positionUnit);
        _referenceStartIntensity = startIntensity;
        _referenceEndIntensity = endIntensity;
        _startIntensityText = _referenceStartIntensityText = InputQuantityFormatter.FormatExact(startIntensity, _intensityUnit);
        _endIntensityText = _referenceEndIntensityText = InputQuantityFormatter.FormatExact(endIntensity, _intensityUnit);
        // A constant load stays constant while the end intensity is untouched; any edit of it ends the following.
        _endIntensityFollowsStart = startIntensity == endIntensity;
        _preview = new(start, end, startIntensity, endIntensity);
        Validate();
    }

    public Guid? OriginalId { get; }
    public string PositionUnit => _positionUnit.Symbol;
    public TechnicalText StartLabel => Strings.DistributedStartLabel + " " + TechnicalSymbols.Axis("x");
    public TechnicalText EndLabel => Strings.DistributedEndLabel + " " + TechnicalSymbols.Axis("x");
    public string IntensityUnit => _intensityUnit.Symbol;
    public bool IsExisting => OriginalId.HasValue;
    public AutoNameCandidate? AutoCandidate { get; }
    public string NameText { get => _nameText; set { if (Set(ref _nameText, value)) Validate(); } }
    public string StartText { get => _startText; set { if (Set(ref _startText, value)) Validate(); } }
    public string EndText { get => _endText; set { if (Set(ref _endText, value)) Validate(); } }
    public string StartIntensityText
    {
        get => _startIntensityText;
        set
        {
            if (!Set(ref _startIntensityText, value)) return;
            if (_endIntensityFollowsStart) Set(ref _endIntensityText, value, nameof(EndIntensityText));
            Validate();
        }
    }

    public string EndIntensityText
    {
        get => _endIntensityText;
        set
        {
            _endIntensityFollowsStart = false;
            if (Set(ref _endIntensityText, value)) Validate();
        }
    }
    public string? NameError => _nameError;
    public string? StartError => _startError;
    public string? EndError => _endError;
    public string? StartIntensityError => _startIntensityError;
    public string? EndIntensityError => _endIntensityError;
    public bool HasNameError => NameError is not null;
    public bool HasStartError => StartError is not null;
    public bool HasEndError => EndError is not null;
    public bool HasStartIntensityError => StartIntensityError is not null;
    public bool HasEndIntensityError => EndIntensityError is not null;
    public bool IsValid => !HasNameError && !HasStartError && !HasEndError && !HasStartIntensityError && !HasEndIntensityError;
    public DistributedLoadPreview Preview => _preview;

    public bool TryGetValues(out Length start, out Length end, out double startIntensity, out double endIntensity)
    {
        Validate();
        start = _inputStart;
        end = _inputEnd;
        startIntensity = _inputStartIntensity;
        endIntensity = _inputEndIntensity;
        return IsValid;
    }

    internal void ApplyDragEndpoint(DistributedLoadEndpoint endpoint, Length position)
    {
        if (endpoint == DistributedLoadEndpoint.Start)
        {
            _referenceStart = position;
            _referenceStartText = InputQuantityFormatter.FormatExact(position.Meters, _positionUnit);
            _preview = _preview with { StartPosition = position };
            Set(ref _startText, _referenceStartText, nameof(StartText));
        }
        else
        {
            _referenceEnd = position;
            _referenceEndText = InputQuantityFormatter.FormatExact(position.Meters, _positionUnit);
            _preview = _preview with { EndPosition = position };
            Set(ref _endText, _referenceEndText, nameof(EndText));
        }
        Validate();
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
        _startError = TryPosition(StartText, _referenceStartText, _referenceStart, document.Length, out var start) ? null : Strings.PositionInsideBeam;
        _endError = TryPosition(EndText, _referenceEndText, _referenceEnd, document.Length, out var end) ? null : Strings.PositionInsideBeam;
        if (_startError is null) _inputStart = StartText == _referenceStartText ? _referenceStart : start;
        if (_endError is null) _inputEnd = EndText == _referenceEndText ? _referenceEnd : end;
        if (_startError is null && _endError is null && _inputStart.Meters >= _inputEnd.Meters)
            _startError = _endError = Strings.InvalidDistributedRange;
        _startIntensityError = TryIntensity(StartIntensityText, _referenceStartIntensityText, _referenceStartIntensity, out double startIntensity)
            ? null : Strings.InvalidLoadValue;
        // A following end intensity shows the start intensity's error only once, at the start field.
        double endIntensity = _referenceEndIntensity;
        _endIntensityError = _endIntensityFollowsStart && _startIntensityError is not null ||
            TryIntensity(EndIntensityText, _referenceEndIntensityText, _referenceEndIntensity, out endIntensity)
            ? null : Strings.InvalidLoadValue;
        if (_startIntensityError is null)
        {
            _inputStartIntensity = startIntensity;
            _preview = _preview with { StartIntensity = startIntensity };
        }
        if (_endIntensityError is null && !(_endIntensityFollowsStart && _startIntensityError is not null))
        {
            _inputEndIntensity = endIntensity;
            _preview = _preview with { EndIntensity = endIntensity };
        }
        _preview = _preview with { IsInvalid = !IsValid };
        foreach (string name in new[] { nameof(NameError), nameof(StartError), nameof(EndError), nameof(StartIntensityError),
            nameof(EndIntensityError), nameof(HasNameError), nameof(HasStartError), nameof(HasEndError),
            nameof(HasStartIntensityError), nameof(HasEndIntensityError), nameof(IsValid), nameof(Preview) }) Notify(name);
    }

    private bool TryIntensity(string text, string referenceText, double reference, out double intensity)
    {
        if (text == referenceText)
        {
            intensity = reference;
            return true;
        }
        return InputQuantityFormatter.TryParse(text, _intensityUnit, out intensity);
    }

    private bool TryPosition(string text, string referenceText, Length reference, Length beamLength, out Length position)
    {
        if (text == referenceText)
        {
            position = reference;
            return position.Meters >= 0 && position.Meters <= beamLength.Meters;
        }
        return InputQuantityFormatter.TryParsePosition(text, _positionUnit, beamLength, out position);
    }
}
