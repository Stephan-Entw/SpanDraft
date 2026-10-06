using System.Globalization;
using SpanDraft.Analysis;
using SpanDraft.Core.Beams;
using SpanDraft.Core.Loads;
using SpanDraft.Core.Materials;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Controls;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;
using Xunit;

namespace SpanDraft.Tests;

public class DesktopStateTests
{
    private sealed class UiCultureScope : IDisposable
    {
        private readonly CultureInfo _previous = CultureInfo.CurrentUICulture;
        public UiCultureScope(string culture) => CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        public void Dispose() => CultureInfo.CurrentUICulture = _previous;
    }

    private sealed class Session
    {
        public int Calls { get; private set; }
        public BeamModel? LastBeam { get; private set; }
        public MainWindowViewModel Main { get; }
        public EditorViewModel Editor => Main.Editor!;

        public Session()
        {
            Main = new(beam =>
            {
                Calls++;
                LastBeam = beam;
                return BeamAnalysis.Analyze(beam);
            });
        }

        public void Create() => Main.Setup.ApplyCommand.Execute(null);
    }

    [Fact]
    public void StartupHasSetupAndNoDocumentOrAnalysis()
    {
        var s = new Session();
        Assert.Equal(MainViewMode.ProjectSetup, s.Main.Mode);
        Assert.Null(s.Main.Editor);
        Assert.Same(s.Main.Setup, s.Main.CurrentViewModel);
        Assert.Equal(ProjectSetupMode.Create, s.Main.Setup.Mode);
        Assert.Same(ProjectTemplates.Section, s.Main.Setup.SelectedSection);
        Assert.Same(ProjectTemplates.Material, s.Main.Setup.SelectedMaterial);
        Assert.Equal(0, s.Calls);
    }

    [Fact]
    public void CreateBuildsExpectedImmutableBeamAndAnalyzesOnce()
    {
        var s = new Session();
        s.Create();
        Assert.Equal(MainViewMode.Editor, s.Main.Mode);
        Assert.Same(s.Editor, s.Main.CurrentViewModel);
        Assert.Equal(1000, s.Editor.Document.Length.Millimeters);
        var section = Assert.IsType<RectangularHollowSection>(s.Editor.Document.Section);
        Assert.Equal(100, section.Width.Millimeters);
        Assert.Equal(100, section.Height.Millimeters);
        Assert.Equal(5, section.WallThickness.Millimeters);
        Assert.Equal("S235JR", s.Editor.Document.Material.Name);
        Assert.Equal(210e9, s.Editor.Document.Material.YoungsModulus.Pascals);
        Assert.Equal(235e6, s.Editor.Document.Material.YieldStrength.Pascals);
        Assert.Empty(s.LastBeam!.Supports);
        Assert.Empty(s.LastBeam.Loads);
        Assert.Equal(s.Editor.Document.Length, s.LastBeam.Length);
        Assert.Same(section, s.LastBeam.Section);
        Assert.Equal(1, s.Calls);
    }

