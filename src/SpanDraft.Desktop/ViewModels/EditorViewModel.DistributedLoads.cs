using System.ComponentModel;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.State;

namespace SpanDraft.Desktop.ViewModels;

public sealed partial class EditorViewModel
{
    private DistributedLoadInteraction _distributedLoadState;
    private bool _distributedLoadTool;
    private Length? _distributedFirstEndpoint, _distributedPointerPosition;
    private DistributedLoadDraftViewModel? _distributedLoadDraft;
    private DistributedLoadPreview? _distributedLoadPreview;
    private Guid? _hoveredDistributedLoadId;
    private DistributedLoadEndpoint _distributedDragEndpoint;
    private bool _distributedDragValid, _distributedDragFromNeutral;
    private Length? _distributedDragPosition;
    private string? _distributedLoadFeedback;

    public ActionCommand DistributedLoadToolCommand { get; }
    public ActionCommand ConfirmDistributedLoadCommand { get; }
    public ActionCommand CancelDistributedLoadCommand { get; }
    public ActionCommand DeleteDistributedLoadCommand { get; }
    public DistributedLoadInteraction DistributedLoadState => _distributedLoadState;
    public bool IsDistributedLoadTool => _distributedLoadTool;
    public Length? DistributedFirstEndpoint => _distributedFirstEndpoint;
    public Length? DistributedPointerPosition => _distributedPointerPosition;
    public DistributedLoadDraftViewModel? DistributedLoadDraft => _distributedLoadDraft;
    public DistributedLoadPreview? DistributedLoadPreview => _distributedLoadPreview;
    public Guid? HoveredDistributedLoadId => _hoveredDistributedLoadId;
    public Guid? HiddenDistributedLoadId => _distributedLoadDraft?.OriginalId;
    public string DistributedLoadPreviewName => _distributedLoadDraft?.NameText
        ?? (_distributedLoadTool ? EntityNaming.Peek(Document, AutoNameKind.DistributedLoad).Name : "");
    public bool IsDistributedLoadFlyoutVisible => _distributedLoadDraft is not null
        && DistributedLoadState is DistributedLoadInteraction.NewDraft or DistributedLoadInteraction.EditDraft;
    public string? DistributedLoadFeedback => _distributedLoadFeedback;
    public bool HasDistributedLoadFeedback => DistributedLoadFeedback is not null;

    public bool CanDistributedLoadRange(double start, double end) =>
        CanLoadPosition(start) && CanLoadPosition(end) && start < end;

    public void ToggleDistributedLoadTool()
    {
        bool deactivate = _distributedLoadTool;
        CancelEditorInteraction();
        if (deactivate) return;
        _distributedLoadTool = true;
        _distributedLoadState = DistributedLoadInteraction.Placement;
        NotifyDistributedLoadState();
    }

    public void HoverDistributedLoadPlacement(Length? position)
    {
        if (DistributedLoadState != DistributedLoadInteraction.Placement) return;
        _distributedPointerPosition = position;
        _distributedLoadPreview = _distributedFirstEndpoint is { } first && position is { } second
            ? new(first.Meters <= second.Meters ? first : second, first.Meters <= second.Meters ? second : first,
                -500, !CanDistributedLoadRange(Math.Min(first.Meters, second.Meters), Math.Max(first.Meters, second.Meters))) : null;
        _distributedLoadFeedback = _distributedLoadPreview?.IsInvalid == true ? Strings.InvalidDistributedRange : null;
        NotifyDistributedLoadState();
    }

    public bool PlaceDistributedLoadEndpoint()
    {
        if (DistributedLoadState != DistributedLoadInteraction.Placement || _distributedPointerPosition is not { } position
            || !CanLoadPosition(position.Meters)) return false;
        if (_distributedFirstEndpoint is null)
        {
            _distributedFirstEndpoint = position;
            HoverDistributedLoadPlacement(position);
            return true;
        }
        if (_distributedLoadPreview is not { IsInvalid: false } preview) return false;
        OpenDistributedLoadDraft(null, preview.StartPosition, preview.EndPosition, preview.Intensity);
        return true;
    }

