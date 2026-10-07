using System.ComponentModel;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.State;

namespace SpanDraft.Desktop.ViewModels;

public sealed partial class EditorViewModel
{
    private LoadInteraction _loadState;
    private PointLoadKind? _loadTool;
    private PointLoadDraftViewModel? _loadDraft;
    private PointLoadPreview? _loadPreview;
    private Guid? _hoveredLoadId;
    private EditorPointLoad? _dragLoad;
    private bool _loadDragValid;
    private string? _loadFeedback;

    public ActionCommand ForceToolCommand { get; }
    public ActionCommand MomentToolCommand { get; }
    public ActionCommand ConfirmLoadCommand { get; }
    public ActionCommand CancelLoadCommand { get; }
    public ActionCommand DeleteLoadCommand { get; }
    public LoadInteraction LoadState => _loadState;
    public PointLoadKind? LoadTool => _loadTool;
    public bool IsForceTool => LoadTool == PointLoadKind.Force;
    public bool IsMomentTool => LoadTool == PointLoadKind.Moment;
    public PointLoadDraftViewModel? LoadDraft => _loadDraft;
    public PointLoadPreview? LoadPreview => _loadPreview;
    public string LoadPreviewName => _loadDraft?.NameText ?? _dragLoad?.Name
        ?? (LoadTool is { } kind ? EntityNaming.Peek(Document,
            kind == PointLoadKind.Force ? AutoNameKind.Force : AutoNameKind.Moment).Name : "");
    public Guid? HoveredLoadId => _hoveredLoadId;
    public Guid? HiddenLoadId => _loadDraft?.OriginalId ?? _dragLoad?.Id;
    public bool IsLoadFlyoutVisible => _loadDraft is not null && LoadState is LoadInteraction.NewDraft or LoadInteraction.EditDraft;
    public string? LoadFeedback => _loadFeedback;
    public bool HasLoadFeedback => !string.IsNullOrEmpty(LoadFeedback);

    public void CancelEditorInteraction()
    {
        InteractionsCancelling?.Invoke();
        ClearAnnotationPreview();
        DimensionLength.Cancel();
        CancelSupportInteraction();
        CancelLoadInteraction();
        CancelDistributedLoadInteraction();
    }

    /// <summary>Native dialogs may take pointer capture. Restore a gesture's draft without discarding its buffers.</summary>
    public void CancelPointerDrag()
    {
        ClearAnnotationPreview();
        if (Interaction == SupportInteraction.Drag)
        {
            if (_draft is null) CancelSupportInteraction();
            else
            {
                _dragSupport = null;
                _dragValid = false;
                _interaction = SupportInteraction.EditDraft;
                _preview = _draft.Preview;
                _supportFeedback = null;
                NotifySupportState();
            }
        }
        if (LoadState == LoadInteraction.Drag)
        {
            if (_loadDraft is null) CancelLoadInteraction();
            else
            {
                _dragLoad = null;
                _loadDragValid = false;
                _loadState = LoadInteraction.EditDraft;
                _loadPreview = _loadDraft.Preview;
                _loadFeedback = null;
                NotifyLoadState();
            }
        }
        if (DistributedLoadState == DistributedLoadInteraction.Drag)
        {
            if (_distributedDragFromNeutral) CancelDistributedLoadInteraction();
            else
            {
                _distributedLoadState = _distributedLoadDraft!.IsExisting
                    ? DistributedLoadInteraction.EditDraft : DistributedLoadInteraction.NewDraft;
                _distributedLoadPreview = _distributedLoadDraft.Preview;
                _distributedDragPosition = null;
                _distributedDragValid = _distributedDragFromNeutral = false;
                _distributedLoadFeedback = null;
                NotifyDistributedLoadState();
            }
        }
    }

