namespace SpanDraft.Core.Supports;

/// <summary>Support classification; the solver will assign the corresponding boundary conditions.</summary>
public enum SupportType
{
    /// <summary>Einspannung.</summary>
    Fixed,
    /// <summary>Festlager.</summary>
    Pinned,
    /// <summary>Loslager.</summary>
    Roller
}
