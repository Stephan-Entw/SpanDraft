using System.ComponentModel;
using System.Globalization;
using SpanDraft.Analysis;
using SpanDraft.Core.Beams;
using SpanDraft.Core.Materials;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Diagnostics;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.State;

namespace SpanDraft.Desktop.ViewModels;

public sealed partial class EditorViewModel : ObservableObject
{
    private readonly Func<BeamModel, BeamAnalysisOutcome> _analyze;
    private readonly InteractionPerformance? _performance;
    private AnalysisPresentationState _presentation;
    private ProjectOverviewState _overview;
    private EditorPresentationState? _annotationPreview;
    private SupportInteraction _interaction;
    private SupportType? _placementTool;
    private SpringKind? _springTool;
    private SupportDraftViewModel? _draft;
    private SupportPreview? _preview;
    private Guid? _hoveredSupportId;
    private EditorSupport? _dragSupport;
    private bool _dragValid;
    private string? _supportFeedback;
    private ConstraintConflictState? _constraintConflict;

    public EditorViewModel(EditorDocument document, Action changeProject,
        Func<BeamModel, BeamAnalysisOutcome>? analyze = null, ResultPresentationOptions? resultPresentation = null,
        InteractionPerformance? performance = null)
        : this(changeProject, ProjectSession.Create(new(document, new())), analyze, resultPresentation, performance) { }

    public static EditorViewModel ForSession(ProjectSession session, Action changeProject,
        Func<BeamModel, BeamAnalysisOutcome>? analyze = null, ResultPresentationOptions? resultPresentation = null,
        InteractionPerformance? performance = null) =>
        new(changeProject, session, analyze, resultPresentation, performance);

    private EditorViewModel(Action changeProject, ProjectSession session,
        Func<BeamModel, BeamAnalysisOutcome>? analyze, ResultPresentationOptions? resultPresentation,
        InteractionPerformance? performance)
    {
        Session = session;
        _performance = performance;
        _analyze = analyze ?? BeamAnalysis.Analyze;
        _presentation = AnalysisPresentationState.FromOutcome(_analyze(Document.ToBeamModel()), resultPresentation,
            Document.MomentReleases.Count > 0);
        _overview = ProjectOverviewState.From(Document, _presentation);
        DimensionLength = new(() => Document.Length, ChangeLength, () => PreserveDrafts,
            () => ResultPresentation.Profile[QuantityKind.BeamLength], readPresentation: () => ResultPresentation,
            performance: _performance, isBlocked: () => Session.IsFrozen);
        Session.StateApplied += ApplyState;
        Session.Changed += RefreshBusyState;
        Session.FreezeChanged += RefreshBusyState;
        DimensionLength.BufferChanged += text =>
        {
            if (ConstraintConflict is not null)
                UpdateConstraintConflict(DimensionLength.TryGetLength(out var length) ? length : null);
        };
        DimensionLength.EditCancelled += () => UpdateConstraintConflict(null);
        ChangeProjectCommand = new(() => { if (!IsBusy) { CancelEditorInteraction(); changeProject(); } });
        ForceToolCommand = new(() => ToggleLoadTool(PointLoadKind.Force));
        MomentToolCommand = new(() => ToggleLoadTool(PointLoadKind.Moment));
        ConfirmLoadCommand = new(() => ConfirmLoad());
        CancelLoadCommand = new(CancelLoadInteraction);
        DeleteLoadCommand = new(DeleteLoad);
        DistributedLoadToolCommand = new(ToggleDistributedLoadTool);
        ConfirmDistributedLoadCommand = new(() => ConfirmDistributedLoad());
        CancelDistributedLoadCommand = new(CancelDistributedLoadInteraction);
        DeleteDistributedLoadCommand = new(DeleteDistributedLoad);
        ToggleSelfWeightCommand = new(() => ToggleSelfWeight());
        ToggleSelfWeightVisibilityCommand = new(ToggleSelfWeightVisibility);
        DisableSelfWeightCommand = new(DisableSelfWeight);
        FixedToolCommand = new(() => ToggleSupportTool(SupportType.Fixed));
        PinnedToolCommand = new(() => ToggleSupportTool(SupportType.Pinned));
        RollerToolCommand = new(() => ToggleSupportTool(SupportType.Roller));
        TranslationalSpringToolCommand = new(() => ToggleSpringTool(SpringKind.Translational));
        RotationalSpringToolCommand = new(() => ToggleSpringTool(SpringKind.Rotational));
        MomentReleaseToolCommand = new(ToggleMomentReleaseTool);
        ConfirmMomentReleaseCommand = new(() => ConfirmMomentRelease());
        CancelMomentReleaseCommand = new(CancelMomentReleaseInteraction);
        DeleteMomentReleaseCommand = new(DeleteMomentRelease);
        ResultProbeToolCommand = new(ToggleResultProbeTool, () => CanResultProbe);
        ConfirmSupportCommand = new(() => ConfirmSupport());
        CancelSupportCommand = new(CancelSupportInteraction);
        DeleteSupportCommand = new(DeleteSupport);
    }