    public void ToggleLoadTool(PointLoadKind kind)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        bool deactivate = LoadTool == kind;
        CancelEditorInteraction();
        if (deactivate) return;
        _loadTool = kind;
        _loadState = LoadInteraction.Placement;
        NotifyLoadState();
    }

    public bool CanLoadPosition(double meters) => double.IsFinite(meters) && meters >= 0 && meters <= Document.Length.Meters;

    public void HoverLoadPlacement(Length? position)
    {
        if (LoadState != LoadInteraction.Placement) return;
        _loadPreview = position is { } p
            ? new(p, LoadTool!.Value, LoadTool == PointLoadKind.Force ? -1000 : 100, !CanLoadPosition(p.Meters)) : null;
        _loadFeedback = _loadPreview?.IsInvalid == true ? Strings.PositionInsideBeam : null;
        NotifyLoadState();
    }

    public bool PlaceLoad()
    {
        if (LoadState != LoadInteraction.Placement || LoadPreview is not { IsInvalid: false } p
            || !CanLoadPosition(p.Position.Meters)) return false;
        OpenLoadDraft(null, p.Kind, p.Position, p.Value);
        return true;
    }

    public void HoverLoad(Guid? id)
    {
        if (!IsEditorNeutral || _hoveredLoadId == id) return;
        _hoveredLoadId = id;
        _loadFeedback = null;
        NotifyLoadState();
    }

    public bool EditLoad(Guid id)
    {
        if (LoadState == LoadInteraction.EditDraft && LoadDraft?.OriginalId == id) return true;
        if (!IsEditorNeutral) return false;
        var load = Document.Loads.FirstOrDefault(l => l.Id == id);
        if (load is null) return false;
        OpenLoadDraft(id, load.Kind, load.Position, load.Value);
        return true;
    }

    public bool BeginLoadDrag(Guid id)
    {
        bool editing = LoadState == LoadInteraction.EditDraft && LoadDraft?.OriginalId == id;
        if (Interaction != SupportInteraction.Neutral || (LoadState != LoadInteraction.Neutral && !editing)
            || DistributedLoadState != DistributedLoadInteraction.Neutral) return false;
        var load = Document.Loads.FirstOrDefault(l => l.Id == id);
        if (load is null) return false;
        if (editing) load = EditorPointLoad.Create(id, LoadDraft!.CanvasPosition, load.Kind, LoadDraft.Preview.Value, load.Name);
        _dragLoad = load;
        _loadState = LoadInteraction.Drag;
        _hoveredLoadId = null;
        UpdateLoadDrag(load.Position);
        return true;
    }

    public void UpdateLoadDrag(Length? position)
    {
        if (LoadState != LoadInteraction.Drag || _dragLoad is null) return;
        _loadDragValid = position is { } p && CanLoadPosition(p.Meters);
        _loadFeedback = _loadDragValid ? null : Strings.PositionInsideBeam;
        _loadPreview = new(position ?? _loadPreview?.Position ?? _dragLoad.Position,
            _dragLoad.Kind, _dragLoad.Value, !_loadDragValid || LoadDraft?.IsValid == false);
        NotifyLoadState();
    }

    public bool EndLoadDrag()
    {
        if (LoadState != LoadInteraction.Drag || _dragLoad is null) return false;
        if (!_loadDragValid)
        {
            var feedback = _loadFeedback;
            if (_loadDraft is not null)
            {
                _dragLoad = null;
                _loadDragValid = false;
                _loadState = LoadInteraction.EditDraft;
                _loadPreview = _loadDraft.Preview;
            }
            else CancelLoadInteraction();
            _loadFeedback = feedback;
            NotifyLoadState();
            return false;
        }
        var load = _dragLoad;
        var position = LoadPreview!.Position;
        _dragLoad = null;
        _loadDragValid = false;
        if (_loadDraft is not null)
        {
            _loadState = LoadInteraction.EditDraft;
            _loadDraft.ApplyDragPosition(position);
            _loadFeedback = null;
            NotifyLoadState();
        }
        else OpenLoadDraft(load.Id, load.Kind, position, load.Value);
        return true;
    }

    public bool ConfirmLoad()
    {
        if (IsBusy) return false;
        var draft = LoadDraft;
        if (!IsLoadFlyoutVisible || draft is null || !draft.TryGetValues(out var position, out double value)
            || !CanLoadPosition(position.Meters)) return false;
        var loads = Document.Loads.ToArray();
        var naming = Document.NamingState;
        if (draft.OriginalId is { } id)
        {
            int index = Array.FindIndex(loads, l => l.Id == id);
            if (index < 0) { CancelLoadInteraction(); return false; }
            loads[index] = EditorPointLoad.Create(id, position, draft.Kind, value, draft.NameText);
        }
        else
        {
            loads = [.. loads, EditorPointLoad.Create(Guid.NewGuid(), position, draft.Kind, value, draft.NameText)];
            naming = naming.Consume(draft.AutoCandidate!.Value);
        }
        var document = Document.WithLoads(loads, naming);
        CancelLoadInteraction();
        Commit(document);
        return true;
    }

    public void DeleteLoad()
    {
        if (IsBusy || LoadState != LoadInteraction.EditDraft || LoadDraft?.OriginalId is not { } id
            || !Document.Loads.Any(l => l.Id == id)) return;
        var document = Document.WithLoads(Document.Loads.Where(l => l.Id != id));
        CancelLoadInteraction();
        Commit(document);
    }

    public void CancelLoadInteraction()
    {
        if (_loadDraft is not null) _loadDraft.PropertyChanged -= LoadDraftChanged;
        _loadDraft = null;
        _loadPreview = null;
        _loadTool = null;
        _dragLoad = null;
        _loadDragValid = false;
        _hoveredLoadId = null;
        _loadFeedback = null;
        _loadState = LoadInteraction.Neutral;
        NotifyLoadState();
    }

    private void OpenLoadDraft(Guid? id, PointLoadKind kind, Length position, double value)
    {
        _loadDraft = new(() => Document, id, kind, position, value);
        _loadDraft.PropertyChanged += LoadDraftChanged;
        _loadPreview = _loadDraft.Preview;
        _hoveredLoadId = null;
        _loadFeedback = null;
        _loadState = id.HasValue ? LoadInteraction.EditDraft : LoadInteraction.NewDraft;
        NotifyLoadState();
    }

    private void LoadDraftChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(PointLoadDraftViewModel.Preview) || _loadDraft is null || LoadState == LoadInteraction.Drag) return;
        _loadPreview = _loadDraft.Preview;
        _loadFeedback = null;
        NotifyLoadState();
    }

    private void NotifyLoadState()
    {
        foreach (string name in new[] { nameof(LoadState), nameof(LoadTool), nameof(IsForceTool), nameof(IsMomentTool),
            nameof(LoadDraft), nameof(LoadPreview), nameof(HoveredLoadId), nameof(HiddenLoadId),
            nameof(LoadPreviewName), nameof(IsLoadFlyoutVisible), nameof(LoadFeedback), nameof(HasLoadFeedback), nameof(HasCoordinate), nameof(CoordinateText) }) Notify(name);
    }
}
