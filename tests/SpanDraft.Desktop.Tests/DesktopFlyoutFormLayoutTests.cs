using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Controls;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;
using SpanDraft.Desktop.Views;
using Xunit;

namespace SpanDraft.Tests;

/// <summary>
/// The form flyouts size their label, field and unit columns from their content, so that no translated label or option is
/// cut off and the rows of one flyout line up whatever the language.
/// </summary>
[Collection("Avalonia")]
public sealed class DesktopFlyoutFormLayoutTests
{
    private static Length M(double value) => Length.FromMeters(value);

    public static IEnumerable<object[]> Cases() =>
        from culture in new[] { "de-DE", "en-US" }
        from flyout in new[] { "pinned", "translational", "rotational", "force", "moment", "distributed", "release", "weight" }
        select new object[] { culture, flyout };

    private static (EditorViewModel Editor, Control Flyout) Create(string kind)
    {
        var editor = new EditorViewModel(kind == "weight" ? SelfWeightTestProjects.Document(true) : DesktopLayoutFixture.Document(), () => { });
        switch (kind)
        {
            case "pinned": editor.ToggleSupportTool(SupportType.Pinned); Hover(editor); Assert.True(editor.PlaceSupport()); return (editor, new SupportFlyout { DataContext = editor });
            case "translational": editor.ToggleSpringTool(SpringKind.Translational); Hover(editor); Assert.True(editor.PlaceSupport()); return (editor, new SupportFlyout { DataContext = editor });
            case "rotational": editor.ToggleSpringTool(SpringKind.Rotational); Hover(editor); Assert.True(editor.PlaceSupport()); return (editor, new SupportFlyout { DataContext = editor });
            case "force": editor.ToggleLoadTool(PointLoadKind.Force); editor.HoverLoadPlacement(M(.5)); Assert.True(editor.PlaceLoad()); return (editor, new PointLoadFlyout { DataContext = editor });
            case "moment": editor.ToggleLoadTool(PointLoadKind.Moment); editor.HoverLoadPlacement(M(.5)); Assert.True(editor.PlaceLoad()); return (editor, new PointLoadFlyout { DataContext = editor });
            case "distributed":
                editor.ToggleDistributedLoadTool();
                editor.HoverDistributedLoadPlacement(M(.2)); Assert.True(editor.PlaceDistributedLoadEndpoint());
                editor.HoverDistributedLoadPlacement(M(.8)); Assert.True(editor.PlaceDistributedLoadEndpoint());
                return (editor, new DistributedLoadFlyout { DataContext = editor });
            case "release": editor.ToggleMomentReleaseTool(); editor.HoverMomentReleasePlacement(M(.5)); Assert.True(editor.PlaceMomentRelease()); return (editor, new MomentReleaseFlyout { DataContext = editor });
            case "weight": Assert.True(editor.EditSelfWeight(editor.Document.SelfWeight!.Id)); return (editor, new SelfWeightFlyout { DataContext = editor });
            default: throw new ArgumentOutOfRangeException(nameof(kind));
        }
        static void Hover(EditorViewModel editor) => editor.HoverPlacement(M(.5));
    }

    /// <summary>The flyout at the size its content asks for, as a popup gives it.</summary>
    private static Window Show(Control flyout)
    {
        flyout.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left; flyout.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
        var window = new Window { Content = flyout, Width = 700, Height = 700 };
        window.Show(); Layout(window);
        return window;
    }