    public void HoverDistributedLoad(Guid? id)
    {
        if (!IsEditorNeutral || _hoveredDistributedLoadId == id) return;
        _hoveredDistributedLoadId = id;
        NotifyDistributedLoadState();
    }

    public bool EditDistributedLoad(Guid id)
    {
        if (DistributedLoadState == DistributedLoadInteraction.EditDraft && DistributedLoadDraft?.OriginalId == id) return true;
        if (!IsEditorNeutral) return false;
        var load = Document.DistributedLoads.FirstOrDefault(l => l.Id == id);
        if (load is null) return false;
        OpenDistributedLoadDraft(id, load.StartPosition, load.EndPosition, load.Intensity.NewtonsPerMeter);
        return true;
    }

    public bool BeginDistributedLoadDrag(Guid? id, DistributedLoadEndpoint endpoint)
    {
        if (!Enum.IsDefined(endpoint) || Interaction != SupportInteraction.Neutral || LoadState != LoadInteraction.Neutral) return false;
        bool editing = IsDistributedLoadFlyoutVisible && DistributedLoadDraft?.OriginalId == id;
        if (!IsEditorNeutral && !editing) return false;
        _distributedDragFromNeutral = !editing;
        if (!editing)
        {
            if (id is null || !EditDistributedLoad(id.Value)) return false;
        }
        _distributedDragEndpoint = endpoint;
        _distributedDragValid = true;
        _distributedDragPosition = endpoint == DistributedLoadEndpoint.Start
            ? DistributedLoadDraft!.Preview.StartPosition : DistributedLoadDraft!.Preview.EndPosition;
        _distributedLoadState = DistributedLoadInteraction.Drag;
        _hoveredDistributedLoadId = null;
        NotifyDistributedLoadState();
        return true;
    }

    public void UpdateDistributedLoadDrag(Length? position)
    {
        if (DistributedLoadState != DistributedLoadInteraction.Drag || DistributedLoadDraft is not { } draft) return;
        var candidate = position is { } p ? _distributedDragEndpoint == DistributedLoadEndpoint.Start
            ? draft.Preview with { StartPosition = p } : draft.Preview with { EndPosition = p } : null;
        _distributedDragValid = candidate is not null && CanDistributedLoadRange(candidate.StartPosition.Meters, candidate.EndPosition.Meters);
        _distributedDragPosition = _distributedDragValid ? position : null;
        _distributedLoadFeedback = _distributedDragValid ? null : Strings.InvalidDistributedRange;
        // Never draw crossed endpoints, even while the pointer is beyond the opposite endpoint.
        _distributedLoadPreview = (_distributedDragValid ? candidate! : _distributedLoadPreview ?? draft.Preview)
            with { IsInvalid = !_distributedDragValid || !draft.IsValid };
        NotifyDistributedLoadState();
    }

    public bool EndDistributedLoadDrag()
    {
        if (DistributedLoadState != DistributedLoadInteraction.Drag || DistributedLoadDraft is not { } draft) return false;
        bool valid = _distributedDragValid && _distributedDragPosition is not null;
        if (!valid && _distributedDragFromNeutral)
        {
            CancelDistributedLoadInteraction();
            return false;
        }
        _distributedLoadState = draft.IsExisting ? DistributedLoadInteraction.EditDraft : DistributedLoadInteraction.NewDraft;
        if (valid) draft.ApplyDragEndpoint(_distributedDragEndpoint, _distributedDragPosition!.Value);
        _distributedLoadPreview = draft.Preview;
        _distributedDragPosition = null;
        _distributedDragValid = _distributedDragFromNeutral = false;
        _distributedLoadFeedback = valid ? null : Strings.InvalidDistributedRange;
        NotifyDistributedLoadState();
        return valid;
    }

