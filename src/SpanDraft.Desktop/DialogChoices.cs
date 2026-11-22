using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.ViewModels;

namespace SpanDraft.Desktop;

/// <summary>
/// The answers of each SpanDraft dialog and what they mean. Only roles are declared here; <see cref="DialogConvention"/>
/// derives order, Enter and Escape from them, so that no dialog decides this on its own.
/// </summary>
internal static class DialogChoices
{
    public static DialogChoice<LeaveDecision>[] Leave() =>
        [new(Strings.Save, LeaveDecision.Save, DialogRole.Primary),
         new(Strings.DontSave, LeaveDecision.Discard, DialogRole.Destructive),
         new(Strings.Cancel, LeaveDecision.Cancel, DialogRole.Cancel)];

    /// <summary>Starting without knowing about unsaved work is not offered: the answers are trying again or leaving.</summary>
    public static DialogChoice<bool>[] RecoveryUnavailable() =>
        [new(Strings.Retry, true, DialogRole.Primary), new(Strings.Quit, false, DialogRole.Cancel)];

    public static DialogChoice<bool>[] Acknowledge() => [new(Strings.OK, true, DialogRole.Primary)];

    /// <summary>Only a list entry of a file that is already missing goes, so this is a plain confirmation.</summary>
    public static DialogChoice<bool>[] RemoveMissingRecent() =>
        [new(Strings.RemoveFromRecent, true, DialogRole.Primary), new(Strings.Cancel, false, DialogRole.Cancel)];

    public static DialogChoice<bool>[] Delete() =>
        [new(Strings.Delete, true, DialogRole.Destructive), new(Strings.Cancel, false, DialogRole.Cancel)];

    /// <summary>Quitting with confirmed but unopened requests is the user's explicit decision to drop them.</summary>
    public static DialogChoice<bool>[] DiscardPendingRequests() =>
        [new(Strings.Discard, true, DialogRole.Destructive), new(Strings.Cancel, false, DialogRole.Cancel)];

    public static DialogChoice<bool>[] ReplaceMaterial() =>
        [new(Strings.ReplaceMaterial, true, DialogRole.Destructive), new(Strings.Cancel, false, DialogRole.Cancel)];

    /// <summary>Renaming is the safe way on; replacing overwrites the existing material and therefore never answers Enter.</summary>
    public static DialogChoice<MaterialNameConflictDecision>[] MaterialNameConflict() =>
        [new(Strings.RenameMaterial, MaterialNameConflictDecision.Rename, DialogRole.Primary),
         new(Strings.ReplaceMaterial, MaterialNameConflictDecision.Replace, DialogRole.Destructive),
         new(Strings.Cancel, MaterialNameConflictDecision.Cancel, DialogRole.Cancel)];
}
