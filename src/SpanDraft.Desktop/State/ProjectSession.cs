namespace SpanDraft.Desktop.State;

public sealed record ProjectRevision(Guid RevisionId, ProjectState State);

/// <summary>Single owner of committed snapshots and savepoint identity. No GUI or file I/O.</summary>
public sealed class ProjectSession
{
    public const int HistoryLimit = 200;
    private readonly List<ProjectRevision> _undo = [];
    private readonly List<ProjectRevision> _redo = [];

    private ProjectSession(ProjectState state, string? filePath, bool saved)
    {
        CurrentRevision = new(Guid.NewGuid(), state);
        FilePath = filePath;
        SavedRevisionId = saved ? CurrentRevision.RevisionId : null;
        UndoHistory = _undo.AsReadOnly();
        RedoHistory = _redo.AsReadOnly();
    }

    public static ProjectSession Create(ProjectState state) => new(state, null, false);
    public static ProjectSession Open(ProjectState state, string filePath) => new(state, filePath, true);
    public static ProjectSession Restore(ProjectState state, string? originalFilePath) => new(state, originalFilePath, false);
    public ProjectRevision CurrentRevision { get; private set; }
    public IReadOnlyList<ProjectRevision> UndoHistory { get; }
    public IReadOnlyList<ProjectRevision> RedoHistory { get; }
    public Guid? SavedRevisionId { get; private set; }
    public string? FilePath { get; private set; }
    public bool IsDirty => CurrentRevision.RevisionId != SavedRevisionId;
    public bool IsBusy { get; private set; }
    public bool CanUndo => !IsBusy && _undo.Count > 0;
    public bool CanRedo => !IsBusy && _redo.Count > 0;
    public event Action<ProjectState, ProjectState, EditorChangeKind>? StateApplied;
    public event Action? Changed;

    public bool Commit(ProjectState next)
    {
        if (IsBusy) return false;
        var previous = CurrentRevision.State;
        var change = EditorChangeClassifier.Classify(previous.Document, next.Document, previous.Presentation, next.Presentation);
        if (change == EditorChangeKind.None) return false;
        var a = previous.Document.NamingState;
        var b = next.Document.NamingState;
        if (b.NextSupportOrdinal < a.NextSupportOrdinal || b.NextForceNumber < a.NextForceNumber
            || b.NextMomentNumber < a.NextMomentNumber || b.NextDistributedLoadNumber < a.NextDistributedLoadNumber)
            throw new ArgumentException("Normal commits cannot move naming counters backwards.", nameof(next));
        _undo.Add(CurrentRevision);
        if (_undo.Count > HistoryLimit) _undo.RemoveAt(0);
        _redo.Clear();
        Apply(new(Guid.NewGuid(), next), change);
        return true;
    }

    public bool Undo() => CanUndo && Move(_undo, _redo);
    public bool Redo() => CanRedo && Move(_redo, _undo);

    private bool Move(List<ProjectRevision> source, List<ProjectRevision> destination)
    {
        var next = source[^1];
        source.RemoveAt(source.Count - 1);
        destination.Add(CurrentRevision);
        var previous = CurrentRevision.State;
        Apply(next, EditorChangeClassifier.Classify(previous.Document, next.State.Document,
            previous.Presentation, next.State.Presentation));
        return true;
    }

    private void Apply(ProjectRevision revision, EditorChangeKind change)
    {
        var previous = CurrentRevision.State;
        CurrentRevision = revision;
        StateApplied?.Invoke(previous, revision.State, change);
        Changed?.Invoke();
    }

    public void MarkSaved(ProjectRevision writtenRevision, string filePath)
    {
        if (writtenRevision.RevisionId != CurrentRevision.RevisionId)
            throw new InvalidOperationException("The project changed during save.");
        FilePath = filePath;
        SavedRevisionId = writtenRevision.RevisionId;
        Changed?.Invoke();
    }

    public void SetBusy(bool busy)
    {
        if (IsBusy == busy) return;
        IsBusy = busy;
        Changed?.Invoke();
    }
}
