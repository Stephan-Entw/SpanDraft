using System.ComponentModel;
using System.Globalization;
using SpanDraft.Analysis;
using SpanDraft.Core.Beams;
using SpanDraft.Core.Materials;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.State;

namespace SpanDraft.Desktop.ViewModels;

public sealed class EditorViewModel : ObservableObject
{
    private readonly Func<BeamModel, BeamAnalysisOutcome> _analyze;
    private EditorDocument _document;
    private AnalysisPresentationState _presentation;
    private SupportInteraction _interaction;
    private SupportType? _placementTool;
    private SupportDraftViewModel? _draft;
    private SupportPreview? _preview;
    private Guid? _hoveredSupportId;
    private EditorSupport? _dragSupport;
    private bool _dragValid;
    private string? _supportFeedback;
    private ConstraintConflictState? _constraintConflict;

    public EditorViewModel(EditorDocument document, Action changeProject,
        Func<BeamModel, BeamAnalysisOutcome>? analyze = null)
    {
        _document = document;
        _analyze = analyze ?? BeamAnalysis.Analyze;
        _presentation = AnalysisPresentationState.FromOutcome(_analyze(document.ToBeamModel()));
        DimensionLength = new(() => Document.Length, ChangeLength);
        DimensionLength.BufferChanged += text =>
        {
            if (ConstraintConflict is not null)
                UpdateConstraintConflict(UiNumbers.TryParseLength(text, out var length) ? length : null);
        };
        DimensionLength.EditCancelled += () => UpdateConstraintConflict(null);
        ChangeProjectCommand = new(() => { CancelSupportInteraction(); changeProject(); });
        FixedToolCommand = new(() => ToggleSupportTool(SupportType.Fixed));
        PinnedToolCommand = new(() => ToggleSupportTool(SupportType.Pinned));
        RollerToolCommand = new(() => ToggleSupportTool(SupportType.Roller));
        ConfirmSupportCommand = new(() => ConfirmSupport());
        CancelSupportCommand = new(CancelSupportInteraction);
        DeleteSupportCommand = new(DeleteSupport);
    }

    public EditorDocument Document => _document;
    public AnalysisPresentationState Presentation => _presentation;
    public string ProjectInfo => Strings.SectionTemplateName + " · " + Document.Material.Name;
    public LengthInputViewModel DimensionLength { get; }
    public ConstraintConflictState? ConstraintConflict => _constraintConflict;
    public IReadOnlyList<Guid> ConflictEntityIds => ConstraintConflict?.BlockingEntityIds ?? Array.Empty<Guid>();
    public ActionCommand ChangeProjectCommand { get; }
    public ActionCommand FixedToolCommand { get; }
    public ActionCommand PinnedToolCommand { get; }
    public ActionCommand RollerToolCommand { get; }
    public ActionCommand ConfirmSupportCommand { get; }
    public ActionCommand CancelSupportCommand { get; }
    public ActionCommand DeleteSupportCommand { get; }
    public SupportInteraction Interaction => _interaction;
    public SupportType? PlacementTool => _placementTool;
    public bool IsFixedTool => PlacementTool == SupportType.Fixed;
    public bool IsPinnedTool => PlacementTool == SupportType.Pinned;
    public bool IsRollerTool => PlacementTool == SupportType.Roller;
    public SupportDraftViewModel? SupportDraft => _draft;
    public bool IsSupportFlyoutVisible => _draft is not null && Interaction is SupportInteraction.NewDraft or SupportInteraction.EditDraft;
    public SupportPreview? Preview => _preview;
    public Guid? HoveredSupportId => _hoveredSupportId;
    public Guid? HiddenSupportId => _draft?.OriginalId ?? _dragSupport?.Id;
    public string? SupportFeedback => _supportFeedback;
    public bool HasSupportFeedback => !string.IsNullOrEmpty(SupportFeedback);
    public bool HasCoordinate => Preview is not null;
    public string CoordinateText => Preview is { } p
        ? string.Format(CultureInfo.CurrentUICulture, Strings.SupportCoordinate, UiNumbers.Format(p.Position.Millimeters)) : "";

