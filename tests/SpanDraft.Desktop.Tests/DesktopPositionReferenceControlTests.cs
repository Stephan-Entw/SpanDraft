using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using SpanDraft.Core.Materials;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Sections.Parametric;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Controls;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;
using Xunit;

namespace SpanDraft.Tests;

/// <summary>The reference row of the flyouts: the choice appears with several segments, the end binding at a segment end.</summary>
[Collection("Avalonia")]
public sealed class DesktopPositionReferenceControlTests
{
    private static Length M(double value) => Length.FromMeters(value);
    private static readonly Material Steel = new("Steel", Pressure.FromPascals(210e9), Pressure.FromPascals(235e6),
        MaterialStrengthType.Re);
    private static EditorSegment Segment() => new(Steel, new RectangleSectionGeometry(M(0.1), M(0.2)), SectionAxisDesignation.Y)
    { Length = M(1) };

    private static (EditorViewModel Editor, SupportFlyout Flyout, Window Window) Open(int segments, double x)
    {
        var document = new EditorDocument(Enumerable.Range(0, segments).Select(_ => Segment()).ToArray());
        var editor = new EditorViewModel(document, () => { });
        editor.ToggleSupportTool(SupportType.Pinned);
        editor.HoverPlacement(M(x));
        Assert.True(editor.PlaceSupport());
        var flyout = new SupportFlyout { DataContext = editor };
        var window = new Window { Content = flyout, Width = 360, Height = 520 };
        window.Show();
        Arrange(window);
        return (editor, flyout, window);
    }

    private static void Arrange(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.Measure(new Size(360, 520));
        window.Arrange(new Rect(0, 0, 360, 520));
    }

    private static Point Origin(Control control, Control flyout) => control.TranslatePoint(new Point(), flyout)!.Value;

    [Fact]
    public void WithSeveralSegmentsTheChoiceAlignsWithThePositionRowAndStaysInsideTheFlyout()
    {
        using var environment = new DesktopControlEnvironment();
        var (_, flyout, window) = Open(3, 1.5);

        var row = flyout.FindControl<PositionReferenceRow>("PositionReference")!;
        Assert.True(row.IsEffectivelyVisible);
        var choice = row.FindControl<Grid>("ReferenceChoice")!;
        Assert.True(choice.IsEffectivelyVisible);
        var combo = row.FindControl<ComboBox>("ReferenceInput")!;
        var position = flyout.FindControl<TextBox>("PositionInput")!;
        Assert.Equal(position.Bounds.Width, combo.Bounds.Width, 3);
        Assert.Equal(Origin(position, flyout).X, Origin(combo, flyout).X, 3);
        Assert.True(Origin(combo, flyout).Y > Origin(position, flyout).Y);
        Assert.True(Origin(combo, flyout).Y + combo.Bounds.Height <= flyout.Bounds.Height + 0.5);
        Assert.False(row.FindControl<CheckBox>("HoldEndInput")!.IsEffectivelyVisible);
        // The point is measured from segment 2: the field shows 500 and the global position stands next to it.
        Assert.Equal("500", position.Text);
        Assert.True(row.FindControl<Grid>("PositionInfoRow")!.IsEffectivelyVisible);
        Assert.Contains("1500", row.FindControl<TextBlock>("PositionInfo")!.Text);

        // Choosing the beam start re-expresses the same point: the field follows and the info is not needed any more.
        var model = Assert.IsType<PositionReferenceDraft>(row.DataContext);
        model.SelectedOption = model.Options[0];
        Arrange(window);
        Assert.Equal("1500", position.Text);
        Assert.False(row.FindControl<Grid>("PositionInfoRow")!.IsEffectivelyVisible);
        window.Close();
    }

    [Fact]
    public void WithOneSegmentThereIsNoChoiceAndTheBindingAppearsOnlyAtTheEnd()
    {
        using var environment = new DesktopControlEnvironment();
        var (editor, flyout, window) = Open(1, 0.5);
        var row = flyout.FindControl<PositionReferenceRow>("PositionReference")!;

        Assert.False(row.FindControl<Grid>("ReferenceChoice")!.IsEffectivelyVisible);
        Assert.False(row.FindControl<CheckBox>("HoldEndInput")!.IsEffectivelyVisible);

        editor.SupportDraft!.PositionText = "1000";
        Arrange(window);

        var hold = row.FindControl<CheckBox>("HoldEndInput")!;
        Assert.True(hold.IsEffectivelyVisible);
        Assert.False(hold.IsChecked);
        Assert.Equal(row.FindControl<CheckBox>("HoldEndInput")!.Content, editor.SupportDraft.Reference.HoldEndLabel);
        window.Close();
    }

    [Fact]
    public void TheBindingIsLabelledWithItsSegmentAndCheckingItBindsThePosition()
    {
        using var environment = new DesktopControlEnvironment();
        var (editor, flyout, window) = Open(3, 1);
        var row = flyout.FindControl<PositionReferenceRow>("PositionReference")!;
        var hold = row.FindControl<CheckBox>("HoldEndInput")!;

        Assert.True(editor.SupportDraft!.Reference.CanHoldEnd);
        Assert.True(hold.IsEffectivelyVisible);
        Assert.Contains("1", (string)hold.Content!);
        // The binding writes the box back to the draft, and the combo box next to it must not undo that.
        hold.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Arrange(window);

        Assert.Same(editor.SupportDraft!.Reference, hold.DataContext);
        Assert.True(editor.SupportDraft.Reference.HoldEnd);
        Assert.IsType<PositionRef.AtSegmentEnd>(editor.SupportDraft.Reference.Current);
        window.Close();
    }
}
