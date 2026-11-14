using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using SpanDraft.Core.Materials;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Sections.Parametric;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;
using SpanDraft.Desktop.Views;
using Xunit;

namespace SpanDraft.Tests;

[Collection("Avalonia")]
public sealed class DesktopSegmentSetupControlTests
{
    private static Length M(double value) => Length.FromMeters(value);
    private static Material Steel(string name) => new(name, Pressure.FromPascals(210e9), Pressure.FromPascals(235e6),
        MaterialStrengthType.Re);

    private static ProjectSetupViewModel Setup(int segments)
    {
        var document = segments == 1
            ? new EditorDocument(M(3), Steel("Steel"), new RectangleSectionGeometry(M(.1), M(.2)), SectionAxisDesignation.Y,
                [new(Guid.NewGuid(), M(0), SupportType.Fixed, "A")])
            : new EditorDocument(M(3), [M(1), M(2)],
                [new(Steel("Steel with a rather long descriptive name"), new RectangleSectionGeometry(M(.1), M(.2)), SectionAxisDesignation.Y),
                    new(Steel("Aluminium"), new CircleSectionGeometry(M(.06)), SectionAxisDesignation.Y),
                    new(Steel("Steel"), new RectangleSectionGeometry(M(.05), M(.1)), SectionAxisDesignation.Z)],
                [new(Guid.NewGuid(), M(0), SupportType.Fixed, "A")]);
        var first = document.Segments[0];
        return new(ProjectSetupMode.Edit, first.Section, first.BendingAxis, first.Material, _ => Task.FromResult(true),
            () => { }, originalDocument: document);
    }

    private static void Arrange(Window window, double width, double height)
    {
        window.Measure(new Size(width, height));
        window.Arrange(new Rect(0, 0, width, height));
        window.UpdateLayout();
    }

    [Theory]
    [InlineData(1100, 650)]
    [InlineData(1250, 800)]
    public void SegmentRowsAlignWithTheirHeaderStayInsideTheCardAndOfferTheirActions(double width, double height)
    {
        using var environment = new DesktopControlEnvironment();
        var setup = Setup(3);
        var view = new ProjectSetupView { DataContext = setup };
        var window = new Window { Content = view, Width = width, Height = height };
        window.Show();
        Arrange(window, width, height);

        var card = view.FindControl<SegmentSetupCard>("SegmentCard")!;
        Assert.True(card.IsEffectivelyVisible);
        var surface = card.FindControl<Border>("SegmentCardSurface")!;
        var header = card.FindControl<Grid>("SegmentHeader")!;
        var list = card.FindControl<ListBox>("SegmentList")!;
        Assert.Equal(3, list.ItemCount);

        // Containment: nothing leaves the card horizontally and the card stays inside the page.
        Rect Bounds(Control control) => control.TranslatePoint(default, window) is { } origin
            ? new Rect(origin, control.Bounds.Size) : default;
        Assert.True(Bounds(surface).Right <= window.Bounds.Width + 0.5);
        foreach (var control in card.GetVisualDescendants().OfType<Control>().Where(c => c.IsEffectivelyVisible && c is TextBlock or TextBox or Button))
            Assert.True(Bounds(control).Right <= Bounds(surface).Right + 0.5, control.GetType().Name);

        // Alignment: every row cell starts where its header cell starts.
        var headerCells = header.Children.OfType<TextBlock>().OrderBy(c => Grid.GetColumn(c)).ToArray();
        foreach (var row in list.GetVisualDescendants().OfType<Grid>().Where(g => g.DataContext is SetupSegmentRow))
        {
            var cells = row.Children.OfType<Control>().OrderBy(c => Grid.GetColumn(c)).ToArray();
            Assert.Equal(headerCells.Length, cells.Length);
            for (int i = 0; i < cells.Length; i++)
                Assert.Equal(Bounds(headerCells[i]).X, Bounds(cells[i]).X, 1.5);
        }

        // Equal sizes: the split and remove buttons have the standard height of the other setup controls.
        var split = card.FindControl<Button>("SplitButton")!;
        var remove = card.FindControl<Button>("RemoveSegmentButton")!;
        Assert.Equal(split.Bounds.Height, remove.Bounds.Height);
        Assert.True(split.IsEffectivelyEnabled && remove.IsEffectivelyEnabled);
        Assert.False(card.FindControl<TextBox>("SegmentStartInput")!.IsEffectivelyVisible);
        window.Close();
    }

    [Fact]
    public void SelectingARowShowsItsStartInputAndTheBeamCardFollowsTheSelectedSegment()
    {
        using var environment = new DesktopControlEnvironment();
        var setup = Setup(3);
        var view = new ProjectSetupView { DataContext = setup };
        var window = new Window { Content = view, Width = 1100, Height = 650 };
        window.Show();
        Arrange(window, 1100, 650);
        var card = view.FindControl<SegmentSetupCard>("SegmentCard")!;
        var list = card.FindControl<ListBox>("SegmentList")!;
        var beamCard = view.FindControl<BeamSetupCard>("BeamCard")!;
        string first = beamCard.FindControl<TextBlock>("MaterialDescription")!.Text!;

        list.SelectedIndex = 1;
        Arrange(window, 1100, 650);

        Assert.Equal(1, setup.SelectedSegmentIndex);
        Assert.True(card.FindControl<TextBox>("SegmentStartInput")!.IsEffectivelyVisible);
        Assert.NotEqual(first, beamCard.FindControl<TextBlock>("MaterialDescription")!.Text);
        Assert.Contains("Aluminium", beamCard.FindControl<TextBlock>("MaterialDescription")!.Text);
        window.Close();
    }

    [Fact]
    public void ASingleSegmentSetupStillOffersSplittingButNoRemoval()
    {
        using var environment = new DesktopControlEnvironment();
        var setup = Setup(1);
        var view = new ProjectSetupView { DataContext = setup };
        var window = new Window { Content = view, Width = 1100, Height = 650 };
        window.Show();
        Arrange(window, 1100, 650);
        var card = view.FindControl<SegmentSetupCard>("SegmentCard")!;

        Assert.True(card.FindControl<Button>("SplitButton")!.IsEffectivelyEnabled);
        Assert.False(card.FindControl<Button>("RemoveSegmentButton")!.IsEffectivelyEnabled);
        Assert.Equal(1, card.FindControl<ListBox>("SegmentList")!.ItemCount);

        setup.SplitInput.Begin();
        setup.SplitInput.Text = "1200";
        card.FindControl<Button>("SplitButton")!.Command!.Execute(null);
        Arrange(window, 1100, 650);
        Assert.Equal(2, card.FindControl<ListBox>("SegmentList")!.ItemCount);
        Assert.True(card.FindControl<Button>("RemoveSegmentButton")!.IsEffectivelyEnabled);
        window.Close();
    }

    [Fact]
    public void ACreateSetupHasNoSegmentList()
    {
        using var environment = new DesktopControlEnvironment();
        var main = new MainWindowViewModel();
        main.NewCommand.Execute(null);
        var view = new ProjectSetupView { DataContext = main.Setup };
        var window = new Window { Content = view, Width = 1100, Height = 650 };
        window.Show();
        Arrange(window, 1100, 650);
        Assert.False(view.FindControl<SegmentSetupCard>("SegmentCard")!.IsEffectivelyVisible);
        window.Close();
    }
}