    public bool ConfirmDistributedLoad()
    {
        if (IsBusy) return false;
        var draft = DistributedLoadDraft;
        if (!IsDistributedLoadFlyoutVisible || draft is null || !draft.TryGetValues(out var start, out var end, out double intensity)
            || !CanDistributedLoadRange(start.Meters, end.Meters)) return false;
        var loads = Document.DistributedLoads.ToArray();
        var naming = Document.NamingState;
        if (draft.OriginalId is { } id)
        {
            int index = Array.FindIndex(loads, l => l.Id == id);
            if (index < 0) { CancelDistributedLoadInteraction(); return false; }
            loads[index] = loads[index] with { StartPosition = start, EndPosition = end,
                Intensity = ForcePerLength.FromNewtonsPerMeter(intensity), Name = draft.NameText };
        }
        else
        {
            loads = [.. loads, new(Guid.NewGuid(), start, end, ForcePerLength.FromNewtonsPerMeter(intensity), draft.NameText)];
            naming = naming.Consume(draft.AutoCandidate!.Value);
        }
        var document = Document.WithDistributedLoads(loads, naming);
        CancelDistributedLoadInteraction();
        Commit(document);
        return true;
    }

    public void DeleteDistributedLoad()
    {
        if (IsBusy || DistributedLoadState != DistributedLoadInteraction.EditDraft || DistributedLoadDraft?.OriginalId is not { } id
            || !Document.DistributedLoads.Any(l => l.Id == id)) return;
        var document = Document.WithDistributedLoads(Document.DistributedLoads.Where(l => l.Id != id));
        CancelDistributedLoadInteraction();
        Commit(document);
    }

    public void CancelDistributedLoadInteraction()
    {
        if (_distributedLoadDraft is not null) _distributedLoadDraft.PropertyChanged -= DistributedLoadDraftChanged;
        _distributedLoadDraft = null;
        _distributedLoadPreview = null;
        _distributedFirstEndpoint = _distributedPointerPosition = _distributedDragPosition = null;
        _distributedLoadTool = _distributedDragValid = _distributedDragFromNeutral = false;
        _hoveredDistributedLoadId = null;
        _distributedLoadFeedback = null;
        _distributedLoadState = DistributedLoadInteraction.Neutral;
        NotifyDistributedLoadState();
    }

    private void OpenDistributedLoadDraft(Guid? id, Length start, Length end, double intensity)
    {
        _distributedLoadDraft = new(() => Document, id, start, end, intensity, ResultPresentation.Profile);
        _distributedLoadDraft.PropertyChanged += DistributedLoadDraftChanged;
        _distributedLoadPreview = _distributedLoadDraft.Preview;
        _distributedPointerPosition = null;
        _distributedLoadFeedback = null;
        _hoveredDistributedLoadId = null;
        _distributedLoadState = id.HasValue ? DistributedLoadInteraction.EditDraft : DistributedLoadInteraction.NewDraft;
        NotifyDistributedLoadState();
    }

    private void DistributedLoadDraftChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DistributedLoadDraftViewModel.Preview) || _distributedLoadDraft is null
            || DistributedLoadState == DistributedLoadInteraction.Drag) return;
        _distributedLoadPreview = _distributedLoadDraft.Preview;
        _distributedLoadFeedback = null;
        NotifyDistributedLoadState();
    }

    private void NotifyDistributedLoadState()
    {
        foreach (string name in new[] { nameof(DistributedLoadState), nameof(IsDistributedLoadTool), nameof(DistributedFirstEndpoint),
            nameof(DistributedPointerPosition), nameof(DistributedLoadDraft), nameof(DistributedLoadPreview), nameof(DistributedLoadPreviewName),
            nameof(HoveredDistributedLoadId), nameof(HiddenDistributedLoadId), nameof(IsDistributedLoadFlyoutVisible),
            nameof(DistributedLoadFeedback), nameof(HasDistributedLoadFeedback), nameof(HasCoordinate), nameof(CoordinateText) }) Notify(name);
    }
}
