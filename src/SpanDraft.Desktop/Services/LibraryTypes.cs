using System.Text.Json;
using SpanDraft.Desktop.Persistence;

namespace SpanDraft.Desktop.Services;

public enum LibraryFailureKind { NotLoaded, Format, Io }

/// <summary>Why a library is unavailable. Technical only: the UI translates it into its own messages.</summary>
public sealed record LibraryFailure(LibraryFailureKind Kind, Exception? Cause = null)
{
    public static readonly LibraryFailure NotLoaded = new(LibraryFailureKind.NotLoaded);

    public static bool IsFailure(Exception e) => e is IOException or UnauthorizedAccessException or LibraryFormatException
        or JsonException or ArgumentException or NotSupportedException or OverflowException;

    public static LibraryFailure From(Exception e) =>
        new(e is IOException or UnauthorizedAccessException ? LibraryFailureKind.Io : LibraryFailureKind.Format, e);
}

/// <summary>One immutable published state: either a snapshot (Available) or a failure (Unavailable), never an empty writable fallback.</summary>
public sealed record LibraryState<TLibrary>(TLibrary? Snapshot, long Revision, LibraryFailure? Failure) where TLibrary : class
{
    public bool IsAvailable => Failure is null;
}

public enum LibraryConflictKind { AlreadyExists, OriginalMissing, OriginalChanged, TargetNameTaken }

public enum LibraryMutationOutcome { Committed, Unavailable, Conflict, Invalid, PersistFailed }

public sealed record LibraryMutationResult(LibraryMutationOutcome Outcome, long Revision = 0,
    LibraryConflictKind? Conflict = null, Exception? Failure = null)
{
    public bool Succeeded => Outcome == LibraryMutationOutcome.Committed;
    internal static LibraryMutationResult Committed(long revision) => new(LibraryMutationOutcome.Committed, revision);
    internal static LibraryMutationResult Unavailable() => new(LibraryMutationOutcome.Unavailable);
    internal static LibraryMutationResult Conflicted(LibraryConflictKind kind) => new(LibraryMutationOutcome.Conflict, Conflict: kind);
    internal static LibraryMutationResult Invalid(Exception e) => new(LibraryMutationOutcome.Invalid, Failure: e);
    internal static LibraryMutationResult PersistFailed(Exception e) => new(LibraryMutationOutcome.PersistFailed, Failure: e);
}

public sealed record LibraryChangedEventArgs(long Revision);
