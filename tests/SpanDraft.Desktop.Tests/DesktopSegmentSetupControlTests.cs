using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using SpanDraft.Core.Materials;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Sections.Parametric;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.Resources;
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
    public void SegmentCardsShareTheirThreeAreasStayInsideTheViewAndOfferTheirActions(double width, double height)
    {
        using var environment = new DesktopControlEnvironment();
        var setup = Setup(3);
        var view = new ProjectSetupView { DataContext = setup };
        var window = new Window { Content = view, Width = width, Height = height };
        window.Show();
        Arrange(window, width, height);

        var cards = view.Cards();
        Assert.Equal(3, cards.Count);
        Rect Bounds(Control control) => control.TranslatePoint(default, window) is { } origin
            ? new Rect(origin, control.Bounds.Size) : default;

        // Containment: nothing leaves its card horizontally and every card stays inside the window.
        foreach (var card in cards)
        {
            Assert.True(Bounds(card).Right <= window.Bounds.Width + 0.5);
            foreach (var control in card.GetVisualDescendants().OfType<Control>().Where(c => c.IsEffectivelyVisible && c is TextBlock or TextBox or Button))
                Assert.True(Bounds(control).Right <= Bounds(card).Right + 0.5, control.GetType().Name);
        }

        // Alignment and equal sizes: the areas of every card start and end at the same x; the areas carry no frame of their own.
        foreach (string name in new[] { "SectionCardButton", "MaterialCardButton", "SegmentMenuButton" })
        {
            var parts = cards.Select(card => Bounds(card.Part<Button>(name))).ToArray();
            Assert.All(parts, bounds => Assert.Equal(parts[0].X, bounds.X, 1.5));
            Assert.All(parts, bounds => Assert.Equal(parts[0].Width, bounds.Width, 1.5));
        }
        Assert.All(cards, card => Assert.Equal(default, card.Part<Button>("SectionCardButton").BorderThickness));
        Assert.All(cards, card => Assert.Equal(default, card.Part<Button>("MaterialCardButton").BorderThickness));
        // The section area is wider than the material area.
        Assert.All(cards, card => Assert.True(card.Part<Button>("SectionCardButton").Bounds.Width > card.Part<Button>("MaterialCardButton").Bounds.Width));
        Assert.All(cards, card => Assert.True(card.Part<TextBlock>("SegmentTitle").IsEffectivelyVisible));
        Assert.Equal([1, 2, 3], cards.Select(card => ((SetupSegmentCard)card.DataContext!).Number));

        // The cards are stacked in order; the add button stands right-aligned on the line of the heading above the first one.
        Assert.True(Bounds(cards[1]).Y >= Bounds(cards[0]).Bottom - 0.5 && Bounds(cards[2]).Y >= Bounds(cards[1]).Bottom - 0.5);
        var add = view.Part<Button>("AddSegmentButton");
        Assert.True(add.IsEffectivelyVisible && add.IsEffectivelyEnabled);
        Assert.True(Bounds(add).Bottom <= Bounds(cards[0]).Y + 0.5);
        Assert.Equal(Bounds(cards[0]).Right, Bounds(add).Right, 1.5);
        var heading = view.Part<TextBlock>("SegmentsTitle");
        Assert.Equal(Strings.SetupSegments, heading.Text);
        Assert.True(Bounds(heading).Right <= Bounds(add).X);
        Assert.InRange(Math.Abs(Bounds(heading).Center.Y - Bounds(add).Center.Y), 0, 1.5);
        Assert.Equal(setup.Cards[0].Id, ((SetupSegmentCard)cards[0].DataContext!).Id);
        if (Environment.GetEnvironmentVariable("SPANDRAFT_VISUAL_TEST_OUTPUT") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize((int)width, (int)height), new Vector(96, 96));
            bitmap.Render(window);
            bitmap.Save(Path.Combine(directory, $"segments-{width}x{height}.png"));
        }
        window.Close();
    }

    [Fact]
    public void AddingASegmentShowsAnEmptyCardAndTheTitlesAppearWithOneSegmentAlready()
    {
        using var environment = new DesktopControlEnvironment();
        var setup = Setup(1);
        var view = new ProjectSetupView { DataContext = setup };
        var window = new Window { Content = view, Width = 1100, Height = 650 };
        window.Show();
        Arrange(window, 1100, 650);

        var only = view.FirstCard();
        Assert.True(only.Part<TextBlock>("SegmentTitle").IsEffectivelyVisible);
        Assert.True(only.Part<Button>("SegmentMenuButton").IsEffectivelyVisible);

        view.Part<Button>("AddSegmentButton").Command!.Execute(null);
        Arrange(window, 1100, 650);

        var cards = view.Cards();
        Assert.Equal(2, cards.Count);
        Assert.All(cards, card => Assert.True(card.Part<TextBlock>("SegmentTitle").IsEffectivelyVisible));
        Assert.Empty(cards[1].Part<TextBox>("SetupLengthInput").Text!);
        window.Close();
    }

    [Fact]
    public void ACreateSetupAlsoOffersTheCardsAndTheAddButton()
    {
        using var environment = new DesktopControlEnvironment();
        var main = new MainWindowViewModel();
        main.NewCommand.Execute(null);
        var view = new ProjectSetupView { DataContext = main.Setup };
        var window = new Window { Content = view, Width = 1100, Height = 650 };
        window.Show();
        Arrange(window, 1100, 650);
        Assert.Single(view.Cards());
        Assert.True(view.Part<Button>("AddSegmentButton").IsEffectivelyVisible);
        window.Close();
    }

    [Theory]
    [InlineData(3)]
    [InlineData(1)]
    public void TheOverviewListsSegmentsAndNamesTheGoverningOneOnlyForSeveral(int segments)
    {
        using var environment = new DesktopControlEnvironment();
        var setup = Setup(segments);
        var document = new EditorDocument(setup.BuildSegments(),
            [new(Guid.NewGuid(), M(0), SupportType.Fixed, "A")], [new EditorPointForce(Guid.NewGuid(), M(3), Force.FromNewtons(-1000), "F1")]);
        var editor = new EditorViewModel(document, () => { });
        var view = new EditorView { DataContext = editor };
        var window = new Window { Content = view, Width = 1250, Height = 900 };
        window.Show();
        Arrange(window, 1250, 900);
        var overview = view.GetVisualDescendants().OfType<ProjectOverviewView>().Single();
        var table = overview.FindControl<ItemsControl>("SegmentTable")!;
        var block = overview.FindControl<Grid>("ProjectBlock")!;
        var row = overview.FindControl<Grid>("GoverningSegmentRow")!;

        Assert.Equal(segments > 1, table.IsEffectivelyVisible);
        Assert.Equal(segments > 1, row.IsEffectivelyVisible);
        Assert.Equal(segments, editor.Overview.Segments.Count);
        if (segments > 1)
        {
            Assert.Equal(segments, table.GetVisualDescendants().OfType<Grid>().Count(g => g.DataContext is ProjectOverviewSegment));
            var rows = table.GetVisualDescendants().OfType<Grid>().Where(g => g.DataContext is ProjectOverviewSegment).ToArray();
            // Rows stack without overlap and stay inside the overview block.
            for (int i = 1; i < rows.Length; i++)
                Assert.True(rows[i].TranslatePoint(default, window)!.Value.Y >= rows[i - 1].TranslatePoint(default, window)!.Value.Y + rows[i - 1].Bounds.Height - 0.5);
            Assert.All(rows, r => Assert.True(r.Bounds.Right <= block.Bounds.Width + 0.5));
            Assert.Equal((editor.Presentation.Result!.Engineering.GoverningSegmentIndex + 1).ToString(),
                row.GetVisualDescendants().OfType<TextBlock>().Last().Text);
        }
        window.Close();
    }
}
