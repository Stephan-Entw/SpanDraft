using Avalonia.Controls;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;
using SpanDraft.Desktop.Views;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Persistence;
using Xunit;
using static SpanDraft.Tests.ProjectTestSupport;

namespace SpanDraft.Tests;

[Collection("Schematic text")]
public sealed class DesktopSectionDefinitionTests
{
    public static IEnumerable<object[]> ShapesAndCultures()
    {
        for (int kind = 0; kind < 9; kind++)
            foreach (var culture in new[] { "de-DE", "en-US" }) yield return [kind, culture];
    }

    [Theory]
    [MemberData(nameof(ShapesAndCultures))]
    public async Task LoadedDefinitionsRenderAndUnchangedSetupPreservesSectionAxisAndMaterial(int kind, string culture)
    {
        using var environment = new DesktopControlEnvironment();
        using var scope = new UiCultureScope(culture);
        ISectionDefinition section = kind == 8 ? new ManualSectionDefinition(Area.FromSquareMeters(.003),
            new(SectionAxisDesignation.V, SecondMomentOfArea.FromMetersToTheFourth(8e-6), SectionModulus.FromCubicMeters(.0002)))
            : ParametricSectionTestSupport.Shape(kind, r: .5);
        var axis = section.Axes[^1].AxisDesignation;
        var material = new SpanDraft.Core.Materials.Material("Snapshot", ProjectTemplates.Material.YoungsModulus,
            ProjectTemplates.Material.YieldStrength, MassDensity.FromKilogramsPerCubicMeter(7850),
            SpanDraft.Core.Materials.PoissonRatio.FromValue(.3));
        var state = State() with { Document = State().Document.WithSection(section, axis) with { Material = material } };
        var app = new App(create: false); var path = TestPath("loaded.spandraft");
        app.Files.Data[path] = ProjectFileCodec.Serialize(state); app.Dialogs.OpenPath = path;
        Assert.True(await app.Main.OpenAsync());
        var d = app.Main.Editor!.Document; var count = app.Analyses;
        app.Main.EditProject(); var setup = app.Main.Setup;
        Assert.Same(d.Section, setup.SelectedSection); Assert.Same(d.Material, setup.SelectedMaterial);
        Assert.Equal(axis, setup.BendingAxis);
        Assert.Same(d.Section, setup.OriginalSection); Assert.Same(d.Material, setup.OriginalMaterial);
        var selected = d.Section.GetAxis(axis);
        string Format(double si, QuantityKind quantity) => QuantityFormatter.Format(si, quantity,
            setup.ResultPresentation.Profile, setup.ResultPresentation.Mode);
        Assert.Equal(Format(selected.SecondMomentOfArea.MetersToTheFourth, QuantityKind.SecondMomentOfArea), setup.Inertia);
        Assert.Equal("I" + axis.ToString().ToLowerInvariant(), setup.InertiaLabel);
        Assert.Equal("W" + axis.ToString().ToLowerInvariant(), setup.ModulusLabel);
        if (selected.PositiveSectionModulus == selected.NegativeSectionModulus)
            Assert.Equal(Format(selected.PositiveSectionModulus.CubicMeters, QuantityKind.SectionModulus), setup.Modulus);
        else
        {
            Assert.Contains("W+ = " + Format(selected.PositiveSectionModulus.CubicMeters, QuantityKind.SectionModulus), setup.Modulus);
            Assert.Contains("W− = " + Format(selected.NegativeSectionModulus.CubicMeters, QuantityKind.SectionModulus), setup.Modulus);
        }
        var view = new ProjectSetupView { DataContext = setup };
        var text = view.FindControl<TextBlock>("SectionDescription")!;
        var expectedCaption = SectionDisplay.Name(d.Section, setup.ResultPresentation.Profile);
        Assert.Equal(expectedCaption, text.Text);
        Assert.Equal(expectedCaption, app.Main.Editor.Overview.Section);
        var words = culture == "de-DE"
            ? new[] { "Rechteck", "Rechteckrohr", "Rundmaterial", "Rundrohr", "I/H-Profil", "U-Profil", "T-Profil", "Winkelprofil", "Benutzerquerschnitt" }
            : ["Rectangle", "Rectangular tube", "Circle", "Circular tube", "I/H section", "U section", "T section", "Angle", "Custom section"];
        Assert.StartsWith(words[kind], expectedCaption);
        setup.ApplyCommand.Execute(null);
        Assert.Same(d, app.Main.Editor.Document); Assert.False(app.Main.Session!.IsDirty);
        Assert.Equal(count, app.Analyses); Assert.Empty(app.Main.Session.UndoHistory);
        Assert.True(state.ContentEquals(app.Main.Session.CurrentRevision.State));
    }
}
