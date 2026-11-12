using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using SpanDraft.Analysis;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Controls;
using SpanDraft.Desktop.Layout;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;
using SpanDraft.Desktop.Views;
using SpanDraft.Solver;
using Xunit;
using SkiaSharp;
using SpanDraft.Desktop.Resources;

namespace SpanDraft.Tests;

[Collection("Avalonia")]
public sealed class DesktopResultDiagramControlTests : DesktopResultDiagramControlTestSupport
{

    [Theory]
    [InlineData("de-DE", ResultDiagramKind.TransverseDisplacement, "Durchbiegung")]
    [InlineData("en-US", ResultDiagramKind.TransverseDisplacement, "Deflection")]
    [InlineData("de-DE", ResultDiagramKind.ShearForce, "Querkraft")]
    [InlineData("en-US", ResultDiagramKind.ShearForce, "Shear force")]
    [InlineData("de-DE", ResultDiagramKind.BendingMoment, "Biegemoment")]
    [InlineData("en-US", ResultDiagramKind.BendingMoment, "Bending moment")]
    public void TransparentDiagramsRetainLocalizedTitlesAndGeometricLabelClearance(string culture, ResultDiagramKind kind, string title)
    {
        using var environment = new DesktopControlEnvironment();
        using var cultures = new ResultCultureScope(culture, culture);
        var document = Document();
        var diagram = new ResultDiagram { Width = 720, Height = 230, Kind = kind,
            BackgroundBrush = Brushes.Transparent,
            Presentation = AnalysisPresentationState.FromOutcome(BeamAnalysis.Analyze(document.ToBeamModel())),
            StationLayout = new BeamLayoutState().Update(document, 720, 240)!.Layout };
        foreach (string key in new[] { "CanvasBackground", "TextSecondary", "TextPrimary", "Border", "AxisStroke", "Accent" })
            diagram.Resources.Add(key, key == "CanvasBackground" ? Brushes.White : Brushes.Black);
        diagram.Measure(new Size(720, 230)); diagram.Arrange(new Rect(0, 0, 720, 230));
        Assert.StartsWith(title, diagram.Title);
        AssertReadableMarkers(diagram);
        Assert.All(diagram.MarkerLabels, label => Assert.Contains(
            label.Marker.Kind switch
            {
                ResultDiagramExtremumKind.Minimum => Strings.DiagramMinimum.Split('{')[0],
                ResultDiagramExtremumKind.Maximum => Strings.DiagramMaximum.Split('{')[0],
                _ => Strings.DiagramMinimumAndMaximum.Split('{')[0]
            }, label.Text));
        var drawing = new DrawingGroup();
        using (var context = drawing.Open()) diagram.Render(context);
        var curves = Assert.Single(drawing.Children.OfType<DrawingGroup>(), g => g.ClipGeometry is not null);
        Assert.All(diagram.MarkerLabels, label => Assert.False(curves.ClipGeometry!.FillContains(label.Bounds.Center)));
        using var bitmap = new RenderTargetBitmap(new PixelSize(720, 230)); bitmap.Render(diagram);
        using var stream = new MemoryStream(); bitmap.Save(stream, PngBitmapEncoderOptions.Default); stream.Position = 0;
        using var pixels = SKBitmap.Decode(stream);
        Assert.Equal(0, pixels.GetPixel(0, 0).Alpha);
        Assert.Equal(0, pixels.GetPixel(719, 229).Alpha);
        Assert.Contains(pixels.Pixels, p => p.Alpha != 0);
    }

