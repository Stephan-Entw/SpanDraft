using System.ComponentModel;
using System.Globalization;
using SpanDraft.Analysis;
using SpanDraft.Core.Beams;
using SpanDraft.Core.Materials;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.State;

namespace SpanDraft.Desktop.ViewModels;

public sealed partial class EditorViewModel : ObservableObject
{
    private readonly Func<BeamModel, BeamAnalysisOutcome> _analyze;
    private AnalysisPresentationState _presentation;
    private ProjectOverviewState _overview;
    private KeyValuePair<Guid, AnnotationOffset>? _annotationPreview;
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
        Func<BeamModel, BeamAnalysisOutcome>? analyze = null, ResultPresentationOptions? resultPresentation = null)
        : this(changeProject, ProjectSession.Create(new(document, new())), analyze, resultPresentation) { }

    public static EditorViewModel ForSession(ProjectSession session, Action changeProject,
        Func<BeamModel, BeamAnalysisOutcome>? analyze = null, ResultPresentationOptions? resultPresentation = null) =>
        new(changeProject, session, analyze, resultPresentation);

    private EditorViewModel(Action changeProject, ProjectSession session,
        Func<BeamModel, BeamAnalysisOutcome>? analyze, ResultPresentationOptions? resultPresentation)
    {
        Session = session;
        _analyze = analyze ?? BeamAnalysis.Analyze;
        _presentation = AnalysisPresentationState.FromOutcome(_analyze(Document.ToBeamModel()), resultPresentation);
        _overview = ProjectOverviewState.From(Document, _presentation);
        DimensionLength = new(() => Document.Length, ChangeLength, () => PreserveDrafts,
            () => ResultPresentation.Profile[QuantityKind.BeamLength]);
        Session.StateApplied += ApplyState;
        Session.Changed += () => Notify(nameof(IsBusy));
        DimensionLength.BufferChanged += text =>
        {
            if (ConstraintConflict is not null)
                UpdateConstraintConflict(DimensionLength.TryGetLength(out var length) ? length : null);
        };
        DimensionLength.EditCancelled += () => UpdateConstraintConflict(null);
        ChangeProjectCommand = new(() => { CancelEditorInteraction(); changeProject(); });
        ForceToolCommand = new(() => ToggleLoadTool(PointLoadKind.Force));
        MomentToolCommand = new(() => ToggleLoadTool(PointLoadKind.Moment));
        ConfirmLoadCommand = new(() => ConfirmLoad());
        CancelLoadCommand = new(CancelLoadInteraction);
        DeleteLoadCommand = new(DeleteLoad);
        DistributedLoadToolCommand = new(ToggleDistributedLoadTool);
        ConfirmDistributedLoadCommand = new(() => ConfirmDistributedLoad());
        CancelDistributedLoadCommand = new(CancelDistributedLoadInteraction);
        DeleteDistributedLoadCommand = new(DeleteDistributedLoad);
        FixedToolCommand = new(() => ToggleSupportTool(SupportType.Fixed));
        PinnedToolCommand = new(() => ToggleSupportTool(SupportType.Pinned));
        RollerToolCommand = new(() => ToggleSupportTool(SupportType.Roller));
        ConfirmSupportCommand = new(() => ConfirmSupport());
        CancelSupportCommand = new(CancelSupportInteraction);
        DeleteSupportCommand = new(DeleteSupport);
    }

    public ProjectSession Session { get; }
    public bool IsBusy => Session.IsBusy;
    public bool IsFileMenuOpen { get; set; }
    public bool IsSettingsDialogOpen { get; set; }
    public bool PreserveDrafts => IsBusy || IsFileMenuOpen || IsSettingsDialogOpen;
    public event Action? InteractionsCancelling;
    public EditorDocument Document => Session.CurrentRevision.State.Document;
    public AnalysisPresentationState Presentation => _presentation;
    public ProjectOverviewState Overview => _overview;
    public ResultPresentationOptions ResultPresentation => _presentation.Options;

    public bool CanApplyInputUnits(UnitProfile profile)
    {
        bool Changed(QuantityKind q) => profile[q].Id != ResultPresentation.Profile[q].Id;
        bool positionEditing = DimensionLength.IsEditing || SupportDraft is not null || LoadDraft is not null
            || DistributedLoadDraft is not null || Interaction == SupportInteraction.Drag
            || LoadState == LoadInteraction.Drag || DistributedLoadState == DistributedLoadInteraction.Drag;
        if (positionEditing && Changed(QuantityKind.BeamLength)) return false;
        var pointKind = LoadDraft?.Kind ?? _dragLoad?.Kind;
        if (pointKind is { } kind && Changed(kind == PointLoadKind.Force ? QuantityKind.TransverseForce : QuantityKind.Moment)) return false;
        return !(DistributedLoadDraft is not null || DistributedLoadState == DistributedLoadInteraction.Drag)
            || !Changed(QuantityKind.DistributedLoad);
    }

    internal void ApplyResultPresentation(ResultPresentationOptions options)
    {
        var next = _presentation.WithPresentation(options);
        if (ReferenceEquals(next, _presentation)) return;
        _presentation = next;
        _overview = ProjectOverviewState.From(Document, _presentation);
        Notify(nameof(ResultPresentation));
        Notify(nameof(Presentation));
        Notify(nameof(Overview));
        DimensionLength.Refresh(preserveError: DimensionLength.IsEditing);
        Notify(nameof(CoordinateText));
    }
    public EditorPresentationState EditorPresentation => Session.CurrentRevision.State.Presentation;
    public EditorPresentationState RenderPresentation => _annotationPreview is { } p
        ? EditorPresentation.WithOffset(p.Key, p.Value) : EditorPresentation;
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
    public string SupportPreviewName => _draft?.NameText ?? _dragSupport?.Name
        ?? (PlacementTool is not null ? EntityNaming.Peek(Document, AutoNameKind.Support).Name : "");
    public Guid? HoveredSupportId => _hoveredSupportId;
    public Guid? HiddenSupportId => _draft?.OriginalId ?? _dragSupport?.Id;
    public string? SupportFeedback => _supportFeedback;
    public bool HasSupportFeedback => !string.IsNullOrEmpty(SupportFeedback);
    public bool IsEditorNeutral => Interaction == SupportInteraction.Neutral && LoadState == LoadInteraction.Neutral
        && DistributedLoadState == DistributedLoadInteraction.Neutral;
    public bool HasCoordinate => Preview is not null || LoadPreview is not null || DistributedPointerPosition is not null || DistributedLoadPreview is not null;
    public string CoordinateText => (Preview?.Position ?? LoadPreview?.Position ?? DistributedPointerPosition ?? DistributedLoadPreview?.EndPosition) is { } position
        ? string.Format(CultureInfo.CurrentUICulture, Strings.SupportCoordinate,
            InputQuantityFormatter.Display(position.Meters, ResultPresentation.Profile[QuantityKind.BeamLength]),
            ResultPresentation.Profile[QuantityKind.BeamLength].Symbol) : "";

    public void ToggleSupportTool(SupportType type)
    {
        if (!Enum.IsDefined(type)) throw new ArgumentOutOfRangeException(nameof(type));
        bool deactivate = PlacementTool == type;
        CancelEditorInteraction();
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
        if (!IsEditorNeutral) return;
        if (_hoveredSupportId == id) return;
        _hoveredSupportId = id;
        _supportFeedback = null;
        NotifySupportState();
    }

    public bool EditSupport(Guid id)
    {
        if (Interaction == SupportInteraction.EditDraft && _draft?.OriginalId == id) return true;
        if (!IsEditorNeutral) return false;
        var support = Document.Supports.FirstOrDefault(s => s.Id == id);
        if (support is null) return false;
        OpenDraft(support.Id, support.Type, support.Position);
        return true;
    }

    public bool BeginSupportDrag(Guid id)
    {
        bool editing = Interaction == SupportInteraction.EditDraft && _draft?.OriginalId == id;
        if ((Interaction != SupportInteraction.Neutral && !editing) || LoadState != LoadInteraction.Neutral
            || DistributedLoadState != DistributedLoadInteraction.Neutral) return false;
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
        if (IsBusy) return false;
        var draft = _draft;
        if (!IsSupportFlyoutVisible || draft is null || !draft.TryGetValue(out var position)) return false;
        var supports = Document.Supports.ToArray();
        var naming = Document.NamingState;
        if (draft.OriginalId is { } id)
        {
            int index = Array.FindIndex(supports, s => s.Id == id);
            if (index < 0) { CancelSupportInteraction(); return false; }
            supports[index] = supports[index] with { Position = position, Type = draft.Type, Name = draft.NameText };
        }
        else
        {
            supports = [.. supports, new(Guid.NewGuid(), position, draft.Type, draft.NameText)];
            naming = naming.Consume(draft.AutoCandidate!.Value);
        }
        var document = Document.WithSupports(supports, naming);
        CancelSupportInteraction();
        Commit(document);
        return true;
    }

    public void DeleteSupport()
    {
        if (IsBusy || Interaction != SupportInteraction.EditDraft || _draft?.OriginalId is not { } id
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
        CanPosition(position.Meters, exclude);

    /// <summary>Checks the inclusive SI range and exact committed-position collisions.</summary>
    public bool CanPosition(double positionMeters, Guid? exclude = null) =>
        positionMeters >= 0 && positionMeters <= Document.Length.Meters
        && !Document.Supports.Any(s => s.Id != exclude && s.Position.Meters == positionMeters);

    private string PositionError(Length position, Guid? exclude = null) => position.Meters > Document.Length.Meters
        ? Strings.PositionInsideBeam : Document.Supports.Any(s => s.Id != exclude && s.Position == position)
            ? Strings.SupportAlreadyExists : Strings.PositionInsideBeam;

    private void OpenDraft(Guid? id, SupportType type, Length position)
    {
        _draft = new(() => Document, id, type, position, ResultPresentation.Profile);
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
            nameof(HasSupportFeedback), nameof(HasCoordinate), nameof(CoordinateText), nameof(SupportPreviewName) }) Notify(name);
    }

    private LengthCommitResult ChangeLength(Length length)
    {
        if (IsBusy) return new(false, Strings.ProjectBusy);
        if (Document.Supports.Any(s => s.Position.Meters > length.Meters)
            || Document.Loads.Any(l => l.Position.Meters > length.Meters)
            || Document.DistributedLoads.Any(l => l.EndPosition.Meters > length.Meters))
        {
            UpdateConstraintConflict(length);
            return new(false, Document.Loads.Any(l => l.Position.Meters > length.Meters)
                || Document.DistributedLoads.Any(l => l.EndPosition.Meters > length.Meters)
                ? Strings.LengthExcludesEntities : Strings.LengthExcludesSupports);
        }
        UpdateConstraintConflict(null);
        Commit(Document with { Length = length });
        return LengthCommitResult.Success;
    }

    private void UpdateConstraintConflict(Length? requested)
    {
        var ids = requested is { } length
            ? Document.Supports.Where(s => s.Position.Meters > length.Meters).Select(s => s.Id)
                .Concat(Document.Loads.Where(l => l.Position.Meters > length.Meters).Select(l => l.Id))
                .Concat(Document.DistributedLoads.Where(l => l.EndPosition.Meters > length.Meters).Select(l => l.Id)).ToArray() : [];
        _constraintConflict = requested is { } value && ids.Length > 0 ? new(value, ids) : null;
        Notify(nameof(ConstraintConflict));
        Notify(nameof(ConflictEntityIds));
    }

    public void ApplySetup(Section section, Material material) =>
        Commit(Document with { Section = section, Material = material });

    public bool SetAnnotationOffset(Guid id, AnnotationOffset? offset)
    {
        if (IsBusy || !Document.NamedEntities.Any(e => e.Id == id)) return false;
        Commit(Document, EditorPresentation.WithOffset(id, offset));
        return true;
    }

    public void PreviewAnnotationOffset(Guid id, AnnotationOffset offset)
    {
        if (IsBusy || !Document.NamedEntities.Any(e => e.Id == id)) return;
        _annotationPreview = new(id, offset);
        Notify(nameof(RenderPresentation));
    }

    public void ClearAnnotationPreview()
    {
        if (_annotationPreview is null) return;
        _annotationPreview = null;
        Notify(nameof(RenderPresentation));
    }

    public bool Undo()
    {
        if (IsBusy) return false;
        CancelEditorInteraction();
        return Session.Undo();
    }

    public bool Redo()
    {
        if (IsBusy) return false;
        CancelEditorInteraction();
        return Session.Redo();
    }

    private void Commit(EditorDocument document, EditorPresentationState? presentation = null) =>
        Session.Commit(new(document, (presentation ?? EditorPresentation).RetainEntities(document)));

    private void ApplyState(ProjectState previous, ProjectState next, EditorChangeKind change)
    {
        bool documentChanged = !ReferenceEquals(previous.Document, next.Document);
        bool presentationChanged = !previous.Presentation.ContentEquals(next.Presentation);
        if (change == EditorChangeKind.Mechanical)
        {
            if (ConstraintConflict is { } conflict) UpdateConstraintConflict(conflict.RequestedLength);
            _presentation = AnalysisPresentationState.FromOutcome(_analyze(next.Document.ToBeamModel()), ResultPresentation);
            DimensionLength.Refresh(preserveError: ConstraintConflict is not null);
        }
        if (documentChanged) _overview = ProjectOverviewState.From(next.Document, _presentation);
        if (change == EditorChangeKind.Mechanical) Notify(nameof(Presentation));
        if (documentChanged)
        {
            Notify(nameof(Document));
            Notify(nameof(Overview));
        }
        if (presentationChanged) { Notify(nameof(EditorPresentation)); Notify(nameof(RenderPresentation)); }
    }
}
