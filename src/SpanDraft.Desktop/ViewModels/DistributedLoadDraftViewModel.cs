using SpanDraft.Core.Units;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.State;

namespace SpanDraft.Desktop.ViewModels;

/// <summary>Transactional range buffers; only endpoint gestures move the canvas range before OK.</summary>
public sealed class DistributedLoadDraftViewModel : ObservableObject
{
    private readonly Func<EditorDocument> _read;
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
        Length start, Length end, double intensity)
    {
        if (!double.IsFinite(intensity)) throw new ArgumentOutOfRangeException(nameof(intensity));
        _read = read;
        OriginalId = originalId;
        AutoCandidate = originalId is null ? EntityNaming.Peek(read(), AutoNameKind.DistributedLoad) : null;
        _nameText = AutoCandidate?.Name ?? read().DistributedLoads.First(l => l.Id == originalId).Name;
        _referenceStart = start;
        _referenceEnd = end;
        _startText = _referenceStartText = UiNumbers.Format(start.Millimeters);
        _endText = _referenceEndText = UiNumbers.Format(end.Millimeters);
        _referenceIntensity = intensity;
        _intensityText = _referenceIntensityText = UiNumbers.Format(intensity);
        _preview = new(start, end, intensity);
        Validate();
    }

    public Guid? OriginalId { get; }
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
            _referenceStartText = UiNumbers.Format(position.Millimeters);
            _preview = _preview with { StartPosition = position };
            Set(ref _startText, _referenceStartText, nameof(StartText));
        }
        else
        {
            _referenceEnd = position;
            _referenceEndText = UiNumbers.Format(position.Millimeters);
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
        _startError = UiNumbers.TryParsePosition(StartText, document.Length, out var start) ? null : Strings.PositionInsideBeam;
        _endError = UiNumbers.TryParsePosition(EndText, document.Length, out var end) ? null : Strings.PositionInsideBeam;
        if (_startError is null) _inputStart = StartText == _referenceStartText ? _referenceStart : start;
        if (_endError is null) _inputEnd = EndText == _referenceEndText ? _referenceEnd : end;
        if (_startError is null && _endError is null && _inputStart.Meters >= _inputEnd.Meters)
            _startError = _endError = Strings.InvalidDistributedRange;
        _intensityError = UiNumbers.TryParseSignedValue(IntensityText, out double intensity) ? null : Strings.InvalidLoadValue;
        if (_intensityError is null)
        {
            _inputIntensity = IntensityText == _referenceIntensityText ? _referenceIntensity : intensity;
            _preview = _preview with { Intensity = _inputIntensity };
        }
        _preview = _preview with { IsInvalid = !IsValid };
        foreach (string name in new[] { nameof(NameError), nameof(StartError), nameof(EndError), nameof(IntensityError),
            nameof(HasNameError), nameof(HasStartError), nameof(HasEndError), nameof(HasIntensityError), nameof(IsValid), nameof(Preview) }) Notify(name);
    }
}
