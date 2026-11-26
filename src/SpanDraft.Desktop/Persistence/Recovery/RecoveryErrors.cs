namespace SpanDraft.Desktop.Persistence;

/// <summary>
/// The one classification of recovery file errors. A storage error means a slot or the area cannot be reached (read,
/// written, listed, moved or deleted); a content error means what was read, or a path inside it, is invalid. Anything
/// else is a programming error and is never swallowed here.
/// </summary>
internal static class RecoveryErrors
{
    public static bool IsStorage(Exception e) => e is IOException or UnauthorizedAccessException or System.Security.SecurityException;

    public static bool IsContent(Exception e) => e is ProjectFormatException or System.Text.Json.JsonException
        or ArgumentException or NotSupportedException;

    /// <summary>A defect of one slot or of one original path: that candidate is damaged or unresolved, the others are unaffected.</summary>
    public static bool IsCandidateDefect(Exception e) => IsStorage(e) || IsContent(e);

    /// <summary>
    /// The active source slot of a discard does not exist (it was never published or is already gone). Only the storage
    /// reports this, and only after it established that the source itself is missing; a failure of the Discarded target
    /// is never one.
    /// </summary>
    public static bool IsMissing(Exception e) => e is RecoverySlotMissingException;
}

/// <summary>The active source slot of a storage operation does not exist; see <see cref="RecoveryErrors.IsMissing"/>.</summary>
public sealed class RecoverySlotMissingException(string fileName)
    : FileNotFoundException("The recovery slot does not exist.", fileName);
