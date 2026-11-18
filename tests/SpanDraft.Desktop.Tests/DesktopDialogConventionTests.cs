using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SpanDraft.Desktop;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.ViewModels;
using Xunit;
using static SpanDraft.Tests.ProjectTestSupport;

namespace SpanDraft.Tests;

/// <summary>
/// The real dialog windows follow one convention: Cancel directly left of the primary action, Enter only for a non-destructive
/// primary action, Escape and closing always Cancel, a destructive action red and never the default. The matrix of answers
/// itself is checked without windows in <see cref="DialogConventionTests"/>; here it is the Avalonia behavior that counts.
/// </summary>
[Collection("Avalonia")]
public sealed class DesktopDialogConventionTests
{
    private static readonly DateTimeOffset Time = new(2026, 10, 16, 15, 42, 0, TimeSpan.Zero);

    /// <summary>Name, how to open it, the buttons from left to right, the Enter default (or null) and what Cancel answers.</summary>
    private sealed record Case(string Name, Func<IProjectDialogs, Task> Open, string[] Order, string? Default, object CancelResult, object? EnterResult);

    private static Case[] Cases() =>
    [
        new("leave", d => d.ConfirmLeaveAsync("Project.spandraft"), [Strings.DontSave, Strings.Cancel, Strings.Save], Strings.Save, LeaveDecision.Cancel, LeaveDecision.Save),
        new("recovery", d => d.ConfirmRecoveryAsync(false, Time), [Strings.Discard, Strings.Cancel, Strings.Restore], Strings.Restore, RecoveryDecision.Cancel, RecoveryDecision.Restore),
        new("recovery damaged", d => d.ConfirmRecoveryAsync(true, null), [Strings.Cancel, Strings.Discard], null, RecoveryDecision.Cancel, null),
        new("setup recovery", d => d.ConfirmSetupRecoveryAsync(false, Time), [Strings.Discard, Strings.Cancel, Strings.Restore], Strings.Restore, RecoveryDecision.Cancel, RecoveryDecision.Restore),
        new("setup recovery damaged", d => d.ConfirmSetupRecoveryAsync(true, null), [Strings.Cancel, Strings.Discard], null, RecoveryDecision.Cancel, null),
        new("error", d => d.ShowErrorAsync("The file could not be saved."), [Strings.OK], Strings.OK, true, true),
        new("missing recent", d => d.ConfirmRemoveMissingRecentAsync(TestPath("old.spandraft")), [Strings.Cancel, Strings.RemoveFromRecent], Strings.RemoveFromRecent, false, true),
        new("material delete", d => d.ConfirmMaterialDeleteAsync("S355"), [Strings.Cancel, Strings.Delete], null, false, null),
        new("section delete", d => d.ConfirmSectionDeleteAsync("IPE 200"), [Strings.Cancel, Strings.Delete], null, false, null),
        new("name conflict", d => d.ResolveMaterialNameConflictAsync("S355"), [Strings.ReplaceMaterial, Strings.Cancel, Strings.RenameMaterial], Strings.RenameMaterial,
            MaterialNameConflictDecision.Cancel, MaterialNameConflictDecision.Rename),
        new("material replace", d => d.ConfirmMaterialReplaceAsync("S355"), [Strings.Cancel, Strings.ReplaceMaterial], null, false, null)
    ];

    public static IEnumerable<object[]> Names() =>
        from culture in new[] { "de-DE", "en-US" } from name in Cases().Select(c => c.Name) select new object[] { culture, name };

    private static object? ResultOf(Task task) => task.GetType().GetProperty("Result")!.GetValue(task);

    private static (Window Owner, IProjectDialogs Dialogs) Owner()
    {
        var owner = new Window { Width = 1100, Height = 650 }; owner.Show();
        return (owner, new ProjectDialogs(owner));
    }

    private static Button[] Buttons(Window dialog) => dialog.GetVisualDescendants().OfType<Button>().Where(b => b.Content is string)
        .OrderBy(b => b.TranslatePoint(default, dialog)!.Value.X).ToArray();