    [Theory]
    [InlineData("de-DE", "1000,5")]
    [InlineData("en-US", "1000.5")]
    public void InlineLengthCommitsToOneDocumentAndRefreshesDisplay(string cultureName, string text)
    {
        using var culture = new UiCultureScope(cultureName);
        var s = new Session();
        s.Create();
        EditorDocument previous = s.Editor.Document;
        var input = s.Editor.DimensionLength;
        input.Begin();
        input.Text = text;
        Assert.Same(previous, s.Editor.Document);
        Assert.Equal(1, s.Calls);
        Assert.Equal("1000 mm", s.Editor.DimensionLength.DisplayText);
        Assert.True(input.Confirm());
        input.LoseFocus();
        Assert.Equal(1000.5, s.Editor.Document.Length.Millimeters);
        Assert.NotSame(previous, s.Editor.Document);
        Assert.Equal(1000, previous.Length.Millimeters);
        Assert.Equal(s.Editor.Document.Length, s.LastBeam!.Length);
        Assert.Equal(text, input.Text);
        Assert.Equal(text + " mm", s.Editor.DimensionLength.DisplayText);
        Assert.False(input.IsEditing);
        Assert.Equal(2, s.Calls);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("1e309")]
    [InlineData("1e-323")]
    [InlineData("1,000.5")]
    public void InvalidEnterRetainsCommittedStateAndAllowsCorrection(string text)
    {
        using var culture = new UiCultureScope("en-US");
        var s = new Session();
        s.Create();
        var previous = s.Editor.Document;
        var input = s.Editor.DimensionLength;
        input.Begin();
        input.Text = text;
        Assert.False(input.Confirm());
        Assert.True(input.IsEditing);
        Assert.True(input.HasError);
        Assert.Same(previous, s.Editor.Document);
        Assert.Equal(1, s.Calls);
        input.Text = "1200";
        Assert.False(input.HasError);
        Assert.True(input.Confirm());
        Assert.Equal(1200, s.Editor.Document.Length.Millimeters);
        Assert.Equal(2, s.Calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EscapeDiscardsValidOrInvalidInputWithoutAnalysis(bool valid)
    {
        var s = new Session();
        s.Create();
        var original = s.Editor.Document;
        var input = s.Editor.DimensionLength;
        input.Begin();
        input.Text = valid ? "1500" : "invalid";
        input.Cancel();
        input.LoseFocus();
        Assert.False(input.IsEditing);
        Assert.False(input.HasError);
        Assert.Equal("1000", input.Text);
        Assert.Same(original, s.Editor.Document);
        Assert.Equal(1, s.Calls);
    }

    [Theory]
    [InlineData("1500", 1500, 2)]
    [InlineData("invalid", 1000, 1)]
    [InlineData("1000", 1000, 1)]
    public void FocusLossCommitsOrRestoresAndCannotCommitTwice(string text, double expected, int calls)
    {
        var s = new Session();
        s.Create();
        var input = s.Editor.DimensionLength;
        input.Begin();
        input.Text = text;
        input.LoseFocus();
        input.LoseFocus();
        Assert.False(input.IsEditing);
        Assert.False(input.HasError);
        Assert.Equal(expected, s.Editor.Document.Length.Millimeters);
        Assert.Equal(UiNumbers.Format(expected), input.Text);
        Assert.Equal(calls, s.Calls);
    }

    [Fact]
    public void UnchangedEnterDoesNotAnalyze()
    {
        var s = new Session();
        s.Create();
        s.Editor.DimensionLength.Begin();
        Assert.True(s.Editor.DimensionLength.Confirm());
        Assert.Equal(1, s.Calls);
    }

    [Fact]
    public void EditCancelDiscardsTemporaryChoicesAndPreservesEditorIdentity()
    {
        var s = new Session();
        s.Create();
        var editor = s.Editor;
        var document = editor.Document;
        s.Main.EditProject();
        Assert.Equal(ProjectSetupMode.Edit, s.Main.Setup.Mode);
        Assert.Same(document.Section, s.Main.Setup.SelectedSection);
        Assert.Same(document.Material, s.Main.Setup.SelectedMaterial);
        s.Main.Setup.SelectedMaterial = new("Temporary", Pressure.FromPascals(100e9), Pressure.FromMegapascals(100));
        s.Main.Setup.SelectedSection = new RectangleSection(Length.FromMillimeters(30), Length.FromMillimeters(40));
        Assert.Same(document, editor.Document);
        s.Main.Setup.CancelCommand.Execute(null);
        Assert.Equal(MainViewMode.Editor, s.Main.Mode);
        Assert.Same(editor, s.Editor);
        Assert.Same(document, s.Editor.Document);
        Assert.Equal(1, s.Calls);
    }

    [Fact]
    public void EditApplyKeepsLengthAndCommitsSectionAndMaterialTogetherOnce()
    {
        var s = new Session();
        s.Create();
        s.Editor.DimensionLength.Begin();
        s.Editor.DimensionLength.Text = "1500";
        s.Editor.DimensionLength.Confirm();
        var editor = s.Editor;
        s.Main.EditProject();
        var material = new Material("Alternative", Pressure.FromPascals(100e9), Pressure.FromMegapascals(100));
        var section = new RectangleSection(Length.FromMillimeters(30), Length.FromMillimeters(40));
        s.Main.Setup.SelectedMaterial = material;
        s.Main.Setup.SelectedSection = section;
        Assert.Equal(2, s.Calls);
        s.Main.Setup.ApplyCommand.Execute(null);
        Assert.Same(editor, s.Editor);
        Assert.Same(section, s.Editor.Document.Section);
        Assert.Same(material, s.Editor.Document.Material);
        Assert.Equal(1500, s.Editor.Document.Length.Millimeters);
        Assert.Same(section, s.LastBeam!.Section);
        Assert.Same(material, s.LastBeam.Material);
        Assert.Equal(3, s.Calls);
        Assert.Equal(MainViewMode.Editor, s.Main.Mode);
    }

    [Fact]
    public void ApplyUnchangedSetupPreservesDocumentAndDoesNotAnalyzeAgain()
    {
        var s = new Session();
        s.Create();
        var document = s.Editor.Document;
        s.Main.EditProject();
        s.Main.Setup.ApplyCommand.Execute(null);
        Assert.Equal(1, s.Calls);
        Assert.Same(document, s.Editor.Document);
    }

    [Theory]
    [InlineData("de-DE", "1000,5")]
    [InlineData("en-US", "1000.5")]
    public void ParsingAndFormattingUseUiCultureEvenWhenNumericCultureDiffers(string culture, string input)
    {
        using var scope = new UiCultureScope(culture);
        var old = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            Assert.True(UiNumbers.TryParseLength(input, out var length));
            Assert.Equal(1000.5, length.Millimeters);
            Assert.Equal(input, UiNumbers.Format(length.Millimeters));
            Assert.Equal("1000", UiNumbers.Format(1000));
        }
        finally { CultureInfo.CurrentCulture = old; }
    }

    [Theory]
    [InlineData("de-DE", "Querschnitt", "Projekt erstellen")]
    [InlineData("en-US", "Section", "Create Project")]
    [InlineData("fr-FR", "Section", "Create Project")]
    public void ResourcesLocalizeAndFallbackToEnglish(string culture, string section, string create)
    {
        using var scope = new UiCultureScope(culture);
        Assert.Equal(section, Strings.Section);
        Assert.Equal(create, Strings.CreateProject);
    }

    private static BeamModel AnalysisBeam(IEnumerable<Support> supports, IEnumerable<BeamLoad>? loads = null) =>
        new(Length.FromMeters(2), ProjectTemplates.Material,
            new CustomSection(Area.FromSquareMeters(0.01),
                SecondMomentOfArea.FromMetersToTheFourth(1e-6), SectionModulus.FromCubicMeters(0.001)),
            supports, loads ?? []);

    [Fact]
    public void MissingSupportsUsesStructuredCodeAndNoTechnicalDiagnostic()
    {
        using var culture = new UiCultureScope("de-DE");
        var outcome = BeamAnalysis.Analyze(AnalysisBeam([]));
        var state = AnalysisPresentationState.FromOutcome(outcome);
        Assert.Equal(AnalysisPresentationKind.MissingSupports, state.Kind);
        Assert.Equal("Berechnung nicht verfügbar · Lagerung fehlt", state.StatusText);
        Assert.DoesNotContain(outcome.Failure!.TechnicalMessage, state.StatusText);
        Assert.Null(state.Result);
        Assert.False(state.IsSuccess);
        Assert.Empty(state.Displacement);
    }

    [Theory]
    [InlineData(AnalysisPresentationKind.IncompleteModel)]
    [InlineData(AnalysisPresentationKind.UnstableModel)]
    [InlineData(AnalysisPresentationKind.IllConditionedSystem)]
    [InlineData(AnalysisPresentationKind.NumericalFailure)]
    public void StructuredAnalysisFailuresMapToLocalizedPresentation(AnalysisPresentationKind kind)
    {
        using var culture = new UiCultureScope("de-DE");
        var fixedSupport = new Support(Length.FromMeters(0), SupportType.Fixed);
        BeamModel beam = kind switch
        {
            AnalysisPresentationKind.IncompleteModel => AnalysisBeam([new(Length.FromMeters(3), SupportType.Fixed)]),
            AnalysisPresentationKind.UnstableModel => AnalysisBeam([new(Length.FromMeters(0), SupportType.Roller),
                new(Length.FromMeters(2), SupportType.Roller)]),
            AnalysisPresentationKind.IllConditionedSystem => AnalysisBeam([fixedSupport],
                [new PointForce(Length.FromMeters(1), Force.FromNewtons(-1000)),
                 new PointForce(Length.FromMeters(1 + 1e-6), Force.FromNewtons(0))]),
            _ => AnalysisBeam([fixedSupport],
                [new PointForce(Length.FromMeters(2), Force.FromNewtons(double.MaxValue)),
                 new PointForce(Length.FromMeters(2), Force.FromNewtons(double.MaxValue))])
        };
        var outcome = BeamAnalysis.Analyze(beam);
        var state = AnalysisPresentationState.FromOutcome(outcome);
        Assert.Equal(kind, state.Kind);
        Assert.Null(state.Result);
        Assert.DoesNotContain(outcome.Failure!.TechnicalMessage, state.StatusText);
        Assert.Equal(kind switch
        {
            AnalysisPresentationKind.IncompleteModel => "Berechnung nicht verfügbar · Modell unvollständig",
            AnalysisPresentationKind.UnstableModel => "Berechnung nicht verfügbar · Lagerung nicht ausreichend",
            _ => "Berechnung nicht möglich"
        }, state.StatusText);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SuccessDisplaysExistingEngineeringResultAndInfinity(bool loaded)
    {
        using var culture = new UiCultureScope("de-DE");
        var outcome = BeamAnalysis.Analyze(AnalysisBeam([new(Length.FromMeters(0), SupportType.Fixed)],
            loaded ? [new PointForce(Length.FromMeters(2), Force.FromNewtons(-1000))] : []));
        var state = AnalysisPresentationState.FromOutcome(outcome);
        Assert.Equal(AnalysisPresentationKind.Success, state.Kind);
        Assert.True(state.IsSuccess);
        Assert.Same(outcome.Result, state.Result);
        var engineering = outcome.Result!.Engineering;
        Assert.Equal(UiNumbers.Indicator(engineering.TransverseDisplacementMagnitude.Meters * 1000) + " mm", state.Displacement);
        Assert.Equal(UiNumbers.Indicator(engineering.BendingMomentMagnitude.NewtonMeters / 1000) + " kNm", state.Moment);
        Assert.Equal(UiNumbers.Indicator(engineering.MaximumBendingStress.Megapascals) + " MPa", state.Stress);
        Assert.Equal(UiNumbers.Indicator(engineering.SafetyFactor), state.SafetyFactor);
        if (!loaded) Assert.Equal("∞", state.SafetyFactor);
    }

    [Theory]
    [InlineData(900, 500, 1)]
    [InlineData(1100, 650, 2.5)]
    [InlineData(1, 1, 1e-12)]
    [InlineData(900, 500, 1e300)]
    public void ViewportMapsEndpointsAndRoundTripsInteriorWithoutMillimeterPixelCoupling(
        double width, double height, double length)
    {
        var viewport = DesktopLayoutFixture.Fit(width, height, length);
        Assert.Equal(viewport.Layout.Stations[0].ScreenX, viewport.Layout.Transform.PhysicalToScreen(0));
        Assert.Equal(viewport.Layout.Stations[^1].ScreenX, viewport.Layout.Transform.PhysicalToScreen(length));
        NumericAssert.Close(length * 0.25, viewport.Layout.Transform.ScreenToPhysical(viewport.Layout.Transform.PhysicalToScreen(length * 0.25)));
        Assert.True(viewport.Layout.Stations[^1].ScreenX > viewport.Layout.Stations[0].ScreenX);
        Assert.Equal(height * 0.5, viewport.Viewport.BeamY);
    }
}
