using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Controls;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.ViewModels;
using Xunit;

namespace SpanDraft.Tests;

[Collection("Avalonia")]
public sealed class DesktopSpringFlyoutControlTests
{
    private static (EditorViewModel Editor, SupportFlyout Flyout, Window Window) Open(SupportType type)
    {
        var editor = new EditorViewModel(DesktopLayoutFixture.Document(), () => { });
        editor.ToggleSupportTool(type); editor.HoverPlacement(Length.FromMeters(.5));
        Assert.True(editor.PlaceSupport());
        var flyout = new SupportFlyout { DataContext = editor };
        var window = new Window { Content = flyout, Width = 360, Height = 520 };
        window.Show(); window.Measure(new Size(360, 520)); window.Arrange(new Rect(0, 0, 360, 520));
        Dispatcher.UIThread.RunJobs();
        return (editor, flyout, window);
    }

    private static Point Origin(Control control, Control flyout) => control.TranslatePoint(new Point(), flyout)!.Value;

    [Theory]
    [InlineData("de-DE", SupportType.Pinned)]
    [InlineData("en-US", SupportType.Roller)]
    public void RigidSupportsDoNotShowTheSpringRows(string culture, SupportType type)
    {
        using var environment = new DesktopControlEnvironment(); using var scope = new UiCultureScope(culture);
        var (_, flyout, window) = Open(type);
        Assert.False(flyout.FindControl<StackPanel>("SpringSection")!.IsVisible);
        window.Close();
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("en-US")]
    public void SpringRowsAlignWithThePositionRowAndStayInsideTheFlyoutWithoutClipping(string culture)
    {
        using var environment = new DesktopControlEnvironment(); using var scope = new UiCultureScope(culture);
        var (editor, flyout, window) = Open(SupportType.Spring);
        editor.SupportDraft!.HasRotational = true;
        Dispatcher.UIThread.RunJobs();
        window.Measure(new Size(360, 520)); window.Arrange(new Rect(0, 0, 360, 520));

        var section = flyout.FindControl<StackPanel>("SpringSection")!;
        Assert.True(section.IsVisible);
        var position = flyout.FindControl<TextBox>("PositionInput")!;
        foreach (string name in new[] { "TranslationalInput", "RotationalInput" })
        {
            var input = flyout.FindControl<TextBox>(name)!;
            Assert.Equal(position.Bounds.Width, input.Bounds.Width, 3);
            Assert.Equal(Origin(position, flyout).X, Origin(input, flyout).X, 3);
        }
        foreach (string name in new[] { "TranslationalUnit", "RotationalUnit" })
            Assert.Equal(Origin(flyout.FindControl<TextBlock>("PositionUnit")!, flyout).X, Origin(flyout.FindControl<Control>(name)!, flyout).X, 3);
        // The check boxes share the label column with the other rows and their captions are not clipped.
        foreach (string name in new[] { "TranslationalToggle", "RotationalToggle" })
        {
            var toggle = flyout.FindControl<CheckBox>(name)!;
            Assert.InRange(Math.Abs(Origin(flyout.FindControl<TextBox>("NameInput")!, flyout).X - 88 - 8 - Origin(toggle, flyout).X), 0, 1.5);
            Assert.True(toggle.Bounds.Width <= 88 + 1, $"{name} is wider than its label column: {toggle.Bounds.Width}");
            var caption = Assert.Single(toggle.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == (string)toggle.Content!);
            Assert.True(caption.TextLayout.TextLines[0].Width <= caption.Bounds.Width + 0.5, $"{name} caption is clipped");
        }
        // Rows keep their order and do not overlap each other or the footer.
        double[] tops = new[] { "TypeInput", "TranslationalInput", "RotationalInput", "PositionInput" }
            .Select(n => Origin(flyout.FindControl<Control>(n)!, flyout).Y).ToArray();
        Assert.Equal(tops.Order(), tops);
        Assert.True(Origin(flyout.FindControl<Control>("RigidTranslationToggle")!, flyout).Y
            >= Origin(flyout.FindControl<Control>("RotationalInput")!, flyout).Y + flyout.FindControl<Control>("RotationalInput")!.Bounds.Height);
        var footer = flyout.FindControl<Control>("Footer")!;
        Assert.True(Origin(footer, flyout).Y + footer.Bounds.Height <= flyout.Bounds.Height + 0.5);
        Assert.True(flyout.Bounds.Height <= 520);
        window.Close();
    }

    [Fact]
    public void RigidTranslationIsOnlyEnabledForARotationalSpringWithoutATranslationalOne()
    {
        using var environment = new DesktopControlEnvironment(); using var scope = new UiCultureScope("en-US");
        var (editor, flyout, window) = Open(SupportType.Spring);
        var rigid = flyout.FindControl<CheckBox>("RigidTranslationToggle")!;
        var translational = flyout.FindControl<CheckBox>("TranslationalToggle")!;
        var rotational = flyout.FindControl<CheckBox>("RotationalToggle")!;
        Assert.True(translational.IsChecked);
        Assert.False(rigid.IsEnabled);
        Assert.False(flyout.FindControl<TextBox>("RotationalInput")!.IsEnabled);
        rotational.IsChecked = true; Dispatcher.UIThread.RunJobs();
        Assert.True(flyout.FindControl<TextBox>("RotationalInput")!.IsEnabled);
        Assert.False(rigid.IsEnabled);
        translational.IsChecked = false; Dispatcher.UIThread.RunJobs();
        Assert.False(flyout.FindControl<TextBox>("TranslationalInput")!.IsEnabled);
        Assert.True(rigid.IsEnabled);
        rigid.IsChecked = true; Dispatcher.UIThread.RunJobs();
        Assert.True(editor.SupportDraft!.RigidTranslation);
        translational.IsChecked = true; Dispatcher.UIThread.RunJobs();
        Assert.False(rigid.IsChecked);
        window.Close();
    }

    [Fact]
    public void TypingBothStiffnessesEnablesOkAndAnInvalidFieldShowsItsMessage()
    {
        using var environment = new DesktopControlEnvironment(); using var scope = new UiCultureScope("en-US");
        var (editor, flyout, window) = Open(SupportType.Spring);
        var ok = flyout.FindControl<Button>("ConfirmAction")!;
        Assert.False(ok.IsEnabled);
        var input = flyout.FindControl<TextBox>("TranslationalInput")!;
        input.Text = "x"; Dispatcher.UIThread.RunJobs();
        Assert.Contains("error", input.Classes);
        Assert.Contains(flyout.GetVisualDescendants().OfType<Control>(), c => c is TextBlock t && t.Text == Strings.InvalidStiffness && t.IsVisible);
        input.Text = "1500"; Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain("error", input.Classes);
        Assert.True(ok.IsEnabled);
        Assert.Equal(1.5e6, editor.SupportDraft!.Spring!.Translational!.Value.NewtonsPerMeter);
        window.Close();
    }
}