    private static void Press(Window window, Key key)
    {
        window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key });
        Dispatcher.UIThread.RunJobs(); // the continuation of an async dialog runs on the dispatcher
    }

    private static void Arrange(Window window)
    {
        window.ApplyTemplate();
        for (int i = 0; i < 3; i++)
        {
            window.Measure(new Size(window.Width, 800)); window.Arrange(new Rect(0, 0, window.Width, 800));
            ((Control)window.Content!).InvalidateMeasure(); ((Control)window.Content!).InvalidateArrange();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void EveryDialogOrdersItsButtonsAndAnswersEnterEscapeAndCloseByTheConvention(string culture, string name)
    {
        using var environment = new DesktopControlEnvironment(); using var scope = new UiCultureScope(culture);
        var (owner, dialogs) = Owner();
        var test = Cases().Single(c => c.Name == name);
        try
        {
            // Layout: the buttons stand in the footer below the message, the last one against its right padding.
            var pending = test.Open(dialogs); Dispatcher.UIThread.RunJobs();
            var dialog = Assert.Single(owner.OwnedWindows); Arrange(dialog);
            var buttons = Buttons(dialog);
            Assert.Equal(test.Order, buttons.Select(b => (string)b.Content!));
            // The visual order is the tab order: the children of the panel follow it.
            var panel = (Panel)buttons[0].Parent!;
            Assert.Equal(buttons, panel.Children.OfType<Button>());
            var footer = buttons[0].FindAncestorOfType<Border>()!;
            Assert.Contains("dialogFooter", footer.Classes);
            var message = dialog.GetVisualDescendants().OfType<TextBlock>().First();
            Assert.True(footer.TranslatePoint(default, dialog)!.Value.Y >= message.TranslatePoint(default, dialog)!.Value.Y + message.Bounds.Height);
            double right = footer.Bounds.Width - footer.Padding.Right;
            Assert.Equal(right, buttons[^1].TranslatePoint(default, footer)!.Value.X + buttons[^1].Bounds.Width, 1);
            for (int i = 1; i < buttons.Length; i++)
                Assert.Equal(8, buttons[i].TranslatePoint(default, footer)!.Value.X - buttons[i - 1].TranslatePoint(default, footer)!.Value.X - buttons[i - 1].Bounds.Width, 1);
            Assert.All(buttons, b => { Assert.Equal(buttons[0].Bounds.Height, b.Bounds.Height); Assert.True(b.Bounds.Width >= 96 - .01); });
            // Roles: only the primary action is accented, a destructive one is red and never the default, Cancel is IsCancel.
            Assert.Equal(test.Default, buttons.SingleOrDefault(b => b.IsDefault)?.Content);
            var error = ((ISolidColorBrush)Application.Current!.FindResource("Error")!).Color;
            foreach (var button in buttons)
            {
                bool destructive = (string)button.Content! == Strings.Delete || (string)button.Content! == Strings.DontSave
                    || (string)button.Content! == Strings.ReplaceMaterial || (name.StartsWith("recovery") || name.StartsWith("setup recovery")) && (string)button.Content! == Strings.Discard;
                Assert.Equal(destructive, button.Classes.Contains("destructive"));
                if (destructive) { Assert.False(button.IsDefault); Assert.Equal(error, ((ISolidColorBrush)button.Foreground!).Color); Assert.Equal(error, ((ISolidColorBrush)button.BorderBrush!).Color); }
                Assert.Equal(button.IsDefault, button.Classes.Contains("primary"));
            }
            Assert.Equal(test.Name == "error" ? new[] { Strings.OK } : new[] { Strings.Cancel }, buttons.Where(b => b.IsCancel).Select(b => (string)b.Content!));
            // Enter answers only a default; otherwise the dialog stays and nothing else takes over.
            Press(dialog, Key.Enter); Dispatcher.UIThread.RunJobs();
            if (test.Default is null)
            {
                Assert.False(pending.IsCompleted); Assert.Same(dialog, Assert.Single(owner.OwnedWindows));
                Press(dialog, Key.Escape);
            }
            else Assert.Equal(test.EnterResult, Answer(pending));
            Assert.Equal(test.Default is null ? test.CancelResult : test.EnterResult, Answer(pending));
            Assert.Empty(owner.OwnedWindows);
            // Escape is always Cancel.
            pending = test.Open(dialogs); Dispatcher.UIThread.RunJobs();
            Press(Assert.Single(owner.OwnedWindows), Key.Escape);
            Assert.Equal(test.CancelResult, Answer(pending));
            // Closing the window by its title bar is Cancel too.
            pending = test.Open(dialogs); Dispatcher.UIThread.RunJobs();
            Assert.Single(owner.OwnedWindows).Close(); Dispatcher.UIThread.RunJobs();
            Assert.Equal(test.CancelResult, Answer(pending));
            Assert.Empty(owner.OwnedWindows);
        }
        finally { foreach (var window in owner.OwnedWindows.ToArray()) window.Close(); owner.Close(); }
    }

    /// <summary>What the dialog answered; an async dialog completes shortly after its window closed.</summary>
    private static object? Answer(Task task)
    {
        for (int i = 0; i < 100 && !task.IsCompleted; i++) { Dispatcher.UIThread.RunJobs(); task.Wait(50); }
        Assert.True(task.IsCompleted, "the dialog has answered");
        task.GetAwaiter().GetResult();
        // A dialog without an answer (the error message) completes with a void result: report it as acknowledged.
        return task.GetType().GetProperty("Result")!.GetValue(task) is { } result && result.GetType().Name != "VoidTaskResult" ? result : true;
    }

    /// <summary>The dialogs are windows of one family: the footer, the buttons and the fields of the settings and the material editor match.</summary>
    [Theory]
    [InlineData("de-DE")] [InlineData("en-US")]
    public void SettingsMaterialEditorAndMessageDialogsShareFooterButtonsAndFieldWidth(string culture)
    {
        using var environment = new DesktopControlEnvironment(); using var scope = new UiCultureScope(culture);
        var (owner, dialogs) = Owner();
        try
        {
            var main = new MainWindowViewModel(); main.NewCommand.Execute(null);
            var editor = new MaterialEditorViewModel(main.Setup, null, SetupEditorMode.ProjectDraft);
            var materialWindow = new MaterialEditorWindow(editor); materialWindow.Show(owner);
            var settingsWindow = new SettingsWindow(new SettingsViewModel(UserSettings.Default, _ => Task.FromResult<string?>(null)));
            settingsWindow.Show(owner);
            _ = dialogs.ConfirmMaterialDeleteAsync("S355");
            var messageWindow = owner.OwnedWindows.OfType<Window>().Single(w => w is not MaterialEditorWindow and not SettingsWindow);
            foreach (var window in new Window[] { materialWindow, settingsWindow, messageWindow }) Arrange(window);
            Border Footer(Window w) => w.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("dialogFooter"));
            Button[] Actions(Window w) => Footer(w).GetVisualDescendants().OfType<Button>().OrderBy(b => b.TranslatePoint(default, w)!.Value.X).ToArray();
            var footers = new Window[] { materialWindow, settingsWindow, messageWindow }.Select(Footer).ToArray();
            Assert.All(footers, f =>
            {
                Assert.Equal(footers[0].Padding, f.Padding); Assert.Equal(footers[0].BorderThickness, f.BorderThickness);
                Assert.Equal(((ISolidColorBrush)footers[0].BorderBrush!).Color, ((ISolidColorBrush)f.BorderBrush!).Color);
                Assert.Equal(((ISolidColorBrush)footers[0].Background!).Color, ((ISolidColorBrush)f.Background!).Color);
            });
            foreach (var window in new Window[] { materialWindow, settingsWindow })
            {
                var buttons = Actions(window);
                Assert.Equal(new[] { Strings.Cancel, Strings.OK }, buttons.Select(b => (string)b.Content!));
                Assert.True(buttons[0].IsCancel); Assert.True(buttons[1].IsDefault);
                Assert.Contains("secondary", buttons[0].Classes); Assert.Contains("primary", buttons[1].Classes);
                var reference = Actions(messageWindow)[0];
                Assert.All(buttons, b => { Assert.Equal(reference.Bounds.Height, b.Bounds.Height); Assert.Equal(reference.MinWidth, b.MinWidth); });
                Assert.Equal(buttons[0].Bounds.Width, buttons[1].Bounds.Width, 3);
            }
            // Same inset of the primary action from the right edge and same gap between the buttons in all three windows.
            var insets = new Window[] { materialWindow, settingsWindow, messageWindow }.Select(w =>
            {
                var b = Actions(w); var f = Footer(w);
                return (Right: f.Bounds.Width - (b[^1].TranslatePoint(default, f)!.Value.X + b[^1].Bounds.Width),
                    Gap: b[^1].TranslatePoint(default, f)!.Value.X - (b[^2].TranslatePoint(default, f)!.Value.X + b[^2].Bounds.Width));
            }).ToArray();
            Assert.All(insets, i => { Assert.Equal(insets[0].Right, i.Right, 1); Assert.Equal(insets[0].Gap, i.Gap, 1); });
            // The settings fields have the height of the combo boxes in the material editor (their width is deliberately
            // narrower: they hold unit symbols, not free text), and labels look the same.
            var category = materialWindow.GetVisualDescendants().OfType<ComboBox>().First(c => c.Name == "MaterialCategoryInput");
            var settingsCombos = settingsWindow.GetVisualDescendants().OfType<ComboBox>().Where(c => c.IsVisible).ToArray();
            var unitFields = settingsWindow.FindControl<Grid>("UnitColumns")!.GetVisualDescendants().OfType<ComboBox>().Where(c => c.IsVisible).ToArray();
            Assert.All(unitFields, c => Assert.True(c.Bounds.Width < category.Bounds.Width, "unit fields are narrower than a material field"));
            Assert.All(settingsCombos, c => Assert.True(c.Bounds.Width <= category.Bounds.Width + 24, "the word fields stay close to the material field width"));
            Assert.All(settingsCombos, c => Assert.Equal(category.Bounds.Height, c.Bounds.Height, 1));
            var materialLabel = materialWindow.GetVisualDescendants().OfType<TextBlock>().First(t => t.Classes.Contains("propertyLabel"));
            var settingsLabel = settingsWindow.GetVisualDescendants().OfType<TextBlock>().First(t => t.Classes.Contains("propertyLabel"));
            Assert.Equal(materialLabel.FontSize, settingsLabel.FontSize); Assert.Equal(materialLabel.FontWeight, settingsLabel.FontWeight);
            Assert.Equal(((ISolidColorBrush)materialLabel.Foreground!).Color, ((ISolidColorBrush)settingsLabel.Foreground!).Color);
            Assert.Equal(materialWindow.Background is ISolidColorBrush m ? m.Color : default, settingsWindow.Background is ISolidColorBrush s ? s.Color : default);
        }
        finally { foreach (var window in owner.OwnedWindows.ToArray()) window.Close(); owner.Close(); }
    }

    /// <summary>The material editor answers Escape through its Cancel button, like every other dialog.</summary>
    [Fact]
    public void EscapeCancelsTheMaterialEditorAndTheSettingsWindow()
    {
        using var environment = new DesktopControlEnvironment();
        var (owner, _) = Owner();
        try
        {
            var main = new MainWindowViewModel(); main.NewCommand.Execute(null);
            var editor = new MaterialEditorViewModel(main.Setup, null, SetupEditorMode.ProjectDraft);
            bool cancelled = false; editor.CloseRequested += accepted => cancelled = !accepted;
            var window = new MaterialEditorWindow(editor); window.Show(owner); Arrange(window);
            Press(window, Key.Escape); Dispatcher.UIThread.RunJobs();
            Assert.True(cancelled); Assert.False(window.IsVisible);
            var settings = new SettingsWindow(new SettingsViewModel(UserSettings.Default, _ => Task.FromResult<string?>(null)));
            settings.Show(owner); Press(settings, Key.Escape); Dispatcher.UIThread.RunJobs();
            Assert.False(settings.IsVisible);
        }
        finally { foreach (var window in owner.OwnedWindows.ToArray()) window.Close(); owner.Close(); }
    }
}
