namespace SpanDraft.Desktop.State;

public sealed record ProjectRevision(Guid RevisionId, ProjectState State);

/// <summary>Single owner of committed snapshots and savepoint identity. No GUI or file I/O.</summary>
public sealed class ProjectSession
{
    public const int HistoryLimit = 200;
    private readonly List<ProjectRevision> _undo = [];
    private readonly List<ProjectRevision> _redo = [];

    private ProjectSession(ProjectState state, string? filePath, bool saved, string? recoveryOriginPath = null)
    {
        CurrentRevision = new(Guid.NewGuid(), state);
        FilePath = filePath;
        RecoveryOriginPath = recoveryOriginPath;
        SavedRevisionId = saved ? CurrentRevision.RevisionId : null;
        UndoHistory = _undo.AsReadOnly();
        RedoHistory = _redo.AsReadOnly();
    }

    public static ProjectSession Create(ProjectState state) => new(state, null, false);
    public static ProjectSession FromTemplate(ProjectState state) => new(state, null, true);
    public static ProjectSession Open(ProjectState state, string filePath) => new(state, filePath, true);
    public static ProjectSession Restore(ProjectState state, string? originalFilePath) => new(state, originalFilePath, false);

    /// <summary>
    /// Restores an older recovery variant of a project without claiming its path: <see cref="FilePath"/> stays null and the
    /// original is only remembered as <see cref="RecoveryOriginPath"/>.
    /// </summary>
    public static ProjectSession RestoreDetached(ProjectState state, string? recoveryOriginPath) => new(state, null, false, recoveryOriginPath);
    public ProjectRevision CurrentRevision { get; private set; }
    public IReadOnlyList<ProjectRevision> UndoHistory { get; }
    public IReadOnlyList<ProjectRevision> RedoHistory { get; }
    public Guid? SavedRevisionId { get; private set; }
    public string? FilePath { get; private set; }

    /// <summary>
    /// The path a detached recovery variant came from; it is kept in later recovery writes but is never a save target and
    /// never turns into <see cref="FilePath"/> on its own. Saving to a path ends the detachment.
    /// </summary>
    public string? RecoveryOriginPath { get; private set; }
    public bool IsDirty => CurrentRevision.RevisionId != SavedRevisionId;
    private bool _busy, _frozen;
    internal bool IsFrozen => _frozen;
    public bool IsBusy => _busy || IsFrozen;
    public bool CanUndo => !IsBusy && _undo.Count > 0;
    public bool CanRedo => !IsBusy && _redo.Count > 0;
    public event Action<ProjectState, ProjectState, EditorChangeKind>? StateApplied;
    public event Action? Changed;
    internal event Action? FreezeChanged;

    public bool Commit(ProjectState next)
    {
        if (IsBusy) return false;
        var previous = CurrentRevision.State;
        var change = EditorChangeClassifier.Classify(previous, next);
        if (change == EditorChangeKind.None) return false;
        var a = previous.Document.NamingState;
        var b = next.Document.NamingState;
        if (!b.IsNotBehind(a))
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
        Apply(next, EditorChangeClassifier.Classify(previous, next.State));
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
        PrepareSave(writtenRevision, filePath).MarkSaved();
        NotifySaved();
    }

    internal PreparedSave PrepareSave(ProjectRevision writtenRevision, string filePath)
    {
        if (writtenRevision.RevisionId != CurrentRevision.RevisionId)
            throw new InvalidOperationException("The project changed during save.");
        return new(this, writtenRevision.RevisionId, filePath);
    }

    internal void NotifySaved() => Changed?.Invoke();

    internal sealed class PreparedSave(ProjectSession session, Guid revisionId, string path)
    {
        internal void MarkSaved()
        {
            session.FilePath = path;
            session.RecoveryOriginPath = null;
            session.SavedRevisionId = revisionId;
        }
    }

    public void SetBusy(bool busy)
    {
        if (_busy == busy) return;
        _busy = busy;
        Changed?.Invoke();
    }

    public void SetFrozen(bool frozen)
    {
        if (_frozen == frozen) return;
        _frozen = frozen;
        FreezeChanged?.Invoke();
    }
}