    private static void Layout(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.Measure(new Size(700, 700)); window.Arrange(new Rect(0, 0, 700, 700)); window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    private static Rect Bounds(Control control, Visual root) => new(control.TranslatePoint(default, root)!.Value, control.Bounds.Size);

    /// <summary>Nothing is cut off or leaves the flyout, and the label, field and unit columns of all rows are the same.</summary>
    private static void AssertFormFits(Control flyout, string context)
    {
        var border = flyout.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("supportFlyout"));
        var inner = Bounds(border, flyout).Deflate(border.BorderThickness).Deflate(border.Padding);
        foreach (var control in border.GetVisualDescendants().OfType<Control>().Where(c => c.IsEffectivelyVisible && c.Bounds.Width > 0 && c.FindAncestorOfType<Button>() is null && c.FindAncestorOfType<ComboBox>() is null))
            Assert.True(Bounds(control, flyout).Right <= inner.Right + 0.5, $"{context}: {control.GetType().Name} {control.Name} leaves the flyout");
        foreach (var text in border.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible && t.TextWrapping == TextWrapping.NoWrap))
            Assert.True(text.TextLayout.WidthIncludingTrailingWhitespace <= text.Bounds.Width + 0.5,
                $"{context}: '{text.Text}' needs {text.TextLayout.WidthIncludingTrailingWhitespace:0.#} of {text.Bounds.Width:0.#}");
        var rows = border.GetVisualDescendants().OfType<FlyoutFormRow>().Where(r => r.IsEffectivelyVisible).ToArray();
        Assert.NotEmpty(rows);
        foreach (var row in rows.Skip(1))
            for (int column = 0; column < 3; column++)
                Assert.Equal(rows[0].ColumnDefinitions[column].ActualWidth, row.ColumnDefinitions[column].ActualWidth, 3);
        foreach (var column in new[] { 0, 1 })
        {
            var starts = rows.SelectMany(r => r.Children.OfType<Control>().Where(c => c.IsEffectivelyVisible && Grid.GetColumn(c) == column))
                .Select(c => Bounds(c, flyout).X).ToArray();
            Assert.All(starts, x => Assert.Equal(starts[0], x, 3));
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void EveryFlyoutKeepsAllTextInsideItsCellAndAlignsItsRows(string culture, string kind)
    {
        using var environment = new DesktopControlEnvironment(); using var scope = new UiCultureScope(culture);
        var (editor, flyout) = Create(kind);
        var window = Show(flyout);
        AssertFormFits(flyout, $"{culture} {kind}");
        // The support type decides the rows and shows the longest option; every option has to fit when it is shown.
        if (editor.SupportDraft is { } draft)
            foreach (var option in draft.Types.ToArray())
            {
                draft.SelectedType = option; Layout(window);
                AssertFormFits(flyout, $"{culture} {kind} -> {option.Name}");
            }
        window.Close();
    }

    /// <summary>A unit reads as part of its label, as in the section and material editors: same color, not darker than the label.</summary>
    [Theory]
    [InlineData("pinned")] [InlineData("translational")] [InlineData("rotational")] [InlineData("force")]
    [InlineData("moment")] [InlineData("distributed")] [InlineData("release")]
    public void UnitsUseTheColorOfTheirLabels(string kind)
    {
        using var environment = new DesktopControlEnvironment();
        var (_, flyout) = Create(kind);
        var window = Show(flyout);
        var units = 0;
        foreach (var row in flyout.GetVisualDescendants().OfType<FlyoutFormRow>().Where(r => r.IsEffectivelyVisible))
        {
            var label = row.Children.OfType<TextBlock>().FirstOrDefault(t => Grid.GetColumn(t) == 0);
            var unit = row.Children.OfType<TextBlock>().FirstOrDefault(t => Grid.GetColumn(t) == 2 && t.IsEffectivelyVisible);
            if (label is null || unit is null) continue;
            Assert.Equal(((ISolidColorBrush)label.Foreground!).Color, ((ISolidColorBrush)unit.Foreground!).Color);
            units++;
        }
        Assert.True(units > 0, $"{kind}: no unit found");
        window.Close();
    }

    [Theory]
    [InlineData("de-DE")] [InlineData("en-US")]
    public void TheSelfWeightFlyoutUsesTheRowGridAndLabelStyleOfTheOtherFlyouts(string culture)
    {
        using var environment = new DesktopControlEnvironment(); using var scope = new UiCultureScope(culture);
        var (_, weight) = Create("weight"); var (_, force) = Create("force");
        var weightWindow = Show(weight); var forceWindow = Show(force);
        var weightRows = weight.GetVisualDescendants().OfType<FlyoutFormRow>().ToArray();
        var forceRows = force.GetVisualDescendants().OfType<FlyoutFormRow>().Where(r => r.IsEffectivelyVisible).ToArray();
        Assert.Equal(3, weightRows.Length);
        // Same row height and label column as the form flyouts, and the same label look.
        Assert.All(weightRows, r => Assert.Equal(forceRows[0].Bounds.Height, r.Bounds.Height, 3));
        Assert.All(weightRows, r => Assert.Equal(32, r.Bounds.Height, 3));
        var forceLabel = forceRows[0].Children.OfType<TextBlock>().First(); var weightLabel = weightRows[0].Children.OfType<TextBlock>().First();
        Assert.Equal(Bounds(forceLabel, force).X, Bounds(weightLabel, weight).X, 3);
        foreach (var row in weightRows)
        {
            var label = row.Children.OfType<TextBlock>().First(t => Grid.GetColumn(t) == 0);
            Assert.Equal(forceLabel.FontSize, label.FontSize); Assert.Equal(forceLabel.FontWeight, label.FontWeight);
            Assert.Equal(((ISolidColorBrush)forceLabel.Foreground!).Color, ((ISolidColorBrush)label.Foreground!).Color);
            var value = row.Children.OfType<TextBlock>().First(t => Grid.GetColumn(t) == 1);
            Assert.Equal(Bounds(weightRows[0].Children.OfType<TextBlock>().First(t => Grid.GetColumn(t) == 1), weight).X, Bounds(value, weight).X, 3);
        }
        // The read-only values keep the label distance of the form flyouts: they start where the text inside their fields starts.
        var field = forceRows[0].Children.OfType<TextBox>().First();
        var fieldTextGap = Bounds(field, force).X + field.BorderThickness.Left + field.Padding.Left
            - Bounds(forceRows[0], force).X - forceRows[0].ColumnDefinitions[0].ActualWidth;
        var firstValue = weightRows[0].Children.OfType<TextBlock>().First(t => Grid.GetColumn(t) == 1);
        var valueGap = Bounds(firstValue, weight).X - Bounds(weightRows[0], weight).X - weightRows[0].ColumnDefinitions[0].ActualWidth;
        Assert.Equal(fieldTextGap, valueGap, 1);
        AssertFormFits(weight, culture);
        weightWindow.Close(); forceWindow.Close();
    }

    [Theory]
    [InlineData("de-DE")] [InlineData("en-US")]
    public void ANewMaterialShowsAPlaceholderForTheMissingStrengthTypeAndEveryChoiceFitsItsField(string culture)
    {
        using var environment = new DesktopControlEnvironment(); using var scope = new UiCultureScope(culture);
        var main = new MainWindowViewModel(); main.NewCommand.Execute(null);
        var editor = new MaterialEditorViewModel(main.Setup, null, SetupEditorMode.ProjectDraft);
        var view = new MaterialEditorView { DataContext = editor };
        var window = new Window { Content = view, Width = 408, Height = 650 };
        window.Show(); Dispatcher.UIThread.RunJobs();
        var choice = view.FindControl<ComboBox>("MaterialStrengthTypeInput")!;
        Assert.Null(choice.SelectedItem);
        Assert.Equal(Strings.MaterialStrengthTypePlaceholder, choice.PlaceholderText);
        var placeholder = choice.GetVisualDescendants().OfType<TextBlock>().Single(t => t.IsEffectivelyVisible && t.Text == choice.PlaceholderText);
        Assert.True(placeholder.TextLayout.WidthIncludingTrailingWhitespace <= placeholder.Bounds.Width);
        // Muted like the placeholder of a TextBox, so that it does not read as an entered value.
        var normal = ((ISolidColorBrush)Application.Current!.FindResource("TextPrimary")!).Color;
        Assert.NotEqual(normal, ((ISolidColorBrush)placeholder.Foreground!).Color);
        foreach (var type in editor.StrengthTypes)
        {
            choice.SelectedItem = type; Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var shown = choice.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible && t.TextLayout.WidthIncludingTrailingWhitespace > 0).ToArray();
            Assert.NotEmpty(shown);
            Assert.All(shown, t => Assert.True(t.TextLayout.WidthIncludingTrailingWhitespace <= t.Bounds.Width + 0.5,
                $"{type.Value} needs {t.TextLayout.WidthIncludingTrailingWhitespace:0.#} of {t.Bounds.Width:0.#}"));
        }
        window.Close();
    }
}
