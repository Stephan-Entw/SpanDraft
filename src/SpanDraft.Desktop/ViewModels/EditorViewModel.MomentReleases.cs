using System.ComponentModel;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.State;

namespace SpanDraft.Desktop.ViewModels;

public sealed partial class EditorViewModel
{
    private MomentReleaseInteraction _releaseState;
    private MomentReleaseDraftViewModel? _releaseDraft;
    private MomentReleasePreview? _releasePreview;
    private Guid? _hoveredReleaseId;
    private EditorMomentRelease? _dragRelease;
    private bool _releaseDragValid;
    private string? _releaseFeedback;

    public ActionCommand MomentReleaseToolCommand { get; }
    public ActionCommand ConfirmMomentReleaseCommand { get; }
    public ActionCommand CancelMomentReleaseCommand { get; }
    public ActionCommand DeleteMomentReleaseCommand { get; }
    public MomentReleaseInteraction MomentReleaseState => _releaseState;
    public bool IsMomentReleaseTool => _releaseState == MomentReleaseInteraction.Placement;
    public MomentReleaseDraftViewModel? MomentReleaseDraft => _releaseDraft;
    public MomentReleasePreview? MomentReleasePreview => _releasePreview;
    public string MomentReleasePreviewName => _releaseDraft?.NameText ?? _dragRelease?.Name
        ?? (IsMomentReleaseTool ? AutoName(AutoNameKind.MomentRelease) : "");
    public Guid? HoveredMomentReleaseId => _hoveredReleaseId;
    public Guid? HiddenMomentReleaseId => _releaseDraft?.OriginalId ?? _dragRelease?.Id;
    public bool IsMomentReleaseFlyoutVisible => _releaseDraft is not null
        && MomentReleaseState is MomentReleaseInteraction.NewDraft or MomentReleaseInteraction.EditDraft;
    public string? MomentReleaseFeedback => _releaseFeedback;
    public bool HasMomentReleaseFeedback => !string.IsNullOrEmpty(_releaseFeedback);

    public void ToggleMomentReleaseTool()
    {
        bool deactivate = IsMomentReleaseTool;
        CancelEditorInteraction();
        if (deactivate) return;
        _releaseState = MomentReleaseInteraction.Placement;
        NotifyMomentReleaseState();
    }

    public void HoverMomentReleasePlacement(Length? position)
    {
        if (MomentReleaseState != MomentReleaseInteraction.Placement) return;
        var conflict = position is { } p ? MomentReleaseRules.Conflict(Document, p) : MomentReleaseConflict.None;
        _releasePreview = position is { } at ? new(at, conflict != MomentReleaseConflict.None) : null;
        _releaseFeedback = MomentReleaseRules.Message(conflict);
        NotifyMomentReleaseState();
    }

    public bool PlaceMomentRelease()
    {
        if (MomentReleaseState != MomentReleaseInteraction.Placement || MomentReleasePreview is not { IsInvalid: false } preview
            || MomentReleaseRules.Conflict(Document, preview.Position) != MomentReleaseConflict.None) return false;
        OpenMomentReleaseDraft(null, preview.Position);
        return true;
    }

    public void HoverMomentRelease(Guid? id)
    {
        if (!IsEditorNeutral || _hoveredReleaseId == id) return;
        _hoveredReleaseId = id;
        _releaseFeedback = null;
        NotifyMomentReleaseState();
    }

    public bool EditMomentRelease(Guid id)
    {
        if (MomentReleaseState == MomentReleaseInteraction.EditDraft && MomentReleaseDraft?.OriginalId == id) return true;
        if (!IsEditorNeutral) return false;
        var release = Document.MomentReleases.FirstOrDefault(r => r.Id == id);
        if (release is null) return false;
        OpenMomentReleaseDraft(id, release.Position);
        return true;
    }

    public bool BeginMomentReleaseDrag(Guid id)
    {
        bool editing = MomentReleaseState == MomentReleaseInteraction.EditDraft && MomentReleaseDraft?.OriginalId == id;
        if (Interaction != SupportInteraction.Neutral || LoadState != LoadInteraction.Neutral
            || DistributedLoadState != DistributedLoadInteraction.Neutral
            || (MomentReleaseState != MomentReleaseInteraction.Neutral && !editing)) return false;
        var release = Document.MomentReleases.FirstOrDefault(r => r.Id == id);
        if (release is null) return false;
        if (editing) release = release with { Position = _releaseDraft!.CanvasPosition };
        _dragRelease = release;
        _releaseState = MomentReleaseInteraction.Drag;
        _hoveredReleaseId = null;
        UpdateMomentReleaseDrag(release.Position);
        return true;
    }

    public void UpdateMomentReleaseDrag(Length? position)
    {
        if (MomentReleaseState != MomentReleaseInteraction.Drag || _dragRelease is null) return;
        var conflict = position is { } p ? MomentReleaseRules.Conflict(Document, p, _dragRelease.Id) : MomentReleaseConflict.NotInsideBeam;
        _releaseDragValid = conflict == MomentReleaseConflict.None;
        _releaseFeedback = MomentReleaseRules.Message(conflict);
        _releasePreview = new(position ?? _releasePreview?.Position ?? _dragRelease.Position, !_releaseDragValid);
        NotifyMomentReleaseState();
    }