    [Theory]
    [InlineData("de-DE", 1100, 650)]
    [InlineData("en-US", 1250, 800)]
    [InlineData("de-DE", 1600, 900)]
    public void NormalWorkspacePinsEditorAndSharesAllHorizontalAnchors(string culture, double width, double height)
    {
        using var environment = new DesktopControlEnvironment();
        using var scope = new UiCultureScope(culture);
        int calls = 0;
        var editor = new EditorViewModel(Document(), () => { }, b => { calls++; return BeamAnalysis.Analyze(b); });
        var view = new EditorView { DataContext = editor };
        var window = new Window { Content = view };
        Arrange(window, width, height - 32);
        var upper = view.FindControl<ScrollViewer>("EditorScroll")!;
        var lower = view.FindControl<ScrollViewer>("DiagramScroll")!;
        var surface = view.FindControl<BeamEditorSurface>("BeamSurface")!;
        var canvas = surface.FindControl<BeamCanvas>("TechnicalCanvas")!;
        var axis = surface.FindControl<CoordinateAxisPane>("CoordinateAxis")!;
        Assert.True(upper.Extent.Height <= upper.Viewport.Height);
        Assert.Equal(0, VerticalBar(upper).Opacity);
        Assert.False(VerticalBar(upper).IsHitTestVisible);
        Assert.Equal(0, upper.Offset.Y);
        Assert.Equal(upper.Bounds.Bottom, lower.Bounds.Top);
        Assert.Equal(upper.Viewport.Width, lower.Viewport.Width);
        Assert.Same(canvas.Scene!.Frame.Layout, surface.StationLayout);
        Assert.Same(surface.StationLayout, axis.StationLayout);
        Assert.Equal(3, Diagrams(view).Length);
        foreach (var diagram in Diagrams(view))
        {
            Assert.Equal(surface.Bounds.Width, diagram.Bounds.Width);
            Assert.Equal(surface.TranslatePoint(default, view)!.Value.X, diagram.TranslatePoint(default, view)!.Value.X);
            Assert.Same(surface.StationLayout, diagram.Projection!.StationLayout);
            Assert.Equal(180, diagram.Bounds.Height);
            AssertReadableMarkers(diagram);
            Render(diagram);
        }
        Assert.StartsWith(culture == "de-DE" ? "Durchbiegung" : "Deflection", Diagrams(view)[0].Title);
        Assert.Equal(1, calls);
        Snapshot(view, $"workspace-{(int)width}-{(int)height}");
    }

