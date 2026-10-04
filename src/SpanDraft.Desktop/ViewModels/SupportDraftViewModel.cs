using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.State;

namespace SpanDraft.Desktop.ViewModels;

public sealed record SupportTypeOption(SupportType Type)
{
    public string Name => Type switch
    {
        SupportType.Fixed => Strings.FixedSupport,
        SupportType.Pinned => Strings.PinnedSupport,
        SupportType.Roller => Strings.RollerSupport,
        _ => throw new ArgumentOutOfRangeException(nameof(Type))
    };
}

/// <summary>A transactional buffer; changing it never changes the committed document.</summary>
public sealed class SupportDraftViewModel : ObservableObject
{
    private readonly Func<EditorDocument> _read;
    private Length _bufferReferencePosition;
    private string _bufferReferenceText;
    private Length _inputPosition;
    private string _positionText;
    private SupportType _type;
    private string? _errorText;
    private SupportPreview _preview;

    public SupportDraftViewModel(Func<EditorDocument> read, Guid? originalId, SupportType type, Length position)
    {
        _read = read;
        OriginalId = originalId;
        _type = type;
        _bufferReferencePosition = position;
        _bufferReferenceText = UiNumbers.Format(position.Millimeters);
        _positionText = _bufferReferenceText;
        _preview = new(position, type);
        Validate();
    }

    public static IReadOnlyList<SupportTypeOption> Types { get; } = Array.AsReadOnly(new[]
    {
        new SupportTypeOption(SupportType.Fixed), new(SupportType.Pinned), new(SupportType.Roller)
    });
    public Guid? OriginalId { get; }
    public bool IsExisting => OriginalId.HasValue;
    public string PositionText
    {
        get => _positionText;
        set { if (Set(ref _positionText, value)) Validate(); }
    }
    public SupportType Type
    {
        get => _type;
        set { if (Set(ref _type, value)) { Notify(nameof(SelectedType)); Validate(); } }
    }
    public SupportTypeOption? SelectedType
    {
        get => Types.FirstOrDefault(t => t.Type == Type);
        set { if (value is not null) Type = value.Type; }
    }
    public string? ErrorText => _errorText;
    public bool HasError => _errorText is not null;
    public bool IsValid => !HasError;
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
        _bufferReferenceText = UiNumbers.Format(position.Millimeters);
        _preview = _preview with { Position = position };
        Set(ref _positionText, _bufferReferenceText, nameof(PositionText));
        Validate();
        Notify(nameof(CanvasPosition));
    }

    private void Validate()
    {
        var document = _read();
        string? error = null;
        if (!Enum.IsDefined(Type)) error = Strings.InvalidSupportType;
        else if (!UiNumbers.TryParsePosition(PositionText, document.Length, out var position))
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
        // Text is a commit buffer, never a movement control. Only pointer interaction
        // can move this preview; a valid type may still be visualized immediately.
        _preview = _preview with { Type = Enum.IsDefined(Type) ? Type : _preview.Type, IsInvalid = error is not null };
        Notify(nameof(ErrorText));
        Notify(nameof(HasError));
        Notify(nameof(IsValid));
        Notify(nameof(Preview));
    }
}