    public bool EndMomentReleaseDrag()
    {
        if (MomentReleaseState != MomentReleaseInteraction.Drag || _dragRelease is null) return false;
        if (!_releaseDragValid)
        {
            var feedback = _releaseFeedback;
            if (_releaseDraft is not null)
            {
                _dragRelease = null;
                _releaseDragValid = false;
                _releaseState = MomentReleaseInteraction.EditDraft;
                _releasePreview = _releaseDraft.Preview;
            }
            else CancelMomentReleaseInteraction();
            _releaseFeedback = feedback;
            NotifyMomentReleaseState();
            return false;
        }
        var release = _dragRelease;
        var position = MomentReleasePreview!.Position;
        _dragRelease = null;
        _releaseDragValid = false;
        if (_releaseDraft is not null)
        {
            _releaseState = MomentReleaseInteraction.EditDraft;
            _releaseDraft.ApplyDragPosition(position);
            _releaseFeedback = null;
            NotifyMomentReleaseState();
        }
        else OpenMomentReleaseDraft(release.Id, position);
        return true;
    }

    public bool ConfirmMomentRelease()
    {
        if (IsBusy) return false;
        var draft = _releaseDraft;
        if (!IsMomentReleaseFlyoutVisible || draft is null || !draft.TryGetValue(out var position)) return false;
        var releases = Document.MomentReleases.ToArray();
        var naming = Document.NamingState;
        if (draft.OriginalId is { } id)
        {
            int index = Array.FindIndex(releases, r => r.Id == id);
            if (index < 0) { CancelMomentReleaseInteraction(); return false; }
            releases[index] = releases[index] with { Position = position, Reference = draft.Reference.Current, Name = draft.NameText };
        }
        else
        {
            releases = [.. releases, new EditorMomentRelease(Guid.NewGuid(), position, draft.NameText) { Reference = draft.Reference.Current }];
            naming = naming.Consume(draft.AutoCandidate!.Value);
        }
        var document = Document.WithMomentReleases(releases, naming);
        CancelMomentReleaseInteraction();
        Commit(document);
        return true;
    }

    public void DeleteMomentRelease()
    {
        if (IsBusy || MomentReleaseState != MomentReleaseInteraction.EditDraft || _releaseDraft?.OriginalId is not { } id
            || !Document.MomentReleases.Any(r => r.Id == id)) return;
        var document = Document.WithMomentReleases(Document.MomentReleases.Where(r => r.Id != id));
        CancelMomentReleaseInteraction();
        Commit(document);
    }

    public void CancelMomentReleaseInteraction()
    {
        if (_releaseDraft is not null) _releaseDraft.PropertyChanged -= MomentReleaseDraftChanged;
        _releaseDraft = null;
        _releasePreview = null;
        _dragRelease = null;
        _releaseDragValid = false;
        _hoveredReleaseId = null;
        _releaseFeedback = null;
        _releaseState = MomentReleaseInteraction.Neutral;
        NotifyMomentReleaseState();
    }

    private void CancelMomentReleaseDrag()
    {
        if (MomentReleaseState != MomentReleaseInteraction.Drag) return;
        if (_releaseDraft is null) CancelMomentReleaseInteraction();
        else
        {
            _dragRelease = null;
            _releaseDragValid = false;
            _releaseState = MomentReleaseInteraction.EditDraft;
            _releasePreview = _releaseDraft.Preview;
            _releaseFeedback = null;
            NotifyMomentReleaseState();
        }
    }

    private void OpenMomentReleaseDraft(Guid? id, Length position)
    {
        _releaseDraft = new(() => Document, id, position, ResultPresentation.Profile);
        _releaseDraft.PropertyChanged += MomentReleaseDraftChanged;
        _releasePreview = _releaseDraft.Preview;
        _hoveredReleaseId = null;
        _releaseFeedback = null;
        _releaseState = id.HasValue ? MomentReleaseInteraction.EditDraft : MomentReleaseInteraction.NewDraft;
        NotifyMomentReleaseState();
    }

    private void MomentReleaseDraftChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MomentReleaseDraftViewModel.Preview) || _releaseDraft is null
            || MomentReleaseState == MomentReleaseInteraction.Drag) return;
        _releasePreview = _releaseDraft.Preview;
        _releaseFeedback = null;
        NotifyMomentReleaseState();
    }

    private void NotifyMomentReleaseState()
    {
        foreach (string name in new[] { nameof(MomentReleaseState), nameof(IsMomentReleaseTool), nameof(MomentReleaseDraft),
            nameof(MomentReleasePreview), nameof(MomentReleasePreviewName), nameof(HoveredMomentReleaseId),
            nameof(HiddenMomentReleaseId), nameof(IsMomentReleaseFlyoutVisible), nameof(MomentReleaseFeedback),
            nameof(HasMomentReleaseFeedback), nameof(IsEditorNeutral), nameof(HasCoordinate), nameof(CoordinateText) }) Notify(name);
    }
}
