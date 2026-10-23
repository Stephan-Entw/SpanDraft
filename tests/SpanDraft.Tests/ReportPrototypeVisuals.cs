using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Skia.Helpers;
using Avalonia.Styling;
using Avalonia.Threading;
using SkiaSharp;
using SpanDraft.Desktop.Controls;
using SpanDraft.Desktop.Layout;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.State;
using SpanDraft.Reporting.Prototype;

namespace SpanDraft.Tests;

/// <summary>Local prototype bridge: the product controls draw directly into the report canvas.</summary>
internal sealed class ReportPrototypeVisuals
{
    internal const double VisualScale = .8;
    private const double SupportSymbolScale = .75;
    private const double LoadSymbolScale = .85;
    internal const double PointsPerDip = 72d / 96 * VisualScale;
    private static readonly double AnnotationFontSize = Math.Floor(13 * PointsPerDip * .95) / PointsPerDip;
    private readonly Canvas beamRoot;
    internal Control BeamVisual => beamRoot;
    internal BeamCanvas Beam { get; }
    internal CoordinateAxisCanvas Axis { get; }
    internal ResultDiagram[] Diagrams { get; }
    internal PrototypeTechnicalGraphics Graphics { get; private set; } = null!;
    internal int MeasurementPasses { get; private set; }

    internal ReportPrototypeVisuals(ReportPrototypeFixtures.Fixture fixture)
    {
        double width = PrototypeReport.LeftWidth / PointsPerDip;
        Beam = new()
        {
            Width = width, IsHitTestVisible = false, SupportSymbolScale = SupportSymbolScale, LabelFontSize = AnnotationFontSize,
            BeamBrush = Brush("BeamStroke"), CanvasBackgroundBrush = Brush("CanvasBackground"),
            AccentBrush = Brush("Accent"), GhostBrush = Brush("AxisStroke"), ErrorBrush = Brush("Error")
        };
        Axis = new()
        {
            Width = width, LabelFontSize = AnnotationFontSize,
            AxisBrush = Brush("AxisStroke"), IsStandalone = true, IsHitTestVisible = false
        };
        beamRoot = new() { Background = Brush("CanvasBackground"), UseLayoutRounding = false, IsHitTestVisible = false, Width = width,
            Children = { Beam, Axis } };
        Diagrams = new[] { ResultDiagramKind.TransverseDisplacement, ResultDiagramKind.ShearForce, ResultDiagramKind.BendingMoment }
            .Select(kind =>
            {
                var diagram = new ResultDiagram { UseLayoutRounding = false, Kind = kind,
                    Presentation = fixture.Presentation };
                foreach (string key in new[] { "CanvasBackground", "TextSecondary", "TextPrimary", "Border", "AxisStroke", "Accent" })
                    diagram.Resources.Add(key, Brush(key));
                return diagram;
            }).ToArray();
        Prepare(fixture, width);
    }

