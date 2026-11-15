using SpanDraft.Core.Units;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.State;
using SpanDraft.Reporting.Notation;

namespace SpanDraft.Desktop.ViewModels;

/// <summary>A transactional buffer for a moment release; changing it never changes the committed document.</summary>
public sealed class MomentReleaseDraftViewModel : ObservableObject
{
    private readonly Func<EditorDocument> _read;
    private readonly UnitDefinition _positionUnit;
    private Length _referencePosition;
    private string _referenceText;
    private Length _inputPosition;
    private string _positionText;
    private string _nameText;
    private string? _nameError;
    private string? _positionError;
    private MomentReleasePreview _preview;

    public MomentReleaseDraftViewModel(Func<EditorDocument> read, Guid? originalId, Length position, UnitProfile? profile = null)
    {
        _read = read;
        _positionUnit = (profile ?? UnitProfile.Default)[QuantityKind.BeamLength];
        OriginalId = originalId;
        var document = read();
        AutoCandidate = originalId is null ? EntityNaming.Peek(document, AutoNameKind.MomentRelease) : null;
        _nameText = AutoCandidate?.Name ?? document.MomentReleases.First(r => r.Id == originalId).Name;
        _preview = new(position);
        var entity = originalId is null ? null : document.MomentReleases.FirstOrDefault(r => r.Id == originalId);
        Reference = new(read, entity?.EffectiveReference, entity?.Position ?? position, _positionUnit);
        // A move that happened before the draft opened (a drag of an existing release) is applied to the reference first.
        Reference.PositionChanged(position);
        _referencePosition = Reference.Distance;
        _positionText = _referenceText = InputQuantityFormatter.FormatExact(Reference.Distance.Meters, _positionUnit);
        Reference.DistanceChanged += ShowReferenceDistance;
        Validate();
    }

    // The distance shown follows a choice of the reference and a pointer move, never what the user is typing.
    private void ShowReferenceDistance()
    {
        _referencePosition = Reference.Distance;
        _referenceText = InputQuantityFormatter.FormatExact(Reference.Distance.Meters, _positionUnit);
        Set(ref _positionText, _referenceText, nameof(PositionText));
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
    public bool HasNameError => _nameError is not null;
    public string PositionUnit => _positionUnit.Symbol;
    /// <summary>The label names what the field holds: the position, or the distance once it is measured from a segment.</summary>
    public TechnicalText PositionLabel => Reference.IsSegmentBased ? (TechnicalText)Strings.PositionDistance
        : Strings.Position + " " + TechnicalSymbols.Axis("x");
    public string PositionText
    {
        get => _positionText;
        set { if (Set(ref _positionText, value)) Validate(); }
    }
    public string? ErrorText => _positionError;
    public bool HasError => _positionError is not null;
    public bool IsValid => !HasError && !HasNameError;
    public MomentReleasePreview Preview => _preview;
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
        _nameError = nameError switch
        {
            NameValidationError.Empty => Strings.EmptyEntityName,
            NameValidationError.Duplicate => Strings.DuplicateEntityName,
            NameValidationError.Reserved => Strings.ReservedEntityName,
            _ => null
        };
        // Preserve the exact SI distance while the displayed text is untouched (or restored).
        Length distance = _referencePosition, position = default;
        bool parsed = PositionText == _referenceText
            ? distance.Meters <= Reference.MaxDistance.Meters : Reference.TryParseDistance(PositionText, out distance);
        if (parsed)
        {
            Reference.TrySetDistance(distance);
            position = Reference.Position;
        }
        _positionError = !parsed ? Reference.IsSegmentBased ? Strings.DistanceBeyondSegment : Strings.MomentReleaseInsideBeam
            : MomentReleaseRules.Message(MomentReleaseRules.Conflict(document, position, OriginalId));
        if (parsed) _inputPosition = position;
        // Text is a commit buffer, never a movement control: only pointer interaction moves the preview.
        _preview = _preview with { IsInvalid = !IsValid };
        foreach (string name in new[] { nameof(ErrorText), nameof(HasError), nameof(NameError), nameof(HasNameError),
            nameof(IsValid), nameof(Preview) }) Notify(name);
    }
}