    [Fact]
    public void DraftsCommitsMetadataAndHistoryShareOnlyTheExistingAnalysisBoundary()
    {
        using var environment = new DesktopControlEnvironment();
        int calls = 0;
        var editor = new EditorViewModel(Document(), () => { }, b => { calls++; return BeamAnalysis.Analyze(b); });
        var view = new EditorView { DataContext = editor };
        var window = new Window { Content = view };
        Arrange(window);
        var original = editor.Presentation;
        var plots = Diagrams(view).Select(d => d.Projection).ToArray();
        var load = editor.Document.Loads[0];
        editor.HoverLoad(load.Id);
        editor.EditLoad(load.Id);
        editor.LoadDraft!.ValueText = "-2000";
        editor.LoadDraft.PositionText = "700";
        editor.BeginLoadDrag(load.Id); editor.UpdateLoadDrag(Length.FromMeters(.7)); editor.EndLoadDrag();
        Arrange(window);
        Assert.Same(original, editor.Presentation);
        for (int i = 0; i < plots.Length; i++) Assert.Same(plots[i], Diagrams(view)[i].Projection);
        Assert.Equal(1, calls);

        Assert.True(editor.ConfirmLoad()); Arrange(window);
        Assert.Equal(2, calls);
        Assert.NotSame(original, editor.Presentation);
        var committed = editor.Presentation;
        Assert.All(Diagrams(view), d => Assert.Same(committed, d.Presentation));
        editor.EditLoad(load.Id); editor.LoadDraft!.NameText = "Renamed";
        Assert.True(editor.ConfirmLoad());
        editor.PreviewAnnotationOffset(load.Id, new(0, -30));
        Arrange(window); editor.ClearAnnotationPreview();
        editor.SetAnnotationOffset(load.Id, new(0, -30)); Arrange(window);
        Assert.Same(committed, editor.Presentation); Assert.Equal(2, calls);
        Assert.True(editor.Undo()); Assert.True(editor.Undo()); Arrange(window); // Annotation and rename.
        Assert.Equal(2, calls);
        Assert.True(editor.Undo()); Arrange(window); Assert.Equal(3, calls);
        Assert.Equal(-1000, editor.Document.Loads[0].Value);
        Assert.True(editor.Redo()); Arrange(window); Assert.Equal(4, calls);
        Assert.Equal(-2000, editor.Document.Loads[0].Value);
        Assert.All(Diagrams(view), Render);
        view.FindControl<ScrollViewer>("DiagramScroll")!.Offset = new(0, 100);
        Arrange(window, 1100, 618); Arrange(window, 1600, 868);
        Assert.Equal(4, calls);

        editor.Session.Commit(new(editor.Document.WithSupports([]), editor.EditorPresentation)); Arrange(window);
        Assert.Equal(5, calls);
        Assert.Equal(AnalysisPresentationKind.MissingSupports, editor.Presentation.Kind);
        Assert.All(Diagrams(view), d => { Assert.Null(d.Projection); Assert.Same(editor.Presentation, d.Presentation); Render(d); });
        Assert.True(editor.Undo()); Arrange(window);
        Assert.Equal(6, calls); Assert.All(Diagrams(view), d => Assert.NotNull(d.Projection));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnloadedStableModelShowsZeroWhileUnstableModelClearsCurves(bool unstable)
    {
        using var environment = new DesktopControlEnvironment();
        var document = Document().WithLoads([]);
        if (unstable) document = document.WithSupports([document.Supports[0] with { Type = SupportType.Pinned }]);
        var editor = new EditorViewModel(document, () => { });
        var view = new EditorView { DataContext = editor };
        var window = new Window { Content = view }; Arrange(window);
        foreach (var d in Diagrams(view))
        {
            if (unstable) Assert.Null(d.Projection);
            else { Assert.True(d.Projection!.Scale.IsZero); Assert.Single(d.Projection.Ticks); }
            Render(d);
        }
    }

    [Theory]
    [InlineData(AnalysisPresentationKind.MissingSupports)]
    [InlineData(AnalysisPresentationKind.IncompleteModel)]
    [InlineData(AnalysisPresentationKind.UnstableModel)]
    [InlineData(AnalysisPresentationKind.IllConditionedSystem)]
    [InlineData(AnalysisPresentationKind.NumericalFailure)]
    public void EveryUnavailablePresentationImmediatelyRemovesOldGeometry(AnalysisPresentationKind kind)
    {
        using var environment = new DesktopControlEnvironment();
        var editor = new EditorViewModel(Document(), () => { });
        var view = new EditorView { DataContext = editor };
        var window = new Window { Content = view }; Arrange(window);
        foreach (var diagram in Diagrams(view))
        {
            Assert.NotNull(diagram.Projection);
            string title = diagram.Title;
            diagram.Presentation = new(kind, null);
            Assert.Null(diagram.Projection);
            Assert.Empty(diagram.MarkerLabels);
            Assert.Equal(title, diagram.Title);
            var drawing = new DrawingGroup();
            using (var context = drawing.Open()) diagram.Render(context);
            // Background and text remain; no curve/grid/zero-line pens survive.
            Assert.DoesNotContain(drawing.Children.OfType<GeometryDrawing>(), d => d.Pen is not null);
        }
    }

    [Theory]
    [InlineData("de-DE", "en-US", 2, 1250)]
    [InlineData("en-US", "de-DE", 2, 1250)]
    [InlineData("de-DE", "en-US", 3, 900)]
    [InlineData("en-US", "de-DE", 3, 900)]
    public void PresentationChangesRefreshBoundResultsWithRegionalNumbersAndPreserveSolverAndXMapping(
        string uiCulture, string culture, int profileIndex, double width)
    {
        using var environment = new DesktopControlEnvironment();
        using var cultures = new ResultCultureScope(culture, uiCulture);
        int analyses = 0;
        var main = new MainWindowViewModel(b => { analyses++; return BeamAnalysis.Analyze(b); }); main.NewCommand.Execute(null);
        ProjectTestSupport.CompleteSetup(main); main.Setup.ApplyCommand.Execute(null);
        var editor = main.Editor!;
        var document = Document().WithLoads([
            new EditorPointForce(Guid.NewGuid(), Length.FromMeters(.03), Force.FromNewtons(-1234567.89), "F1"),
            new EditorPointMoment(Guid.NewGuid(), Length.FromMeters(.04), Moment.FromNewtonMeters(123456.789), "M1")]);
        Assert.True(main.Session!.Commit(main.Session.CurrentRevision.State with { Document = document }));
        var result = editor.Presentation.Result!;
        var view = new EditorView { DataContext = editor };
        var window = new Window { Content = view };
        Arrange(window, width, 950);
        var diagrams = Diagrams(view);
        var layouts = diagrams.Select(d => d.Projection!.StationLayout).ToArray();
        var markers = diagrams.Select(d => d.Projection!.Markers.ToArray()).ToArray();
        var jumps = diagrams.Select(d => d.Projection!.Jumps.ToArray()).ToArray();
        int count = analyses;
        var profile = PresentationTestProfiles.Profile(profileIndex);
        main.SetResultPresentation(profile, 3);
        Arrange(window, width, 950);
        var projections = diagrams.Select(d => d.Projection!).ToArray();
        for (int i = 0; i < diagrams.Length; i++)
        {
            var diagram = diagrams[i];
            var projection = projections[i];
            var kind = ResultDiagramProjection.QuantityOf(diagram.Kind);
            Assert.Same(layouts[i], projection.StationLayout);
            Assert.Same(profile[kind], projection.Unit);
            Assert.EndsWith($"[{profile[kind].Symbol}]", diagram.Title);
            Assert.Equal(markers[i].Select(m => (m.Kind, m.Position, m.Side, m.SiValue, m.Screen.X)),
                projection.Markers.Select(m => (m.Kind, m.Position, m.Side, m.SiValue, m.Screen.X)));
            Assert.Equal(jumps[i].Select(j => (j.Left.Position, j.Left.Side, j.Left.SiValue, j.Right.Side, j.Right.SiValue)),
                projection.Jumps.Select(j => (j.Left.Position, j.Left.Side, j.Left.SiValue, j.Right.Side, j.Right.SiValue)));
            foreach (var point in projection.Sections.SelectMany(s => s))
            {
                var raw = result.Solution.EvaluateAt(point.Position, point.Side);
                double si = diagram.Kind switch
                {
                    ResultDiagramKind.TransverseDisplacement => raw.TransverseDisplacement.Meters,
                    ResultDiagramKind.ShearForce => raw.ShearForce.Newtons,
                    _ => raw.BendingMoment.NewtonMeters
                };
                Assert.Equal(si, point.SiValue);
                Assert.Equal(profile[kind].FromSi(si), point.Value);
                Assert.Equal(layouts[i].Transform.PhysicalToScreen(point.Position.Meters), point.Screen.X);
            }
            Assert.Equal(projection.Ticks.Count, projection.Ticks.Select(diagram.TickLabel).Distinct().Count());
            foreach (var tick in projection.Ticks)
                Assert.Equal(AxisTickFormatter.Format(tick.Index, projection.Scale.StepMantissa,
                    projection.Scale.StepExponent, System.Globalization.CultureInfo.GetCultureInfo(culture)), diagram.TickLabel(tick));
            AssertReadableMarkers(diagram);
            Snapshot(diagram, $"phase2-{uiCulture}-{profileIndex}-standard-{diagram.Kind}");
        }
        var overview = Assert.Single(view.GetVisualDescendants().OfType<ProjectOverviewView>());
        Assert.Equal(editor.Overview.ReactionXHeader, TechnicalTextPresenter.GetContent(overview.FindControl<TextBlock>("ReactionXHeader")!)!.PlainText);
        Assert.Equal(editor.Overview.ReactionYHeader, TechnicalTextPresenter.GetContent(overview.FindControl<TextBlock>("ReactionYHeader")!)!.PlainText);
        Assert.Equal(editor.Overview.ReactionMomentHeader, TechnicalTextPresenter.GetContent(overview.FindControl<TextBlock>("ReactionMomentHeader")!)!.PlainText);
        Snapshot(view, $"phase2-{uiCulture}-{profileIndex}-standard-workspace");
        main.SetResultPresentation(profile, 5);
        Arrange(window, width, 950);
        Assert.Equal(count, analyses);
        Assert.Same(result, editor.Presentation.Result);
        for (int i = 0; i < diagrams.Length; i++)
        {
            Assert.Same(projections[i], diagrams[i].Projection);
            AssertReadableMarkers(diagrams[i]);
            foreach (var label in diagrams[i].MarkerLabels)
                Assert.Contains(QuantityFormatter.FormatResult(label.Marker.SiValue,
                    ResultDiagramProjection.QuantityOf(diagrams[i].Kind), profile, 5), label.Text);
            Snapshot(diagrams[i], $"phase2-{uiCulture}-{profileIndex}-detailed-{diagrams[i].Kind}");
        }
        Snapshot(view, $"phase2-{uiCulture}-{profileIndex}-detailed-workspace");
        Assert.True(main.Session.Commit(main.Session.CurrentRevision.State with { Document = document.WithSupports([]) }));
        Assert.All(diagrams, d => { Assert.Null(d.Projection); Assert.Empty(d.MarkerLabels); });
        main.SetResultPresentation(UnitProfile.Default, 3);
        Assert.All(diagrams, d => { Assert.Null(d.Projection); Assert.Empty(d.MarkerLabels); });
        Assert.Empty(editor.Overview.Reactions);
        window.Content = null;
    }

    [Fact]
    public void PlacementFreezesPublishedMappingAcrossResizeUntilCancellation()
    {
        using var environment = new DesktopControlEnvironment();
        int calls = 0;
        var editor = new EditorViewModel(Document(), () => { }, b => { calls++; return BeamAnalysis.Analyze(b); });
        var view = new EditorView { DataContext = editor };
        var window = new Window { Content = view }; Arrange(window);
        var surface = view.FindControl<BeamEditorSurface>("BeamSurface")!;
        var layout = surface.StationLayout;
        var presentation = editor.Presentation;
        editor.ToggleLoadTool(PointLoadKind.Force);
        editor.HoverLoadPlacement(Length.FromMeters(.01));
        Arrange(window, 1100, 618);
        Assert.Same(layout, surface.StationLayout);
        Assert.Same(presentation, editor.Presentation);
        Assert.All(Diagrams(view), d => Assert.Same(layout, d.Projection!.StationLayout));
        Assert.All(Diagrams(view), AssertReadableMarkers);
        editor.CancelEditorInteraction(); Arrange(window, 1100, 618);
        Assert.NotSame(layout, surface.StationLayout);
        Assert.All(Diagrams(view), d => Assert.Same(surface.StationLayout, d.Projection!.StationLayout));
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData("de-DE", 1100, -1000)]
    [InlineData("en-US", 1250, 1000)]
    [InlineData("de-DE", 1600, -1e-12)]
    public void EdgeMarkersAreReadableAndUseResultPrecisionAndUnits(string culture, double width, double force)
    {
        using var environment = new DesktopControlEnvironment();
        using var scope = new UiCultureScope(culture);
        var document = Document().WithLoads([
            new EditorPointForce(Guid.NewGuid(), Length.FromMeters(1), Force.FromNewtons(force), "F1")]);
        var editor = new EditorViewModel(document, () => { });
        var view = new EditorView { DataContext = editor };
        var window = new Window { Content = view };
        Arrange(window, width, 618);
        foreach (var diagram in Diagrams(view))
        {
            AssertReadableMarkers(diagram);
            string unit = diagram.Kind switch
            {
                ResultDiagramKind.TransverseDisplacement => "mm",
                ResultDiagramKind.ShearForce => "N",
                _ => "N·m"
            };
            Assert.All(diagram.MarkerLabels, label =>
            {
                Assert.EndsWith(" " + unit, label.Text);
                Assert.Contains(QuantityFormatter.FormatResult(label.Marker.SiValue,
                    ResultDiagramProjection.QuantityOf(diagram.Kind)), label.Text);
                Assert.DoesNotContain("E", label.Text);
                if (label.Marker.Value != 0) Assert.DoesNotContain("= 0 ", label.Text);
            });
        }
        Snapshot(view, $"edge-markers-{culture}-{(int)width}");
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("en-US")]
    public void ConstantTinyValuesStayVisible(string culture)
    {
        using var environment = new DesktopControlEnvironment();
        using var scope = new ResultCultureScope(culture, culture);
        var document = Document().WithLoads([
            new EditorPointForce(Guid.NewGuid(), Length.FromMeters(1), Force.FromNewtons(-1e-8), "F1"),
            new EditorPointMoment(Guid.NewGuid(), Length.FromMeters(1), Moment.FromNewtonMeters(1000), "M1")]);
        var editor = new EditorViewModel(document, () => { });
        var view = new EditorView { DataContext = editor };
        var window = new Window { Content = view };
        Arrange(window, 900, 950);
        var diagram = Assert.Single(Diagrams(view), d => d.Kind == ResultDiagramKind.ShearForce);
        var label = Assert.Single(diagram.MarkerLabels);
        Assert.NotEqual(0, label.Marker.SiValue);
        Assert.Equal("min = max = " + QuantityFormatter.FormatResult(label.Marker.SiValue, QuantityKind.TransverseForce), label.Text);
        AssertReadableMarkers(diagram);
        Snapshot(diagram, $"approximate-constant-{culture}");
        window.Content = null;
    }

    [Theory]
    [InlineData("de-DE", -1e-8, "min = ")]
    [InlineData("en-US", -1e-8, "min = ")]
    [InlineData("de-DE", 1e-8, "max = ")]
    [InlineData("en-US", 1e-8, "max = ")]
    public void TinyExtremaRemainVisibleWhenPrecisionChanges(string culture, double force, string expected)
    {
        using var environment = new DesktopControlEnvironment();
        using var scope = new ResultCultureScope(culture, culture);
        var document = Document().WithLoads([
            new EditorPointForce(Guid.NewGuid(), Length.FromMeters(1), Force.FromNewtons(force), "F1")]);
        var editor = new EditorViewModel(document, () => { });
        var view = new EditorView { DataContext = editor };
        var window = new Window { Content = view };
        Arrange(window, 900, 950);
        var diagram = Assert.Single(Diagrams(view), d => d.Kind == ResultDiagramKind.TransverseDisplacement);
        var projection = diagram.Projection;
        var label = Assert.Single(diagram.MarkerLabels, l => l.Marker.SiValue != 0);
        Assert.Equal(expected + QuantityFormatter.FormatResult(label.Marker.SiValue, QuantityKind.TransverseDisplacement), label.Text);
        Assert.Equal(force < 0 ? ResultDiagramExtremumKind.Minimum : ResultDiagramExtremumKind.Maximum, label.Marker.Kind);
        Assert.Contains(diagram.MarkerLabels, l => l.Text == (force < 0 ? "max = 0 mm" : "min = 0 mm"));
        AssertReadableMarkers(diagram);
        Snapshot(diagram, $"approximate-comparison-{culture}-{(force < 0 ? "minimum" : "maximum")}");
        diagram.Presentation = editor.Presentation.WithPresentation(new(significantDigits: 5));
        Assert.Same(projection, diagram.Projection);
        Assert.All(diagram.MarkerLabels, l => Assert.DoesNotContain("≈", l.Text));
        Assert.Contains(diagram.MarkerLabels, l => l.Text == (force < 0 ? "min = " : "max = ")
            + QuantityFormatter.FormatResult(label.Marker.SiValue, QuantityKind.TransverseDisplacement, significantDigits: 5));
        window.Content = null;
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("en-US")]
    public void ConstantZeroLabelsCombineMinimumAndMaximumInAllDiagrams(string culture)
    {
        using var environment = new DesktopControlEnvironment();
        using var scope = new UiCultureScope(culture);
        var editor = new EditorViewModel(Document().WithLoads([]), () => { });
        var view = new EditorView { DataContext = editor };
        var window = new Window { Content = view }; Arrange(window);
        foreach (var diagram in Diagrams(view))
        {
            AssertReadableMarkers(diagram);
            var label = Assert.Single(diagram.MarkerLabels);
            Assert.StartsWith("min = max = 0 ", label.Text);
            Assert.Equal(ResultDiagramExtremumKind.MinimumAndMaximum, label.Marker.Kind);
            Assert.Equal(EvaluationSide.Right, label.Marker.Side);
        }
        Snapshot(view, $"zero-markers-{culture}");
    }

    [Theory]
    [InlineData(false, "de-DE")]
    [InlineData(true, "en-US")]
    public void MomentLabelsDoNotOverlapAtSharedXOrNearlyIdenticalY(bool nearlyConstant, string culture)
    {
        using var environment = new DesktopControlEnvironment();
        using var scope = new UiCultureScope(culture);
        var document = Document().WithLoads(nearlyConstant
            ? [new EditorPointMoment(Guid.NewGuid(), Length.FromMeters(.01), Moment.FromNewtonMeters(1e-7), "M1"),
               new EditorPointMoment(Guid.NewGuid(), Length.FromMeters(1), Moment.FromNewtonMeters(1000), "M2")]
            : [new EditorPointMoment(Guid.NewGuid(), Length.FromMeters(.5), Moment.FromNewtonMeters(400), "M1")]);
        if (!nearlyConstant) document = document.WithSupports([
            new(Guid.NewGuid(), Length.FromMeters(0), SupportType.Pinned, "A"),
            new(Guid.NewGuid(), Length.FromMeters(1), SupportType.Roller, "B")]);
        var editor = new EditorViewModel(document, () => { });
        var diagram = new ResultDiagram
        {
            Kind = ResultDiagramKind.BendingMoment, Presentation = editor.Presentation,
            StationLayout = StationLayout.Compute(1, 40, 260, [new(nearlyConstant ? .01 : .5, 24, 24)])
        };
        var window = new Window { Content = diagram };
        window.ApplyTemplate(); window.Measure(new Size(300, 180)); window.Arrange(new Rect(0, 0, 300, 180));
        diagram.InvalidateMeasure(); diagram.Measure(new Size(300, 180));
        diagram.InvalidateArrange(); diagram.Arrange(new Rect(0, 0, 300, 180));
        Assert.Equal(2, diagram.Projection!.Markers.Count);
        var markers = diagram.Projection.Markers;
        if (nearlyConstant) Assert.InRange(Math.Abs(markers[0].Screen.Y - markers[1].Screen.Y), 0, 1);
        else Assert.Equal(markers[0].Screen.X, markers[1].Screen.X);
        AssertReadableMarkers(diagram);
    }
}