    private void RefreshBusyState()
    {
        Notify(nameof(IsBusy));
        RefreshResultProbeAvailability();
    }

    public ProjectSession Session { get; }
    public bool IsBusy => Session.IsBusy;
    public bool IsReportOpen { get; set; }
    public bool IsMainMenuOpen { get; set; }
    public bool IsSettingsDialogOpen { get; set; }
    public bool IsProjectSetupOpen { get; set; }
    public bool PreserveDrafts => IsReportOpen || IsBusy || IsMainMenuOpen || IsSettingsDialogOpen || IsProjectSetupOpen;
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
            || LoadState == LoadInteraction.Drag || DistributedLoadState == DistributedLoadInteraction.Drag
            || MomentReleaseState != MomentReleaseInteraction.Neutral;
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
        if (IsSelfWeightEnabled) NotifySelfWeightValues();
        DimensionLength.Refresh(preserveError: DimensionLength.IsEditing);
        Notify(nameof(CoordinateText));
        if (IsResultProbeTool) RefreshResultProbe();
    }
    public EditorPresentationState EditorPresentation => Session.CurrentRevision.State.Presentation;
    public EditorPresentationState RenderPresentation => _annotationPreview ?? EditorPresentation;
    public LengthInputViewModel DimensionLength { get; }
    public ConstraintConflictState? ConstraintConflict => _constraintConflict;
    public IReadOnlyList<Guid> ConflictEntityIds => ConstraintConflict?.BlockingEntityIds ?? Array.Empty<Guid>();
    public ActionCommand ChangeProjectCommand { get; }
    public ActionCommand FixedToolCommand { get; }
    public ActionCommand PinnedToolCommand { get; }
    public ActionCommand RollerToolCommand { get; }
    public ActionCommand TranslationalSpringToolCommand { get; }
    public ActionCommand RotationalSpringToolCommand { get; }
    public ActionCommand ConfirmSupportCommand { get; }
    public ActionCommand CancelSupportCommand { get; }
    public ActionCommand DeleteSupportCommand { get; }
    public SupportInteraction Interaction => _interaction;
    public SupportType? PlacementTool => _placementTool;
    public bool IsFixedTool => PlacementTool == SupportType.Fixed;
    public bool IsPinnedTool => PlacementTool == SupportType.Pinned;
    public bool IsRollerTool => PlacementTool == SupportType.Roller;
    public bool IsTranslationalSpringTool => _springTool == SpringKind.Translational;
    public bool IsRotationalSpringTool => _springTool == SpringKind.Rotational;
    public SupportDraftViewModel? SupportDraft => _draft;
    public bool IsSupportFlyoutVisible => _draft is not null && Interaction is SupportInteraction.NewDraft or SupportInteraction.EditDraft;
    public SupportPreview? Preview => _preview;
    public string SupportPreviewName => _draft?.NameText ?? _dragSupport?.Name
        ?? (PlacementTool is not null ? AutoName(AutoNameKind.Support) : "");

    // The next automatic name only changes with the document, but the preview names are read on every pointer move.
    private EditorDocument? _autoNameDocument;
    private readonly Dictionary<AutoNameKind, string> _autoNames = [];
    private string AutoName(AutoNameKind kind)
    {
        if (!ReferenceEquals(_autoNameDocument, Document)) { _autoNames.Clear(); _autoNameDocument = Document; }
        if (!_autoNames.TryGetValue(kind, out var name)) _autoNames[kind] = name = EntityNaming.Peek(Document, kind).Name;
        return name;
    }
    public Guid? HoveredSupportId => _hoveredSupportId;
    public Guid? HiddenSupportId => _draft?.OriginalId ?? _dragSupport?.Id;
    public string? SupportFeedback => _supportFeedback;
    public bool HasSupportFeedback => !string.IsNullOrEmpty(SupportFeedback);
    public bool IsEditorNeutral => !IsResultProbeTool && Interaction == SupportInteraction.Neutral && LoadState == LoadInteraction.Neutral
        && DistributedLoadState == DistributedLoadInteraction.Neutral && MomentReleaseState == MomentReleaseInteraction.Neutral
        && !IsSelfWeightFlyoutVisible;
    public bool HasCoordinate => Preview is not null || LoadPreview is not null || DistributedPointerPosition is not null || DistributedLoadPreview is not null
        || MomentReleasePreview is not null;
    public string CoordinateText => (Preview?.Position ?? LoadPreview?.Position ?? DistributedPointerPosition ?? DistributedLoadPreview?.EndPosition
        ?? MomentReleasePreview?.Position) is { } position
        ? string.Format(CultureInfo.CurrentUICulture, Strings.SupportCoordinate,
            QuantityFormatter.FormatInputNumber(position.Meters, ResultPresentation.Profile[QuantityKind.BeamLength], ResultPresentation),
            ResultPresentation.Profile[QuantityKind.BeamLength].Symbol) : "";

    /// <summary>Activates the placement tool of a rigid support; a spring support is placed by its own tool, see <see cref="ToggleSpringTool"/>.</summary>
    public void ToggleSupportTool(SupportType type)
    {
        if (!Enum.IsDefined(type)) throw new ArgumentOutOfRangeException(nameof(type));
        if (type == SupportType.Spring) throw new ArgumentException("A spring support needs a spring kind.", nameof(type));
        ActivateTool(type, null);
    }

    /// <summary>Activates the placement tool of a translational or a rotational spring support.</summary>
    public void ToggleSpringTool(SpringKind kind)
    {
        if (kind is not (SpringKind.Translational or SpringKind.Rotational)) throw new ArgumentOutOfRangeException(nameof(kind));
        ActivateTool(SupportType.Spring, kind);
    }

    private void ActivateTool(SupportType type, SpringKind? kind)
    {
        if (IsBusy) return;
        bool deactivate = PlacementTool == type && _springTool == kind;
        CancelEditorInteraction();
        if (deactivate) return;
        _placementTool = type;
        _springTool = kind;
        _interaction = SupportInteraction.Placement;
        NotifySupportState();
    }

    public void HoverPlacement(Length? position)
    {
        if (IsBusy) return;
        if (Interaction != SupportInteraction.Placement) return;
        var type = PlacementTool!.Value;
        var spring = _springTool switch
        {
            SpringKind.Translational => PlacementTranslationalSpring,
            SpringKind.Rotational => PlacementRotationalSpring,
            _ => null
        };
        _preview = position is { } p ? new(p, type, !CanSupportAt(p, type, spring), spring) : null;
        _supportFeedback = _preview?.IsInvalid == true ? PositionError(position!.Value, null, type, spring) : null;
        NotifySupportState();
    }

    public bool PlaceSupport()
    {
        if (IsBusy) return false;
        if (Interaction != SupportInteraction.Placement || Preview is not { IsInvalid: false } preview
            || !CanSupportAt(preview.Position, preview.Type, preview.Spring)) return false;
        OpenDraft(null, preview.Type, preview.Position, preview.Spring, _springTool);
        return true;
    }

    public void HoverSupport(Guid? id)
    {
        if (IsBusy) return;
        if (!IsEditorNeutral) return;
        if (_hoveredSupportId == id) return;
        _hoveredSupportId = id;
        _supportFeedback = null;
        NotifySupportState();
    }

    public bool EditSupport(Guid id)
    {
        if (IsBusy) return false;
        if (Interaction == SupportInteraction.EditDraft && _draft?.OriginalId == id) return true;
        if (!IsEditorNeutral) return false;
        var support = Document.Supports.FirstOrDefault(s => s.Id == id);
        if (support is null) return false;
        OpenDraft(support.Id, support.Type, support.Position, support.Spring);
        return true;
    }

    public bool BeginSupportDrag(Guid id)
    {
        if (IsBusy) return false;
        bool editing = Interaction == SupportInteraction.EditDraft && _draft?.OriginalId == id;
        if ((Interaction != SupportInteraction.Neutral && !editing) || LoadState != LoadInteraction.Neutral
            || DistributedLoadState != DistributedLoadInteraction.Neutral
            || MomentReleaseState != MomentReleaseInteraction.Neutral) return false;
        var support = Document.Supports.FirstOrDefault(s => s.Id == id);
        if (support is null) return false;
        if (editing) support = support with { Position = _draft!.CanvasPosition, Type = _draft.Preview.Type, Spring = _draft.Preview.Spring };
        _dragSupport = support;
        _interaction = SupportInteraction.Drag;
        _hoveredSupportId = null;
        UpdateSupportDrag(support.Position);
        return true;
    }

    public void UpdateSupportDrag(Length? position)
    {
        if (IsBusy) return;
        if (Interaction != SupportInteraction.Drag || _dragSupport is null) return;
        _dragValid = position is { } p && CanSupportAt(p, _dragSupport.Type, _dragSupport.Spring, _dragSupport.Id);
        _supportFeedback = _dragValid ? null : position is { } invalid
            ? PositionError(invalid, _dragSupport.Id, _dragSupport.Type, _dragSupport.Spring) : Strings.PositionInsideBeam;
        _preview = new(position ?? _preview?.Position ?? _dragSupport.Position, _dragSupport.Type, !_dragValid, _dragSupport.Spring);
        NotifySupportState();
    }

    public bool EndSupportDrag()
    {
        if (IsBusy) return false;
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
        else OpenDraft(support.Id, support.Type, position, support.Spring);
        return true;
    }

    public bool ConfirmSupport()
    {
        using var measurement = InteractionPerformance.Measure(_performance, _draft?.Type == SupportType.Spring
            ? InteractionAction.SpringSupport : InteractionAction.Support);
        if (IsBusy) return false;
        var draft = _draft;
        if (!IsSupportFlyoutVisible || draft is null || !draft.TryGetValue(out var position)) return false;
        var supports = Document.Supports.ToArray();
        var naming = Document.NamingState;
        if (draft.OriginalId is { } id)
        {
            int index = Array.FindIndex(supports, s => s.Id == id);
            if (index < 0) { CancelSupportInteraction(); return false; }
            supports[index] = supports[index] with { Position = position, Reference = draft.Reference.Current, Type = draft.Type, Name = draft.NameText, Spring = draft.Spring };
        }
        else
        {
            supports = [.. supports, new(Guid.NewGuid(), position, draft.Type, draft.NameText, draft.Spring) { Reference = draft.Reference.Current }];
            naming = naming.Consume(draft.AutoCandidate!.Value);
        }
        var document = Document.WithSupports(supports, naming);
        CancelSupportInteraction();
        Commit(document);
        return true;
    }

    public void DeleteSupport()
    {
        using var measurement = InteractionPerformance.Measure(_performance, _draft?.Type == SupportType.Spring
            ? InteractionAction.SpringSupport : InteractionAction.Support);
        if (IsBusy || Interaction != SupportInteraction.EditDraft || _draft?.OriginalId is not { } id
            || !Document.Supports.Any(s => s.Id == id)) return;
        var document = Document.WithSupports(Document.Supports.Where(s => s.Id != id));
        CancelSupportInteraction();
        Commit(document);
    }

    public void CancelSupportInteraction()
    {
        if (Session.IsFrozen) return;
        if (_draft is not null) _draft.PropertyChanged -= DraftChanged;
        _draft = null;
        _dragSupport = null;
        _dragValid = false;
        _placementTool = null;
        _springTool = null;
        _preview = null;
        _hoveredSupportId = null;
        _supportFeedback = null;
        _interaction = SupportInteraction.Neutral;
        NotifySupportState();
    }

    /// <summary>Checks the inclusive SI range and exact committed-position collisions: no other support stands there.</summary>
    public bool CanPosition(double positionMeters, Guid? exclude = null) =>
        InsideBeam(positionMeters)
        && !Document.Supports.Any(s => s.Id != exclude && s.Position.Meters == positionMeters);

    private bool InsideBeam(double positionMeters) => positionMeters >= 0 && positionMeters <= Document.Length.Meters;

    /// <summary>
    /// Position and type together: the position lies in the beam, the station rule of the supports holds (one translational
    /// and one rotational spring may share a position) and the hinge rule of <see cref="MomentReleaseRules"/> holds
    /// (a fixed support or a rotational spring cannot stand at a moment release).
    /// </summary>
    private bool CanSupportAt(Length position, SupportType type, SpringData? spring, Guid? exclude = null) =>
        InsideBeam(position.Meters) && SupportStations.Allows(Document.Supports, position, type, spring, exclude)
        && !MomentReleaseRules.SupportConflicts(Document, position, type, spring);

    private string PositionError(Length position, Guid? exclude = null, SupportType? type = null, SpringData? spring = null) =>
        position.Meters > Document.Length.Meters ? Strings.PositionInsideBeam
        : type is { } station && !SupportStations.Allows(Document.Supports, position, station, spring, exclude) ? Strings.SupportAlreadyExists
        : type is { } kind && MomentReleaseRules.SupportConflicts(Document, position, kind, spring) ? Strings.SupportAtMomentRelease
        : Strings.PositionInsideBeam;

    /// <summary>The SI values used by the placement preview and the initial, editable draft, as for point loads.</summary>
    private static readonly SpringData PlacementTranslationalSpring = new(TranslationalStiffness.FromNewtonsPerMeter(1e6), null);
    private static readonly SpringData PlacementRotationalSpring = new(null, RotationalStiffness.FromNewtonMetersPerRadian(1000));

    private void OpenDraft(Guid? id, SupportType type, Length position, SpringData? spring = null, SpringKind? kind = null)
    {
        _draft = new(() => Document, id, type, position, ResultPresentation.Profile, spring, kind, () => Session.IsFrozen);
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
            nameof(IsPinnedTool), nameof(IsRollerTool), nameof(IsTranslationalSpringTool), nameof(IsRotationalSpringTool), nameof(SupportDraft), nameof(IsSupportFlyoutVisible), nameof(Preview),
            nameof(HoveredSupportId), nameof(HiddenSupportId), nameof(SupportFeedback),
            nameof(HasSupportFeedback), nameof(HasCoordinate), nameof(CoordinateText), nameof(SupportPreviewName) }) Notify(name);
    }

    /// <summary>
    /// The beam length is edited here only while it is the length of the one segment; with several segments it is the
    /// sum of their lengths and is changed in the project properties, where it is clear which segment changes.
    /// </summary>
    public bool CanEditLength => !Document.HasSeveralSegments;
    public string LengthTooltip => CanEditLength ? Strings.EditLength : Strings.LengthFromSegments;

    private LengthCommitResult ChangeLength(Length length)
    {
        if (IsBusy) return new(false, Strings.ProjectBusy);
        if (!CanEditLength) return new(false, Strings.LengthFromSegments);
        var check = BeamLengthConstraints.Evaluate(Document, length);
        UpdateConstraintConflict(length, check.BlockingEntityIds);
        if (check.Document is not { } document) return new(false, check.ErrorText);
        Commit(document);
        return LengthCommitResult.Success;
    }

    private void UpdateConstraintConflict(Length? requested, IReadOnlyList<Guid>? blockingEntities = null)
    {
        var ids = blockingEntities ?? (requested is { } length
            ? BeamLengthConstraints.BlockingEntities(Document, length) : []);
        _constraintConflict = requested is { } value && ids.Count > 0 ? new(value, ids) : null;
        Notify(nameof(ConstraintConflict));
        Notify(nameof(ConflictEntityIds));
    }

    public void ApplySetup(Section section, Material material) => ApplySetup(section, SectionAxisDesignation.Y, material);

    public void ApplySetup(ISectionDefinition section, SectionAxisDesignation bendingAxis, Material material)
    {
        using var measurement = InteractionPerformance.Measure(_performance, InteractionAction.ProjectSetup);
        // The single-segment shortcut; a beam of several segments is set up through the segment chain.
        if (!IsBusy && !Document.HasSeveralSegments && CanApplyMaterial(material))
            Commit(Document.WithSection(section, bendingAxis) with { Material = material });
    }

    public bool ApplySetup(Length length, ISectionDefinition section, SectionAxisDesignation bendingAxis,
        Material material, string? sectionDisplayName) =>
        ApplySetup(length, section, bendingAxis, material, sectionDisplayName, Session.CurrentRevision.State.Info);

    public bool ApplySetup(Length length, ISectionDefinition section, SectionAxisDesignation bendingAxis,
        Material material, string? sectionDisplayName, ProjectInfo? info)
    {
        using var measurement = InteractionPerformance.Measure(_performance, InteractionAction.ProjectSetup);
        if (IsBusy || Document.HasSeveralSegments || !(length.Meters > 0)) return false;
        var segment = Document.Segments[0].WithSection(section, bendingAxis, sectionDisplayName).WithMaterial(material)
            with { Length = length };
        return ApplySetup([segment], info);
    }

    /// <summary>
    /// Applies the project setup of a beam of one or several segments in one commit. The chain is checked against the
    /// elements of the project; nothing is applied when one of them would become invalid.
    /// </summary>
    public bool ApplySetup(IReadOnlyList<EditorSegment> segments, ProjectInfo? info)
    {
        using var measurement = InteractionPerformance.Measure(_performance, InteractionAction.ProjectSetup);
        if (IsBusy || !segments.All(segment => CanApplyMaterial(segment.Material))) return false;
        var change = Document.WithSegmentChain(segments);
        if (change.Document is not { } document) return false;
        Commit(Session.CurrentRevision.State with
        {
            Document = document, Presentation = EditorPresentation.RetainEntities(document), Info = info
        });
        return true;
    }

    public bool SetAnnotationOffset(Guid id, AnnotationOffset? offset) => SetAnnotationOffsets([id], offset);

    public bool SetAnnotationOffsets(IReadOnlyList<Guid> ids, AnnotationOffset? offset)
    {
        using var measurement = InteractionPerformance.Measure(_performance, InteractionAction.AnnotationOffset);
        if (IsBusy || ids.Count == 0 || ids.Any(id => !Document.EntityIds.Contains(id))) return false;
        var presentation = EditorPresentation;
        foreach (var id in ids) presentation = presentation.WithOffset(id, offset);
        Commit(Document, presentation);
        return true;
    }

    public void PreviewAnnotationOffset(Guid id, AnnotationOffset offset) => PreviewAnnotationOffsets([id], offset);

    public void PreviewAnnotationOffsets(IReadOnlyList<Guid> ids, AnnotationOffset offset)
    {
        if (IsBusy || ids.Count == 0 || ids.Any(id => !Document.EntityIds.Contains(id))) return;
        var presentation = EditorPresentation;
        foreach (var id in ids) presentation = presentation.WithOffset(id, offset);
        _annotationPreview = presentation;
        Notify(nameof(RenderPresentation));
    }

    public void ClearAnnotationPreview()
    {
        if (Session.IsFrozen) return;
        if (_annotationPreview is null) return;
        _annotationPreview = null;
        Notify(nameof(RenderPresentation));
    }

    public bool Undo()
    {
        using var measurement = InteractionPerformance.Measure(_performance, InteractionAction.Undo);
        if (IsBusy) return false;
        CancelEditorInteraction();
        bool changed = Session.Undo();
        if (changed) _performance?.Committed();
        return changed;
    }

    public bool Redo()
    {
        using var measurement = InteractionPerformance.Measure(_performance, InteractionAction.Redo);
        if (IsBusy) return false;
        CancelEditorInteraction();
        bool changed = Session.Redo();
        if (changed) _performance?.Committed();
        return changed;
    }

    private void Commit(EditorDocument document, EditorPresentationState? presentation = null) =>
        Commit(Session.CurrentRevision.State with
        { Document = document, Presentation = (presentation ?? EditorPresentation).RetainEntities(document) });

    private void Commit(ProjectState next)
    {
        if (Session.Commit(next)) _performance?.Committed();
    }

    private void ApplyState(ProjectState previous, ProjectState next, EditorChangeKind change)
    {
        bool documentChanged = !ReferenceEquals(previous.Document, next.Document);
        bool presentationChanged = !previous.Presentation.ContentEquals(next.Presentation);
        if (change == EditorChangeKind.Mechanical)
        {
            CancelResultProbe();
            if (ConstraintConflict is { } conflict) UpdateConstraintConflict(conflict.RequestedLength);
            _performance?.AnalysisStarting();
            var outcome = _analyze(next.Document.ToBeamModel());
            _performance?.AnalysisFinished();
            _presentation = AnalysisPresentationState.FromOutcome(outcome, ResultPresentation,
                next.Document.MomentReleases.Count > 0);
            DimensionLength.Refresh(preserveError: ConstraintConflict is not null);
        }
        else _performance?.PresentationStarting();
        if (documentChanged) _overview = ProjectOverviewState.From(next.Document, _presentation);
        if (change == EditorChangeKind.Mechanical)
        {
            Notify(nameof(Presentation));
            RefreshResultProbeAvailability();
        }
        if (documentChanged)
        {
            Notify(nameof(Document));
            Notify(nameof(Overview));
            Notify(nameof(CanEditLength));
            Notify(nameof(LengthTooltip));
        }
        if (presentationChanged) { Notify(nameof(EditorPresentation)); Notify(nameof(RenderPresentation)); }
        if (documentChanged || presentationChanged) NotifySelfWeightState();
    }
}
