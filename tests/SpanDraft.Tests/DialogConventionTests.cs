using SpanDraft.Desktop;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.ViewModels;
using Xunit;

namespace SpanDraft.Tests;

/// <summary>
/// The convention of all dialogs on the cheap level: which answer stands where, which one Enter confirms and what Escape
/// means is derived from roles, never decided per dialog.
/// </summary>
public sealed class DialogConventionTests
{
    private static string[] Labels<T>(IReadOnlyList<DialogButtonPlan<T>> plan) => plan.Select(b => b.Label).ToArray();

    [Fact]
    public void ThePrimaryActionStandsRightmostCancelDirectlyLeftOfItAndAlternativesFurtherLeft()
    {
        var (plan, dismiss) = DialogConvention.Arrange<string>(
        [
            new("Cancel", "cancel", DialogRole.Cancel), new("Primary", "primary", DialogRole.Primary),
            new("Alt 1", "alt1", DialogRole.Alternative), new("Alt 2", "alt2", DialogRole.Alternative)
        ]);
        Assert.Equal(new[] { "Alt 1", "Alt 2", "Cancel", "Primary" }, Labels(plan));
        Assert.Equal("cancel", dismiss);
    }

    [Fact]
    public void OnlyANonDestructivePrimaryActionIsTheEnterDefault()
    {
        var (plan, _) = DialogConvention.Arrange<string>(
        [
            new("Save", "save", DialogRole.Primary), new("Don't save", "discard", DialogRole.Destructive), new("Cancel", "cancel", DialogRole.Cancel)
        ]);
        Assert.Equal(new[] { "Don't save", "Cancel", "Save" }, Labels(plan));
        Assert.Equal(new[] { false, false, true }, plan.Select(b => b.IsDefault));
        Assert.Equal(new[] { false, true, false }, plan.Select(b => b.IsCancel));
        Assert.Equal(new[] { "destructive", "secondary", "primary" }, plan.Select(b => b.StyleClass));
    }

    [Fact]
    public void ADestructiveActionWithoutAPrimaryStandsRightmostWithoutBecomingTheDefaultAndNothingReplacesIt()
    {
        var (plan, dismiss) = DialogConvention.Arrange<bool>([new("Delete", true, DialogRole.Destructive), new("Cancel", false, DialogRole.Cancel)]);
        Assert.Equal(new[] { "Cancel", "Delete" }, Labels(plan));
        Assert.DoesNotContain(plan, b => b.IsDefault);
        Assert.Equal(new[] { true, false }, plan.Select(b => b.IsCancel));
        Assert.False(dismiss);
    }

    [Fact]
    public void ADialogThatOnlyInformsHasOneButtonThatAlsoAnswersEscape()
    {
        var (plan, dismiss) = DialogConvention.Arrange<bool>([new("OK", true, DialogRole.Primary)]);
        var ok = Assert.Single(plan);
        Assert.True(ok.IsDefault); Assert.True(ok.IsCancel); Assert.True(dismiss);
    }

    [Fact]
    public void DialogsWithoutAnAffirmativeAnswerOrWithAnUncancellableDestructiveOneAreRejected()
    {
        Assert.Throws<ArgumentException>(() => DialogConvention.Arrange<bool>([new("Cancel", false, DialogRole.Cancel)]));
        Assert.Throws<ArgumentException>(() => DialogConvention.Arrange<bool>([new("Delete", true, DialogRole.Destructive)]));
        Assert.Throws<InvalidOperationException>(() => DialogConvention.Arrange<bool>(
            [new("A", true, DialogRole.Primary), new("B", true, DialogRole.Primary), new("C", false, DialogRole.Cancel)]));
    }

    /// <summary>Every dialog of the application, as declared: the same rules for each, whatever its content.</summary>
    public static IEnumerable<object[]> Dialogs() =>
        from culture in new[] { "de-DE", "en-US" }
        from dialog in new[] { "leave", "recovery unavailable", "error", "missing recent", "delete", "replace material", "name conflict" }
        select new object[] { culture, dialog };

    [Theory]
    [MemberData(nameof(Dialogs))]
    public void EveryDialogFollowsTheConvention(string culture, string dialog)
    {
        using var scope = new UiCultureScope(culture);
        switch (dialog)
        {
            case "leave": Check(DialogChoices.Leave(), [Strings.DontSave, Strings.Cancel, Strings.Save], Strings.Save); break;
            case "recovery unavailable": Check(DialogChoices.RecoveryUnavailable(), [Strings.Quit, Strings.Retry], Strings.Retry); break;
            case "error": Check(DialogChoices.Acknowledge(), [Strings.OK], Strings.OK); break;
            case "missing recent": Check(DialogChoices.RemoveMissingRecent(), [Strings.Cancel, Strings.RemoveFromRecent], Strings.RemoveFromRecent); break;
            case "delete": Check(DialogChoices.Delete(), [Strings.Cancel, Strings.Delete], null); break;
            case "replace material": Check(DialogChoices.ReplaceMaterial(), [Strings.Cancel, Strings.ReplaceMaterial], null); break;
            case "name conflict":
                Check(DialogChoices.MaterialNameConflict(), [Strings.ReplaceMaterial, Strings.Cancel, Strings.RenameMaterial], Strings.RenameMaterial); break;
            default: throw new ArgumentOutOfRangeException(nameof(dialog));
        }
    }

    private static void Check<T>(DialogChoice<T>[] choices, string[] order, string? expectedDefault)
    {
        var (plan, dismiss) = DialogConvention.Arrange(choices);
        Assert.Equal(order, Labels(plan));
        Assert.Equal(expectedDefault, plan.SingleOrDefault(b => b.IsDefault)?.Label);
        // The primary or destructive action is rightmost; Cancel, when there is one, directly left of it and the answer to Escape.
        var cancel = plan.SingleOrDefault(b => b.Role == DialogRole.Cancel);
        if (cancel is not null)
        {
            Assert.Same(cancel, plan[^2]); Assert.True(cancel.IsCancel); Assert.Equal(cancel.Result, dismiss);
            Assert.Single(plan, b => b.IsCancel);
        }
        else Assert.True(plan[^1].IsCancel);
        // Enter never answers a destructive action, and no other button takes its place when the primary action is destructive.
        Assert.All(plan.Where(b => b.Role is DialogRole.Destructive or DialogRole.Cancel or DialogRole.Alternative), b => Assert.False(b.IsDefault));
        if (plan[^1].Role == DialogRole.Destructive) Assert.DoesNotContain(plan, b => b.IsDefault);
        Assert.All(plan.Where(b => b.Role == DialogRole.Destructive), b => Assert.Equal("destructive", b.StyleClass));
        Assert.Equal(plan.Count, plan.Select(b => b.Label).Distinct().Count());
    }
}
