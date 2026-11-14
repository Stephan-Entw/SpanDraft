using System.Globalization;

namespace SpanDraft.Desktop.State;

public enum AutoNameKind { Support, Force, Moment, DistributedLoad, MomentRelease }
public enum NameValidationError { None, Empty, Duplicate, Reserved }
public readonly record struct AutoNameCandidate(AutoNameKind Kind, long Ordinal, string Name);

/// <summary>Project counters, independent of current names. Only successful creation advances them.</summary>
public sealed record NamingState
{
    public NamingState(long nextSupportOrdinal = 1, long nextForceNumber = 1, long nextMomentNumber = 1,
        long nextDistributedLoadNumber = 1, long nextMomentReleaseNumber = 1)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(nextSupportOrdinal, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(nextForceNumber, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(nextMomentNumber, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(nextDistributedLoadNumber, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(nextMomentReleaseNumber, 1);
        NextSupportOrdinal = nextSupportOrdinal;
        NextForceNumber = nextForceNumber;
        NextMomentNumber = nextMomentNumber;
        NextDistributedLoadNumber = nextDistributedLoadNumber;
        NextMomentReleaseNumber = nextMomentReleaseNumber;
    }
    public long NextSupportOrdinal { get; }
    public long NextForceNumber { get; }
    public long NextMomentNumber { get; }
    public long NextDistributedLoadNumber { get; }
    public long NextMomentReleaseNumber { get; }

    /// <summary>True when no counter of this state is below the corresponding counter of <paramref name="other"/>.</summary>
    public bool IsNotBehind(NamingState other) => NextSupportOrdinal >= other.NextSupportOrdinal
        && NextForceNumber >= other.NextForceNumber && NextMomentNumber >= other.NextMomentNumber
        && NextDistributedLoadNumber >= other.NextDistributedLoadNumber
        && NextMomentReleaseNumber >= other.NextMomentReleaseNumber;

    public long Next(AutoNameKind kind) => kind switch
    {
        AutoNameKind.Support => NextSupportOrdinal,
        AutoNameKind.Force => NextForceNumber,
        AutoNameKind.Moment => NextMomentNumber,
        AutoNameKind.DistributedLoad => NextDistributedLoadNumber,
        AutoNameKind.MomentRelease => NextMomentReleaseNumber,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    public NamingState Consume(AutoNameCandidate candidate)
    {
        if (candidate.Ordinal < Next(candidate.Kind) || candidate.Name != EntityNaming.Format(candidate.Kind, candidate.Ordinal))
            throw new ArgumentException("Candidate is stale or inconsistent.", nameof(candidate));
        long next = checked(candidate.Ordinal + 1);
        return candidate.Kind switch
        {
            AutoNameKind.Support => With(support: next),
            AutoNameKind.Force => With(force: next),
            AutoNameKind.Moment => With(moment: next),
            AutoNameKind.DistributedLoad => With(distributed: next),
            AutoNameKind.MomentRelease => With(release: next),
            _ => throw new ArgumentOutOfRangeException(nameof(candidate))
        };
    }

    private NamingState With(long? support = null, long? force = null, long? moment = null, long? distributed = null,
        long? release = null) => new(support ?? NextSupportOrdinal, force ?? NextForceNumber, moment ?? NextMomentNumber,
        distributed ?? NextDistributedLoadNumber, release ?? NextMomentReleaseNumber);
}

public static class EntityNaming
{
    public static bool IsReserved(string name) => StringComparer.OrdinalIgnoreCase.Equals(name, EditorSelfWeight.Symbol);
    public static string Normalize(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return name.Trim();
    }

    public static AutoNameCandidate Peek(EditorDocument document, AutoNameKind kind)
    {
        var occupied = document.NamedEntities.Select(e => e.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        occupied.Add(EditorSelfWeight.Symbol);
        long ordinal = document.NamingState.Next(kind);
        while (occupied.Contains(Format(kind, ordinal))) ordinal = checked(ordinal + 1);
        return new(kind, ordinal, Format(kind, ordinal));
    }

    public static string Format(AutoNameKind kind, long ordinal)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(ordinal, 1);
        if (kind == AutoNameKind.Support)
        {
            string result = "";
            do
            {
                ordinal--;
                result = (char)('A' + ordinal % 26) + result;
                ordinal /= 26;
            } while (ordinal > 0);
            return result;
        }
        return kind switch
        {
            AutoNameKind.Force => "F" + ordinal.ToString(CultureInfo.InvariantCulture),
            AutoNameKind.Moment => "M" + ordinal.ToString(CultureInfo.InvariantCulture),
            AutoNameKind.DistributedLoad => "q" + ordinal.ToString(CultureInfo.InvariantCulture),
            // G for Gelenk; never the bare reserved self-weight symbol, which has no number.
            AutoNameKind.MomentRelease => "G" + ordinal.ToString(CultureInfo.InvariantCulture),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    public static bool TryNormalize(EditorDocument document, string? name, Guid? exclude,
        out string normalized, out NameValidationError error)
    {
        string trimmed = name?.Trim() ?? "";
        normalized = trimmed;
        error = trimmed.Length == 0 ? NameValidationError.Empty
            : IsReserved(trimmed) && !document.NamedEntities.Any(e => e.Id == exclude && e.Name == trimmed)
                ? NameValidationError.Reserved
            : document.NamedEntities.Any(e => e.Id != exclude && StringComparer.OrdinalIgnoreCase.Equals(e.Name, trimmed))
                ? NameValidationError.Duplicate : NameValidationError.None;
        return error == NameValidationError.None;
    }
}