    private void Prepare(ReportPrototypeFixtures.Fixture fixture, double width)
    {
        var profile = fixture.Presentation.Options.Profile;
        var layout = new BeamLayoutState();
        var requirements = StationRequirementBuilder.FromDocument(fixture.Document, SupportSymbolScale, LoadSymbolScale);
        var minimum = StationOuterMargins.Calculate(requirements[0], requirements[^1], baseSideMargin: 0);
        var margins = minimum;
        for (int pass = 0; pass < 8; pass++)
        {
            var initial = layout.Update(fixture.Document, width, SchematicMetrics.MinimumBeamPaneHeight,
                outerMargins: margins, supportSymbolScale: SupportSymbolScale, loadSymbolScale: LoadSymbolScale)!;
            var request = BeamRenderState.Create(fixture.Document, initial, Measure,
                presentation: fixture.EditorPresentation, profile: profile, loadSymbolScale: LoadSymbolScale);
            var frame = layout.Update(fixture.Document, width, request.MinimumPaneHeight,
                request.RequiredBelowBeamSpace, margins, SupportSymbolScale, LoadSymbolScale)!;
            Beam.Scene = BeamRenderState.Create(fixture.Document, frame, Measure,
                presentation: fixture.EditorPresentation, profile: profile, loadSymbolScale: LoadSymbolScale);
            Beam.Height = frame.Viewport.Height;
            Axis.AxisLayout = CoordinateAxisLayout.Create(frame.Layout, width, Measure, unit: profile[QuantityKind.BeamLength]);
            Axis.Height = Axis.AxisLayout.PaneHeight;
            Canvas.SetTop(Axis, Beam.Height);
            Arrange(beamRoot, new(width, Beam.Height + Axis.Height));
            Graphics = new((float)(beamRoot.Height * PointsPerDip), Draw);

            // Use the real page composition: plot height can change the required tick-label width.
            using var report = new PrototypeReport(fixture.Data, Graphics);
            var areas = report.TechnicalAreas(0);
            for (int i = 0; i < Diagrams.Length; i++)
            {
                Diagrams[i].StationLayout = frame.Layout;
                Arrange(Diagrams[i], new(width, areas[i + 1].Bounds.Height / PointsPerDip));
            }
            double tickWidth = Diagrams.Max(d => d.Projection!.Ticks.Max(t =>
                SchematicText.Measure(d.TickLabel(t), Typeface.Default, 12).Width));
            double startWidth = Measure(Axis.AxisLayout.Labels.Single(l => l.Role == AxisEndpointRole.Start).Text).Width;
            double endWidth = Measure(Axis.AxisLayout.Labels.Single(l => l.Role == AxisEndpointRole.End).Text).Width;
            double unitWidth = Measure("x [" + Axis.AxisLayout.UnitSymbol + "]").Width;
            var next = new StationOuterMargins(
                Math.Max(margins.Left, Math.Max(minimum.Left, Math.Max(startWidth / 2 + 8, tickWidth + 16))),
                Math.Max(margins.Right, Math.Max(minimum.Right, Math.Max(endWidth / 2 + 8, unitWidth + 16))));
            MeasurementPasses = pass + 1;
            if (next == margins) return;
            margins = next;
        }
        throw new InvalidOperationException("The fixed report fixture did not reach stable graphic margins in eight passes.");
    }

    private static Size Measure(string text) => SchematicText.Measure(text, Typeface.Default, AnnotationFontSize);

    private void Draw(SKCanvas canvas, PrototypeGraphic kind, SKRect bounds)
    {
        Dispatcher.UIThread.VerifyAccess();
        Control visual = kind == PrototypeGraphic.Beam ? beamRoot : Diagrams[(int)kind - 1];
        Arrange(visual, new(bounds.Width / PointsPerDip, bounds.Height / PointsPerDip));
        var current = canvas.TotalMatrix;
        var pageTransform = new Matrix(current.ScaleX, current.SkewY, current.SkewX, current.ScaleY, current.TransX, current.TransY);
        visual.RenderTransformOrigin = RelativePoint.TopLeft;
        visual.RenderTransform = new MatrixTransform(Matrix.CreateScale(PointsPerDip, PointsPerDip)
            * Matrix.CreateTranslation(bounds.Left, bounds.Top) * pageTransform);
        // Avalonia sets the external canvas matrix. Carry the page transform through its visual root.
        using var restore = new SKAutoCanvasRestore(canvas);
        canvas.ClipRect(bounds);
        var clip = canvas.DeviceClipBounds;
        DrawingContextHelper.RenderAsync(canvas, visual, new Rect(0, 0, Math.Max(1, clip.Right), Math.Max(1, clip.Bottom)),
            new Vector(96, 96)).GetAwaiter().GetResult();
    }

    private static void Arrange(Control visual, Size size)
    {
        visual.Width = size.Width; visual.Height = size.Height;
        visual.Measure(size); visual.Arrange(new Rect(size));
    }

    private static IBrush Brush(string key) => Application.Current!.Resources.TryGetResource(key, ThemeVariant.Light, out var value)
        && value is IBrush brush ? brush : throw new InvalidOperationException("Missing report visual brush: " + key);
}
