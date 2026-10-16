using SpanDraft.Core.Sections;
using SpanDraft.Desktop.State;

namespace SpanDraft.Desktop.ViewModels;

public sealed partial class SectionDefinitionViewModel
{
    private sealed record DraftSnapshot(string? CustomName, ManualAxisConfiguration Configuration,
        SectionAxisDesignation? Axis, Dictionary<ManualAxisConfiguration, SectionAxisDesignation?> ManualAxes,
        Dictionary<string, SectionDefinitionInput.Snapshot> Inputs);
    private sealed record HistoryState(SectionEditorType? Type, Dictionary<SectionEditorType, DraftSnapshot> Drafts);

    private readonly List<HistoryState> _undo = [], _redo = [];
    private readonly Func<bool>? _preserveInputs;
    private bool _restoringHistory;
    private HistoryState? _beforeInputChange;
    internal bool PreserveInputs => _restoringHistory || IsBusy || _preserveInputs?.Invoke() == true;
    private bool CanUseHistory => !_restoringHistory && !IsBusy && !IsEditingName
        && !_drafts.Values.SelectMany(d => d.Inputs.Values).Any(i => i.IsPending);
    public bool CanUndo => CanUseHistory && _undo.Count > 0;
    public bool CanRedo => CanUseHistory && _redo.Count > 0;

    private HistoryState CaptureHistory() => new(_type, _drafts.ToDictionary(p => p.Key, p =>
        new DraftSnapshot(p.Value.CustomName, p.Value.Configuration, p.Value.Axis,
            new(p.Value.ManualAxisSelections), p.Value.Inputs.ToDictionary(i => i.Key, i => i.Value.Capture()))));

    private static bool SameState(HistoryState a, HistoryState b) => a.Type == b.Type
        && a.Drafts.Count == b.Drafts.Count && a.Drafts.All(p => b.Drafts.TryGetValue(p.Key, out var d)
            && p.Value.CustomName == d.CustomName && p.Value.Configuration == d.Configuration && p.Value.Axis == d.Axis
            && p.Value.ManualAxes.Count == d.ManualAxes.Count
            && p.Value.ManualAxes.All(x => d.ManualAxes.TryGetValue(x.Key, out var axis) && axis == x.Value)
            && p.Value.Inputs.Count == d.Inputs.Count
            && p.Value.Inputs.All(x => d.Inputs.TryGetValue(x.Key, out var input) && input == x.Value));

    private void RecordHistory(HistoryState before)
    {
        if (_restoringHistory || SameState(before, CaptureHistory())) return;
        Push(_undo, before); _redo.Clear(); RefreshHistoryAvailability();
    }

    private static void Push(List<HistoryState> stack, HistoryState state)
    {
        stack.Add(state);
        if (stack.Count > ProjectSession.HistoryLimit) stack.RemoveAt(0);
    }

    public bool Undo() => CanUndo && MoveHistory(_undo, _redo);
    public bool Redo() => CanRedo && MoveHistory(_redo, _undo);

    private bool MoveHistory(List<HistoryState> source, List<HistoryState> target)
    {
        var next = source[^1]; source.RemoveAt(source.Count - 1);
        Push(target, CaptureHistory());
        _restoringHistory = true;
        try
        {
            _drafts.Clear();
            foreach (var (type, snapshot) in next.Drafts)
            {
                var draft = new FormDraft { CustomName = snapshot.CustomName, Configuration = snapshot.Configuration, Axis = snapshot.Axis };
                foreach (var pair in snapshot.ManualAxes) draft.ManualAxisSelections.Add(pair.Key, pair.Value);
                _drafts.Add(type, draft);
                _type = type;
                EnsureInputs();
                // Inactive manual configurations also own confirmed input values.
                foreach (var (key, input) in snapshot.Inputs)
                {
                    if (!draft.Inputs.TryGetValue(key, out var field))
                    {
                        field = new(key, Presentation.Profile[QuantityOf(key)], InputChanged, key == "r", InputLabel(key));
                        field.BeforeCommittedValueChange += () => _beforeInputChange = CaptureHistory();
                        draft.Inputs.Add(key, field);
                    }
                    field.Restore(input);
                }
            }
            _type = next.Type; _choosing = _type is null;
            _editingName = false; _pendingName = ""; _nameError = _saveError = _highlight = null;
            _beforeInputChange = null; _section = null; _axisValues = [];
            RefreshInputs();
            if (_type is not null) { NormalizeAxis(); Recalculate(null); }
            else Refresh();
            Notify(nameof(HighlightedParameter));
        }
        finally { _restoringHistory = false; RefreshHistoryAvailability(); }
        return true;
    }

    private void RefreshHistoryAvailability()
    {
        Notify(nameof(CanUndo)); Notify(nameof(CanRedo));
    }
}