    public void ToggleSupportTool(SupportType type)
    {
        if (!Enum.IsDefined(type)) throw new ArgumentOutOfRangeException(nameof(type));
        bool deactivate = PlacementTool == type;
        CancelSupportInteraction();
        if (deactivate) return;
        _placementTool = type;
        _interaction = SupportInteraction.Placement;
        NotifySupportState();
    }

    public void HoverPlacement(Length? position)
    {
        if (Interaction != SupportInteraction.Placement) return;
        _preview = position is { } p ? new(p, PlacementTool!.Value, !CanPosition(p)) : null;
        _supportFeedback = _preview?.IsInvalid == true ? PositionError(position!.Value) : null;
        NotifySupportState();
    }

    public bool PlaceSupport()
    {
        if (Interaction != SupportInteraction.Placement || Preview is not { IsInvalid: false } preview
            || !CanPosition(preview.Position)) return false;
        OpenDraft(null, preview.Type, preview.Position);
        return true;
    }

    public void HoverSupport(Guid? id)
    {
        if (Interaction != SupportInteraction.Neutral) return;
        if (_hoveredSupportId == id) return;
        _hoveredSupportId = id;
        _supportFeedback = null;
        NotifySupportState();
    }

    public bool EditSupport(Guid id)
    {
        if (Interaction == SupportInteraction.EditDraft && _draft?.OriginalId == id) return true;
        if (Interaction != SupportInteraction.Neutral) return false;
        var support = Document.Supports.FirstOrDefault(s => s.Id == id);
        if (support is null) return false;
        OpenDraft(support.Id, support.Type, support.Position);
        return true;
    }

    public bool BeginSupportDrag(Guid id)
    {
        bool editing = Interaction == SupportInteraction.EditDraft && _draft?.OriginalId == id;
        if (Interaction != SupportInteraction.Neutral && !editing) return false;
        var support = Document.Supports.FirstOrDefault(s => s.Id == id);
        if (support is null) return false;
        if (editing) support = support with { Position = _draft!.CanvasPosition, Type = _draft.Preview.Type };
        _dragSupport = support;
        _interaction = SupportInteraction.Drag;
        _hoveredSupportId = null;
        UpdateSupportDrag(support.Position);
        return true;
    }

    public void UpdateSupportDrag(Length? position)
    {
        if (Interaction != SupportInteraction.Drag || _dragSupport is null) return;
        _dragValid = position is { } p && CanPosition(p, _dragSupport.Id);
        _supportFeedback = _dragValid ? null : position is { } invalid
            ? PositionError(invalid, _dragSupport.Id) : Strings.PositionInsideBeam;
        _preview = new(position ?? _preview?.Position ?? _dragSupport.Position, _dragSupport.Type, !_dragValid);
        NotifySupportState();
    }

    public bool EndSupportDrag()
    {
        if (Interaction != SupportInteraction.Drag || _dragSupport is null) return false;
        if (!_dragValid)
        {
            var feedback = _supportFeedback;
            if (_draft is not null)
            {
                _dragSupport = null;
                _dragValid = false;
                _interaction = SupportInteraction.EditDraft;
                _preview = _draft.Preview;
            }
            else CancelSupportInteraction();
            _supportFeedback = feedback;
            NotifySupportState();
            return false;
        }
        var support = _dragSupport;
        var position = Preview!.Position;
        _dragSupport = null;
        _dragValid = false;
        if (_draft is not null)
        {
            _interaction = SupportInteraction.EditDraft;
            _draft.ApplyDragPosition(position);
            _supportFeedback = null;
            NotifySupportState();
        }
        else OpenDraft(support.Id, support.Type, position);
        return true;
    }

    public bool ConfirmSupport()
    {
        var draft = _draft;
        if (!IsSupportFlyoutVisible || draft is null || !draft.TryGetValue(out var position)) return false;
        var supports = Document.Supports.ToArray();
        if (draft.OriginalId is { } id)
        {
            int index = Array.FindIndex(supports, s => s.Id == id);
            if (index < 0) { CancelSupportInteraction(); return false; }
            if (supports[index].Position == position && supports[index].Type == draft.Type)
            {
                CancelSupportInteraction();
                return true;
            }
            supports[index] = supports[index] with { Position = position, Type = draft.Type };
        }
        else
            supports = [.. supports, new(Guid.NewGuid(), position, draft.Type)];
        var document = Document.WithSupports(supports);
        CancelSupportInteraction();
        Commit(document);
        return true;
    }

