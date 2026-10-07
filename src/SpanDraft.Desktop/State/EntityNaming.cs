using System.Globalization;

namespace SpanDraft.Desktop.State;

public enum AutoNameKind { Support, Force, Moment, DistributedLoad }
public enum NameValidationError { None, Empty, Duplicate }
public readonly record struct AutoNameCandidate(AutoNameKind Kind, long Ordinal, string Name);

/// <summary>Project counters, independent of current names. Only successful creation advances them.</summary>
public sealed record NamingState
{
    public NamingState(long nextSupportOrdinal = 1, long nextForceNumber = 1, long nextMomentNumber = 1,
        long nextDistributedLoadNumber = 1)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(nextSupportOrdinal, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(nextForceNumber, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(nextMomentNumber, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(nextDistributedLoadNumber, 1);
        NextSupportOrdinal = nextSupportOrdinal;
        NextForceNumber = nextForceNumber;
        NextMomentNumber = nextMomentNumber;
        NextDistributedLoadNumber = nextDistributedLoadNumber;
    }
    public long NextSupportOrdinal { get; }
    public long NextForceNumber { get; }
    public long NextMomentNumber { get; }
    public long NextDistributedLoadNumber { get; }

    public long Next(AutoNameKind kind) => kind switch
    {
        AutoNameKind.Support => NextSupportOrdinal,
        AutoNameKind.Force => NextForceNumber,
        AutoNameKind.Moment => NextMomentNumber,
        AutoNameKind.DistributedLoad => NextDistributedLoadNumber,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    public NamingState Consume(AutoNameCandidate candidate)
    {
        if (candidate.Ordinal < Next(candidate.Kind) || candidate.Name != EntityNaming.Format(candidate.Kind, candidate.Ordinal))
            throw new ArgumentException("Candidate is stale or inconsistent.", nameof(candidate));
        long next = checked(candidate.Ordinal + 1);
        return candidate.Kind switch
        {
            AutoNameKind.Support => new(next, NextForceNumber, NextMomentNumber, NextDistributedLoadNumber),
            AutoNameKind.Force => new(NextSupportOrdinal, next, NextMomentNumber, NextDistributedLoadNumber),
            AutoNameKind.Moment => new(NextSupportOrdinal, NextForceNumber, next, NextDistributedLoadNumber),
            AutoNameKind.DistributedLoad => new(NextSupportOrdinal, NextForceNumber, NextMomentNumber, next),
            _ => throw new ArgumentOutOfRangeException(nameof(candidate))
        };
    }
}

public static class EntityNaming
{
    public static string Normalize(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return name.Trim();
    }

    public static AutoNameCandidate Peek(EditorDocument document, AutoNameKind kind)
    {
        var occupied = document.NamedEntities.Select(e => e.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
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
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    public static bool TryNormalize(EditorDocument document, string? name, Guid? exclude,
        out string normalized, out NameValidationError error)
    {
        string trimmed = name?.Trim() ?? "";
        normalized = trimmed;
        error = trimmed.Length == 0 ? NameValidationError.Empty
            : document.NamedEntities.Any(e => e.Id != exclude && StringComparer.OrdinalIgnoreCase.Equals(e.Name, trimmed))
                ? NameValidationError.Duplicate : NameValidationError.None;
        return error == NameValidationError.None;
    }
}
