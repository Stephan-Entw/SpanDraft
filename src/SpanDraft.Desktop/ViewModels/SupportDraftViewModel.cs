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
    private readonly Length _initialPosition;
    private readonly string _initialPositionText;
    private string _positionText;
    private SupportType _type;
    private string? _errorText;
    private SupportPreview _preview;

    public SupportDraftViewModel(Func<EditorDocument> read, Guid? originalId, SupportType type, Length position)
    {
        _read = read;
        OriginalId = originalId;
        _type = type;
        _initialPosition = position;
        _initialPositionText = UiNumbers.Format(position.Millimeters);
        _positionText = _initialPositionText;
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

    public bool TryGetValue(out Length position)
    {
        Validate();
        position = _preview.Position;
        return IsValid;
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
            if (PositionText == _initialPositionText) position = _initialPosition;
            if (document.Supports.Any(s => s.Id != OriginalId && s.Position == position))
                error = Strings.SupportAlreadyExists;
            _preview = new(position, Type, error is not null);
        }
        _errorText = error;
        _preview = _preview with { IsInvalid = error is not null };
        Notify(nameof(ErrorText));
        Notify(nameof(HasError));
        Notify(nameof(IsValid));
        Notify(nameof(Preview));
    }
}