    public void DeleteSupport()
    {
        if (Interaction != SupportInteraction.EditDraft || _draft?.OriginalId is not { } id
            || !Document.Supports.Any(s => s.Id == id)) return;
        var document = Document.WithSupports(Document.Supports.Where(s => s.Id != id));
        CancelSupportInteraction();
        Commit(document);
    }

    public void CancelSupportInteraction()
    {
        if (_draft is not null) _draft.PropertyChanged -= DraftChanged;
        _draft = null;
        _dragSupport = null;
        _dragValid = false;
        _placementTool = null;
        _preview = null;
        _hoveredSupportId = null;
        _supportFeedback = null;
        _interaction = SupportInteraction.Neutral;
        NotifySupportState();
    }

    private bool CanPosition(Length position, Guid? exclude = null) =>
        position.Meters <= Document.Length.Meters && !Document.Supports.Any(s => s.Id != exclude && s.Position == position);

    private string PositionError(Length position, Guid? exclude = null) => position.Meters > Document.Length.Meters
        ? Strings.PositionInsideBeam : Document.Supports.Any(s => s.Id != exclude && s.Position == position)
            ? Strings.SupportAlreadyExists : Strings.PositionInsideBeam;

    private void OpenDraft(Guid? id, SupportType type, Length position)
    {
        _draft = new(() => Document, id, type, position);
        _draft.PropertyChanged += DraftChanged;
        _preview = _draft.Preview;
        _hoveredSupportId = null;
        _supportFeedback = null;
        _interaction = id.HasValue ? SupportInteraction.EditDraft : SupportInteraction.NewDraft;
        NotifySupportState();
    }

    private void DraftChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SupportDraftViewModel.Preview) || _draft is null || Interaction == SupportInteraction.Drag) return;
        _preview = _draft.Preview;
        _supportFeedback = null;
        NotifySupportState();
    }

    private void NotifySupportState()
    {
        foreach (string name in new[] { nameof(Interaction), nameof(PlacementTool), nameof(IsFixedTool),
            nameof(IsPinnedTool), nameof(IsRollerTool), nameof(SupportDraft), nameof(IsSupportFlyoutVisible), nameof(Preview),
            nameof(HoveredSupportId), nameof(HiddenSupportId), nameof(SupportFeedback),
            nameof(HasSupportFeedback), nameof(HasCoordinate), nameof(CoordinateText) }) Notify(name);
    }

    private LengthCommitResult ChangeLength(Length length)
    {
        if (Document.Supports.Any(s => s.Position.Meters > length.Meters))
        {
            UpdateConstraintConflict(length);
            return new(false, Strings.LengthExcludesSupports);
        }
        UpdateConstraintConflict(null);
        if (length != Document.Length) Commit(Document with { Length = length });
        return LengthCommitResult.Success;
    }

    private void UpdateConstraintConflict(Length? requested)
    {
        var ids = requested is { } length
            ? Document.Supports.Where(s => s.Position.Meters > length.Meters).Select(s => s.Id).ToArray() : [];
        _constraintConflict = requested is { } value && ids.Length > 0 ? new(value, ids) : null;
        Notify(nameof(ConstraintConflict));
        Notify(nameof(ConflictEntityIds));
    }

    public void ApplySetup(Section section, Material material) =>
        Commit(Document with { Section = section, Material = material });

    private void Commit(EditorDocument document)
    {
        _document = document;
        if (ConstraintConflict is { } conflict) UpdateConstraintConflict(conflict.RequestedLength);
        _presentation = AnalysisPresentationState.FromOutcome(_analyze(document.ToBeamModel()));
        DimensionLength.Refresh(preserveError: ConstraintConflict is not null);
        Notify(nameof(Document));
        Notify(nameof(Presentation));
        Notify(nameof(ProjectInfo));
    }
}
