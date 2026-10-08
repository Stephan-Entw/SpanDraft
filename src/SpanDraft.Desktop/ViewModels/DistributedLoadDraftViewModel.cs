using SpanDraft.Core.Units;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.Presentation;

namespace SpanDraft.Desktop.ViewModels;

/// <summary>Transactional range buffers; only endpoint gestures move the canvas range before OK.</summary>
public sealed class DistributedLoadDraftViewModel : ObservableObject
{
    private readonly Func<EditorDocument> _read;
    private readonly UnitDefinition _positionUnit, _intensityUnit;
    private Length _referenceStart, _referenceEnd;
    private string _referenceStartText, _referenceEndText;
    private readonly double _referenceIntensity;
    private readonly string _referenceIntensityText;
    private string _startText, _endText, _intensityText, _nameText;
    private Length _inputStart, _inputEnd;
    private double _inputIntensity;
    private string? _startError, _endError, _intensityError, _nameError;
    private DistributedLoadPreview _preview;

    public DistributedLoadDraftViewModel(Func<EditorDocument> read, Guid? originalId,
        Length start, Length end, double intensity, UnitProfile? profile = null)
    {
        if (!double.IsFinite(intensity)) throw new ArgumentOutOfRangeException(nameof(intensity));
        _read = read;
        profile ??= UnitProfile.Default;
        _positionUnit = profile[QuantityKind.BeamLength];
        _intensityUnit = profile[QuantityKind.DistributedLoad];
        OriginalId = originalId;
        AutoCandidate = originalId is null ? EntityNaming.Peek(read(), AutoNameKind.DistributedLoad) : null;
        _nameText = AutoCandidate?.Name ?? read().DistributedLoads.First(l => l.Id == originalId).Name;
        _referenceStart = start;
        _referenceEnd = end;
        _startText = _referenceStartText = InputQuantityFormatter.Format(start.Meters, _positionUnit);
        _endText = _referenceEndText = InputQuantityFormatter.Format(end.Meters, _positionUnit);
        _referenceIntensity = intensity;
        _intensityText = _referenceIntensityText = InputQuantityFormatter.Format(intensity, _intensityUnit);
        _preview = new(start, end, intensity);
        Validate();
    }

    public Guid? OriginalId { get; }
    public string PositionUnit => _positionUnit.Symbol;
    public string IntensityUnit => _intensityUnit.Symbol;
    public bool IsExisting => OriginalId.HasValue;
    public AutoNameCandidate? AutoCandidate { get; }
    public string NameText { get => _nameText; set { if (Set(ref _nameText, value)) Validate(); } }
    public string StartText { get => _startText; set { if (Set(ref _startText, value)) Validate(); } }
    public string EndText { get => _endText; set { if (Set(ref _endText, value)) Validate(); } }
    public string IntensityText { get => _intensityText; set { if (Set(ref _intensityText, value)) Validate(); } }
    public string? NameError => _nameError;
    public string? StartError => _startError;
    public string? EndError => _endError;
    public string? IntensityError => _intensityError;
    public bool HasNameError => NameError is not null;
    public bool HasStartError => StartError is not null;
    public bool HasEndError => EndError is not null;
    public bool HasIntensityError => IntensityError is not null;
    public bool IsValid => !HasNameError && !HasStartError && !HasEndError && !HasIntensityError;
    public DistributedLoadPreview Preview => _preview;

    public bool TryGetValues(out Length start, out Length end, out double intensity)
    {
        Validate();
        start = _inputStart;
        end = _inputEnd;
        intensity = _inputIntensity;
        return IsValid;
    }

    internal void ApplyDragEndpoint(DistributedLoadEndpoint endpoint, Length position)
    {
        if (endpoint == DistributedLoadEndpoint.Start)
        {
            _referenceStart = position;
            _referenceStartText = InputQuantityFormatter.Format(position.Meters, _positionUnit);
            _preview = _preview with { StartPosition = position };
            Set(ref _startText, _referenceStartText, nameof(StartText));
        }
        else
        {
            _referenceEnd = position;
            _referenceEndText = InputQuantityFormatter.Format(position.Meters, _positionUnit);
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
            _ => null
        };
        _startError = TryPosition(StartText, _referenceStartText, _referenceStart, document.Length, out var start) ? null : Strings.PositionInsideBeam;
        _endError = TryPosition(EndText, _referenceEndText, _referenceEnd, document.Length, out var end) ? null : Strings.PositionInsideBeam;
        if (_startError is null) _inputStart = StartText == _referenceStartText ? _referenceStart : start;
        if (_endError is null) _inputEnd = EndText == _referenceEndText ? _referenceEnd : end;
        if (_startError is null && _endError is null && _inputStart.Meters >= _inputEnd.Meters)
            _startError = _endError = Strings.InvalidDistributedRange;
        double intensity = _referenceIntensity;
        _intensityError = IntensityText == _referenceIntensityText || InputQuantityFormatter.TryParse(IntensityText, _intensityUnit, out intensity)
            ? null : Strings.InvalidLoadValue;
        if (_intensityError is null)
        {
            _inputIntensity = IntensityText == _referenceIntensityText ? _referenceIntensity : intensity;
            _preview = _preview with { Intensity = _inputIntensity };
        }
        _preview = _preview with { IsInvalid = !IsValid };
        foreach (string name in new[] { nameof(NameError), nameof(StartError), nameof(EndError), nameof(IntensityError),
            nameof(HasNameError), nameof(HasStartError), nameof(HasEndError), nameof(HasIntensityError), nameof(IsValid), nameof(Preview) }) Notify(name);
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
