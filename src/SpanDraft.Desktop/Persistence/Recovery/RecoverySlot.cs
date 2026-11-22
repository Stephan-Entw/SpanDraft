using System.Globalization;

namespace SpanDraft.Desktop.Persistence;

/// <summary>Identity of one recovery slot generation; defined by the controlled slot file name.</summary>
public readonly record struct RecoveryId(Guid Value) : IComparable<RecoveryId>
{
    public static RecoveryId New() => new(Guid.NewGuid());

    /// <summary>The canonical lowercase 32-digit form used in slot names.</summary>
    public override string ToString() => Value.ToString("N", CultureInfo.InvariantCulture);

    /// <summary>Accepts exactly the canonical form, so every id has one file name.</summary>
    public static bool TryParse(string text, out RecoveryId id)
    {
        if (text.Length == 32 && Guid.TryParseExact(text, "N", out var guid) && guid.ToString("N", CultureInfo.InvariantCulture) == text)
        {
            id = new(guid);
            return true;
        }
        id = default;
        return false;
    }

    /// <summary>The stable tie-break order of the catalog.</summary>
    public int CompareTo(RecoveryId other) => string.CompareOrdinal(ToString(), other.ToString());
}

public enum RecoverySlotKind { Project, Setup }

/// <summary>
/// A controlled recovery slot name. <see cref="Id"/> is null for the two legacy single slots, which are only
/// detected and moved to an identified slot when they are claimed.
/// </summary>
public readonly record struct RecoverySlot(RecoverySlotKind Kind, RecoveryId? Id)
{
    public const string LegacyProjectFileName = "recovery.json";
    public const string LegacySetupFileName = "setup-recovery.json";
    private const string ProjectPrefix = "project-", SetupPrefix = "setup-", Extension = ".json";

    public static RecoverySlot Project(RecoveryId id) => new(RecoverySlotKind.Project, id);
    public static RecoverySlot Setup(RecoveryId id) => new(RecoverySlotKind.Setup, id);
    public static RecoverySlot LegacyProject { get; } = new(RecoverySlotKind.Project, null);
    public static RecoverySlot LegacySetup { get; } = new(RecoverySlotKind.Setup, null);

    public bool IsLegacy => Id is null;

    public string FileName => Id is { } id
        ? (Kind == RecoverySlotKind.Project ? ProjectPrefix : SetupPrefix) + id + Extension
        : Kind == RecoverySlotKind.Project ? LegacyProjectFileName : LegacySetupFileName;

    /// <summary>The same slot kind under a new identity; the way a legacy slot is claimed.</summary>
    public RecoverySlot WithId(RecoveryId id) => new(Kind, id);

    /// <summary>Recognizes only controlled active-slot names; temporary files and everything else are ignored.</summary>
    public static bool TryParse(string fileName, out RecoverySlot slot)
    {
        slot = default;
        if (fileName == LegacyProjectFileName) { slot = LegacyProject; return true; }
        if (fileName == LegacySetupFileName) { slot = LegacySetup; return true; }
        if (!fileName.EndsWith(Extension, StringComparison.Ordinal)) return false;
        string stem = fileName[..^Extension.Length];
        RecoverySlotKind kind;
        if (stem.StartsWith(ProjectPrefix, StringComparison.Ordinal)) { kind = RecoverySlotKind.Project; stem = stem[ProjectPrefix.Length..]; }
        else if (stem.StartsWith(SetupPrefix, StringComparison.Ordinal)) { kind = RecoverySlotKind.Setup; stem = stem[SetupPrefix.Length..]; }
        else return false;
        if (!RecoveryId.TryParse(stem, out var id)) return false;
        slot = new(kind, id);
        return true;
    }

    public override string ToString() => FileName;
}
