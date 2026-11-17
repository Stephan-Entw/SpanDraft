namespace SpanDraft.Desktop.ViewModels;

/// <summary>
/// Which spring a spring support is edited as. Every spring is a support of its own with exactly one stiffness; the kind
/// decides which stiffness field is shown and which part ends up in the resulting <c>SpringData</c>.
/// </summary>
public enum SpringKind
{
    Translational,
    Rotational
}
