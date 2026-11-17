using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Controls;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;
using Xunit;

namespace SpanDraft.Tests;

[Collection("Avalonia")]
public sealed class DesktopSpringFlyoutControlTests
{
    private static (EditorViewModel Editor, SupportFlyout Flyout, Window Window) Show(EditorViewModel editor)
    {
        var flyout = new SupportFlyout { DataContext = editor };
        var window = new Window { Content = flyout, Width = 360, Height = 520 };
        window.Show(); window.Measure(new Size(360, 520)); window.Arrange(new Rect(0, 0, 360, 520));
        Dispatcher.UIThread.RunJobs();
        return (editor, flyout, window);
    }

    private static (EditorViewModel Editor, SupportFlyout Flyout, Window Window) Open(SupportType type, SpringKind? kind = null)
    {
        var editor = new EditorViewModel(DesktopLayoutFixture.Document(), () => { });
        if (kind is { } spring) editor.ToggleSpringTool(spring); else editor.ToggleSupportTool(type);
        editor.HoverPlacement(Length.FromMeters(.5));
        Assert.True(editor.PlaceSupport());
        return Show(editor);
    }

    /// <summary>A rotational spring placed where a translational spring already stands: two supports of their own at one station.</summary>
    private static (EditorViewModel Editor, SupportFlyout Flyout, Window Window) OpenRotationalNextToATranslationalSpring()
    {
        var document = DesktopLayoutFixture.Document().WithSupports([new(Guid.NewGuid(), Length.FromMeters(.5), SupportType.Spring, "S",
            new SpringData(TranslationalStiffness.FromNewtonsPerMeter(2e6), null))]);
        var editor = new EditorViewModel(document, () => { });
        editor.ToggleSpringTool(SpringKind.Rotational);
        editor.HoverPlacement(Length.FromMeters(.5));
        Assert.True(editor.PlaceSupport());
        return Show(editor);
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
    [InlineData("de-DE", SpringKind.Translational)]
    [InlineData("en-US", SpringKind.Translational)]
    [InlineData("de-DE", SpringKind.Rotational)]
    [InlineData("en-US", SpringKind.Rotational)]
    public void ASpringToolShowsExactlyItsOwnStiffnessField(string culture, SpringKind kind)
    {
        using var environment = new DesktopControlEnvironment(); using var scope = new UiCultureScope(culture);
        var (editor, flyout, window) = Open(SupportType.Spring, kind);
        bool translational = kind == SpringKind.Translational;
        Assert.True(flyout.FindControl<StackPanel>("SpringSection")!.IsVisible);
        Assert.Equal(translational, flyout.FindControl<TextBox>("TranslationalInput")!.IsEffectivelyVisible);
        Assert.Equal(!translational, flyout.FindControl<TextBox>("RotationalInput")!.IsEffectivelyVisible);
        Assert.Equal(!translational, flyout.FindControl<CheckBox>("RigidTranslationToggle")!.IsEffectivelyVisible);
        Assert.DoesNotContain(flyout.GetVisualDescendants().OfType<CheckBox>(), c => c.IsEffectivelyVisible && c.Name != "RigidTranslationToggle");
        Assert.Equal(kind, editor.SupportDraft!.Kind);
        window.Close();
    }

    [Theory]
    [InlineData("de-DE", SpringKind.Translational, "TranslationalInput", "TranslationalUnit", "TranslationalLabel")]
    [InlineData("de-DE", SpringKind.Rotational, "RotationalInput", "RotationalUnit", "RotationalLabel")]
    [InlineData("en-US", SpringKind.Translational, "TranslationalInput", "TranslationalUnit", "TranslationalLabel")]
    [InlineData("en-US", SpringKind.Rotational, "RotationalInput", "RotationalUnit", "RotationalLabel")]
    public void SpringRowsAlignWithThePositionRowAndStayInsideTheFlyoutWithoutClipping(string culture, SpringKind kind,
        string inputName, string unitName, string labelName)
    {
        using var environment = new DesktopControlEnvironment(); using var scope = new UiCultureScope(culture);
        var (_, flyout, window) = Open(SupportType.Spring, kind);
        window.Measure(new Size(360, 520)); window.Arrange(new Rect(0, 0, 360, 520));

        var section = flyout.FindControl<StackPanel>("SpringSection")!;
        Assert.True(section.IsVisible);
        var position = flyout.FindControl<TextBox>("PositionInput")!;
        var input = flyout.FindControl<TextBox>(inputName)!;
        Assert.Equal(position.Bounds.Width, input.Bounds.Width, 3);
        Assert.Equal(Origin(position, flyout).X, Origin(input, flyout).X, 3);
        Assert.Equal(Origin(flyout.FindControl<TextBlock>("PositionUnit")!, flyout).X, Origin(flyout.FindControl<Control>(unitName)!, flyout).X, 3);
        // The stiffness symbol shares the label column with the other rows and is not clipped.
        var label = flyout.FindControl<TextBlock>(labelName)!;
        Assert.InRange(Math.Abs(Origin(flyout.FindControl<TextBox>("NameInput")!, flyout).X - 88 - 8 - Origin(label, flyout).X), 0, 1.5);
        Assert.True(label.DesiredSize.Width <= 88 + 0.5, $"{labelName} is wider than its label column: {label.DesiredSize.Width}");
        // Rows keep their order and do not overlap each other or the footer.
        double[] tops = new[] { "TypeInput", inputName, "PositionInput" }
            .Select(n => Origin(flyout.FindControl<Control>(n)!, flyout).Y).ToArray();
        Assert.Equal(tops.Order(), tops);
        var footer = flyout.FindControl<Control>("Footer")!;
        Assert.True(Origin(footer, flyout).Y + footer.Bounds.Height <= flyout.Bounds.Height + 0.5);
        Assert.True(flyout.Bounds.Height <= 520);
        window.Close();
    }

    [Fact]
    public void RigidTranslationIsOnlyOfferedForARotationalSpring()
    {
        using var environment = new DesktopControlEnvironment(); using var scope = new UiCultureScope("en-US");
        var (editor, flyout, window) = Open(SupportType.Spring, SpringKind.Rotational);
        var rigid = flyout.FindControl<CheckBox>("RigidTranslationToggle")!;
        Assert.True(rigid.IsEffectivelyVisible);
        rigid.IsChecked = true; Dispatcher.UIThread.RunJobs();
        Assert.True(editor.SupportDraft!.RigidTranslation);
        // Next to a translational spring of the same station the translation cannot be rigid.
        var beside = OpenRotationalNextToATranslationalSpring();
        Assert.False(beside.Flyout.FindControl<CheckBox>("RigidTranslationToggle")!.IsEffectivelyVisible);
        beside.Window.Close();
        // Changing the kind in the flyout drops the rigid option together with the rotational spring.
        var types = flyout.FindControl<ComboBox>("TypeInput")!;
        types.SelectedItem = editor.SupportDraft.Types.First(o => o.Kind == SpringKind.Translational); Dispatcher.UIThread.RunJobs();
        Assert.False(editor.SupportDraft.RigidTranslation);
        Assert.False(rigid.IsEffectivelyVisible);
        window.Close();
    }

    [Fact]
    public void InitialStiffnessEnablesOkAndEditingStillValidatesTheField()
    {
        using var environment = new DesktopControlEnvironment(); using var scope = new UiCultureScope("en-US");
        var (editor, flyout, window) = Open(SupportType.Spring, SpringKind.Translational);
        var ok = flyout.FindControl<Button>("ConfirmAction")!;
        Assert.True(ok.IsEnabled);
        var input = flyout.FindControl<TextBox>("TranslationalInput")!;
        Assert.Equal("1000", input.Text);
        input.Text = "x"; Dispatcher.UIThread.RunJobs();
        Assert.False(ok.IsEnabled);
        Assert.Contains("error", input.Classes);
        Assert.Contains(flyout.GetVisualDescendants().OfType<Control>(), c => c is TextBlock t && t.Text == Strings.InvalidStiffness && t.IsVisible);
        input.Text = "1500"; Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain("error", input.Classes);
        Assert.True(ok.IsEnabled);
        Assert.Equal(1.5e6, editor.SupportDraft!.Spring!.Translational!.Value.NewtonsPerMeter);
        Assert.Null(editor.SupportDraft.Spring.Rotational);
        window.Close();
    }
}
