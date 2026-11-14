namespace SpanDraft.Core.Supports;

/// <summary>Support classification; the solver will assign the corresponding boundary conditions.</summary>
public enum SupportType
{
    /// <summary>Einspannung.</summary>
    Fixed,
    /// <summary>Festlager.</summary>
    Pinned,
    /// <summary>Loslager.</summary>
    Roller,
    /// <summary>Federlager: linear-elastic translational and/or rotational spring, axially rigid.</summary>
    Spring
}
