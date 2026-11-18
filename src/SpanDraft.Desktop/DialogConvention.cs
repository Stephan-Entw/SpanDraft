namespace SpanDraft.Desktop;

/// <summary>What a button of a dialog means; the convention derives order, Enter and Escape from it.</summary>
internal enum DialogRole
{
    /// <summary>The recommended, non-destructive answer. Rightmost and the Enter default.</summary>
    Primary,
    /// <summary>A further non-destructive answer, left of Cancel.</summary>
    Alternative,
    /// <summary>An answer that removes or overwrites something. Never the Enter default; red.</summary>
    Destructive,
    /// <summary>Leaves everything as it is. Directly left of the rightmost button; Escape.</summary>
    Cancel
}

internal sealed record DialogChoice<T>(string Label, T Result, DialogRole Role);

/// <summary>One button of an arranged dialog, from left to right.</summary>
internal sealed record DialogButtonPlan<T>(string Label, T Result, DialogRole Role, bool IsDefault, bool IsCancel)
{
    /// <summary>The style class of the button: the accent only for the primary answer, red for a destructive one.</summary>
    public string StyleClass => Role switch
    {
        DialogRole.Primary => "primary",
        DialogRole.Destructive => "destructive",
        _ => "secondary"
    };
}

/// <summary>
/// The one convention of every SpanDraft dialog, kept out of the callers: the primary action stands rightmost, Cancel directly
/// left of it, further alternatives further left. Enter confirms only a non-destructive primary action; a destructive
/// action is never the default and nothing replaces it, so it must be chosen deliberately. Escape and closing the window
/// always mean Cancel.
/// </summary>
internal static class DialogConvention
{
    public static (IReadOnlyList<DialogButtonPlan<T>> Buttons, T DismissResult) Arrange<T>(IReadOnlyList<DialogChoice<T>> choices)
    {
        var cancel = choices.SingleOrDefault(c => c.Role == DialogRole.Cancel);
        var primary = choices.SingleOrDefault(c => c.Role == DialogRole.Primary);
        // Without a primary answer the first destructive one takes the rightmost place, still without being the default.
        var last = primary ?? choices.FirstOrDefault(c => c.Role == DialogRole.Destructive)
            ?? throw new ArgumentException("A dialog needs a primary or a destructive answer.", nameof(choices));
        if (cancel is null && last.Role == DialogRole.Destructive)
            throw new ArgumentException("A destructive dialog needs a way to cancel.", nameof(choices));
        // A dialog that only informs has no Cancel; its one answer also closes it on Escape.
        bool lastDismisses = cancel is null;
        var buttons = new List<DialogButtonPlan<T>>();
        foreach (var c in choices.Where(c => c != cancel && c != last))
            buttons.Add(new(c.Label, c.Result, c.Role, false, false));
        if (cancel is not null) buttons.Add(new(cancel.Label, cancel.Result, cancel.Role, false, true));
        buttons.Add(new(last.Label, last.Result, last.Role, last.Role == DialogRole.Primary, lastDismisses));
        return (buttons, (cancel ?? last).Result);
    }
}
