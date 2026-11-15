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
using SpanDraft.Reporting;
using SpanDraft.Desktop.Sections;
using SpanDraft.Desktop.ViewModels;
using SpanDraft.Desktop.Resources;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Sections.Parametric;
using SpanDraft.Reporting.Notation;

namespace SpanDraft.Desktop.Reporting;

/// <summary>Measures product controls once and freezes their vector drawings for report output.</summary>
internal sealed class ReportVisuals
{
    internal const double VisualScale = .8;
    internal const double MinimumBeamPaneHeight = 100;
    // The room a result diagram keeps above (title) and below its plot, in device-independent pixels; see ResultDiagram.
    private const double DiagramChrome = 48;
    private const double SupportSymbolScale = .75;
    private const double LoadSymbolScale = .85;
    internal const double PointsPerDip = 72d / 96 * VisualScale;
    private double drawingScale = PointsPerDip;
    private static readonly double AnnotationFontSize = Math.Floor(13 * PointsPerDip * .95) / PointsPerDip;
    private readonly Canvas beamRoot;
    internal Control BeamVisual => beamRoot;
    internal BeamCanvas Beam { get; }
    internal CoordinateAxisCanvas Axis { get; }
    internal ResultDiagram[] Diagrams { get; }
    internal ReportTechnicalGraphics Graphics { get; private set; } = null!;
    internal int MeasurementPasses { get; private set; }

    private readonly SectionPreviewControl?[] sections;
    private readonly Dictionary<(ReportGraphic, int), SKPicture> pictures = [];
    internal SegmentRowPane SegmentRow { get; }

    internal ReportVisuals(ProjectState state, AnalysisPresentationState presentation, ReportData data)
    {
        var inputs = new Inputs(state.Document, presentation, state.Presentation, data);
        // One section sketch per segment; the governing segment has no special place.
        sections = state.Document.Segments.Select(segment =>
        {
            if (SectionDefinitionCompatibility.Normalize(segment.Section) is not IParametricSectionDefinition parametric) return null;
            var type = SectionDefinitionViewModel.FormOf(parametric);
            var control = new SectionPreviewControl { Geometry = SectionPreviewGeometryResolver.FromSection(type,
                SectionDefinitionViewModel.PresetValues(parametric), parametric), SelectedAxis = segment.BendingAxis,
                Presentation = presentation.Options, IsHitTestVisible = false, Compact = true };
            foreach (string key in new[] { "CanvasBackground", "AppBackground", "SurfacePressed", "TextSecondary", "TextPrimary", "Accent" })
                control.Resources.Add(key, Brush(key));
            return control;
        }).ToArray();
        double width = ReportDocument.LeftWidth / PointsPerDip;
        Beam = new()
        {
            Width = width, IsHitTestVisible = false, SupportSymbolScale = SupportSymbolScale, LabelFontSize = AnnotationFontSize,
            BeamBrush = Brush("BeamStroke"), CanvasBackgroundBrush = Brush("CanvasBackground"),
            SelfWeightBrush = Brush("SelfWeightStroke"),
            AccentBrush = Brush("Accent"), GhostBrush = Brush("AxisStroke"), ErrorBrush = Brush("Error")
        };
        Axis = new()
        {
            Width = width, LabelFontSize = AnnotationFontSize,
            AxisBrush = Brush("AxisStroke"), IsStandalone = true, IsHitTestVisible = false
        };
        // The segment row of the editor sits between the beam and the axis, only for several segments.
        SegmentRow = new() { Width = width, IsHitTestVisible = false, StrokeBrush = Brush("AxisStroke"), LabelFontSize = AnnotationFontSize };
        beamRoot = new() { Background = Brush("CanvasBackground"), UseLayoutRounding = false, IsHitTestVisible = false, Width = width,
            Children = { Beam, SegmentRow, Axis } };
        Diagrams = new[] { ResultDiagramKind.TransverseDisplacement, ResultDiagramKind.ShearForce, ResultDiagramKind.BendingMoment }
            .Select(kind =>
            {
                var diagram = new ResultDiagram { UseLayoutRounding = false, Kind = kind, MarkerFontSize = 11,
                    Presentation = inputs.Presentation };
                foreach (string key in new[] { "CanvasBackground", "TextSecondary", "TextPrimary", "Border", "AxisStroke", "DiagramStroke", "DiagramHatch", "DiagramExtremum" })
                    diagram.Resources.Add(key, Brush(key));
                return diagram;
            }).ToArray();
        Prepare(inputs, width);
    }

    private void Prepare(Inputs inputs, double width)
    {
        var profile = inputs.Presentation.Options.Profile;
        var layout = new BeamLayoutState();
        var requirements = StationRequirementBuilder.FromDocument(inputs.Document, SupportSymbolScale, LoadSymbolScale);
        var minimum = StationOuterMargins.Calculate(requirements[0], requirements[^1], baseSideMargin: 0);
        var margins = minimum;
        for (int pass = 0; pass < 16; pass++)
        {
            var initial = layout.Update(inputs.Document, width, MinimumBeamPaneHeight,
                outerMargins: margins, supportSymbolScale: SupportSymbolScale, loadSymbolScale: LoadSymbolScale)!;
            var request = BeamRenderState.Create(inputs.Document, initial, Measure,
                presentation: inputs.EditorPresentation, profile: profile, loadSymbolScale: LoadSymbolScale,
                reserveLoadPreviewRow: false, minimumPaneHeight: MinimumBeamPaneHeight, options: inputs.Presentation.Options);
            var frame = layout.Update(inputs.Document, width, request.MinimumPaneHeight,
                request.RequiredBelowBeamSpace, margins, SupportSymbolScale, LoadSymbolScale)!;
            Beam.Scene = BeamRenderState.Create(inputs.Document, frame, Measure,
                presentation: inputs.EditorPresentation, profile: profile, loadSymbolScale: LoadSymbolScale,
                reserveLoadPreviewRow: false, minimumPaneHeight: MinimumBeamPaneHeight, options: inputs.Presentation.Options);
            Beam.Height = frame.Viewport.Height;
            Axis.AxisLayout = CoordinateAxisLayout.Create(frame.Layout, width, Measure, unit: profile[QuantityKind.BeamLength],
                presentation: inputs.Presentation.Options);
            Axis.Height = Axis.AxisLayout.PaneHeight;
            double rowHeight = 0;
            SegmentRow.Width = width;
            if (inputs.Document.HasSeveralSegments)
            {
                SegmentRow.Update(frame.Layout, inputs.Document, inputs.Presentation.Options);
                Arrange(SegmentRow, new(width, SchematicMetrics.SegmentRowHeight));
                Canvas.SetTop(SegmentRow, Beam.Height);
                rowHeight = SchematicMetrics.SegmentRowHeight;
            }
            else SegmentRow.Hide();
            Canvas.SetTop(Axis, Beam.Height + rowHeight);
            Arrange(beamRoot, new(width, Beam.Height + rowHeight + Axis.Height));
            // A result diagram at its normal compact height has a plot of 25 mm; the chrome around the plot scales with the drawing.
            Graphics = new((float)(beamRoot.Height * drawingScale), (float)(25 * ReportDocument.Mm + DiagramChrome * drawingScale), Draw);

            // Use the real page composition: plot height can change the required tick-label width.
            using var report = new ReportDocument(inputs.Data, Graphics);
            int graphicPage = report.Pages.Select((p, i) => (p, i)).First(p => p.p.HasGraphics).i;
            double maximumBeamHeight = ReportDocument.FooterTop - 14 - report.Pages[graphicPage].BodyTop - 8
                - 3 * Graphics.MinimumDiagramHeight;
            if (Graphics.BeamHeight > maximumBeamHeight)
            {
                // Dense models still use the same controls and shared stations; fit them uniformly to paper.
                drawingScale *= Math.Min(.9, maximumBeamHeight / Graphics.BeamHeight);
                if (!double.IsFinite(drawingScale) || drawingScale <= 0)
                    throw new InvalidOperationException("The technical drawing is too large for the report.");
                width = ReportDocument.LeftWidth / drawingScale;
                Beam.Width = Axis.Width = beamRoot.Width = width;
                margins = minimum;
                continue;
            }
            var areas = report.TechnicalAreas(graphicPage);
            for (int i = 0; i < Diagrams.Length; i++)
            {
                Diagrams[i].StationLayout = frame.Layout;
                Arrange(Diagrams[i], new(width, areas[i + 1].Bounds.Height / drawingScale));
            }
            double tickWidth = Diagrams.Max(d => d.Projection!.Ticks.Max(t =>
                SchematicText.Measure(d.TickLabel(t), AppTypography.Typeface, AppTypography.Size("SecondarySize")).Width));
            double startWidth = Measure(Axis.AxisLayout.Labels.Single(l => l.Role == AxisEndpointRole.Start).Text).Width;
            double endWidth = Measure(Axis.AxisLayout.Labels.Single(l => l.Role == AxisEndpointRole.End).Text).Width;
            double unitWidth = SchematicText.Measure(TechnicalSymbols.Axis("x") + " [" + Axis.AxisLayout.UnitSymbol + "]", AppTypography.Typeface, AnnotationFontSize).Width;
            var next = new StationOuterMargins(
                Math.Max(margins.Left, Math.Max(minimum.Left, Math.Max(startWidth / 2 + 8, tickWidth + 16))),
                Math.Max(margins.Right, Math.Max(minimum.Right, Math.Max(endWidth / 2 + 8, unitWidth + 16))));
            MeasurementPasses = pass + 1;
            if (next == margins) return;
            margins = next;
        }
        throw new InvalidOperationException("The report did not reach stable graphic margins in sixteen passes.");
    }

    private sealed record Inputs(EditorDocument Document, AnalysisPresentationState Presentation,
        EditorPresentationState EditorPresentation, ReportData Data);

    internal void Freeze(ReportDocument report)
    {
        int graphicPage = report.Pages.Select((p, i) => (p, i)).First(p => p.p.HasGraphics).i;
        foreach (var area in report.TechnicalAreas(graphicPage)) Record(report, area.Kind, 0, area.Bounds);
        // Every segment's sketch has the same size; each is frozen with its own geometry and bending axis.
        foreach (var sketch in report.SketchAreas()) Record(report, ReportGraphic.Section, sketch.Segment, sketch.Bounds);
    }

    private void Record(ReportDocument report, ReportGraphic kind, int index, SKRect area)
    {
        using var recorder = new SKPictureRecorder();
        var bounds = new SKRect(0, 0, area.Width, area.Height);
        var canvas = recorder.BeginRecording(bounds);
        DrawLive(canvas, kind, index, bounds);
        var picture = recorder.EndRecording();
        report.Own(picture);
        pictures.Add((kind, index), picture);
    }

    private static Size Measure(string text) => SchematicText.Measure(text, AppTypography.Typeface, AnnotationFontSize);

    private void Draw(SKCanvas canvas, ReportGraphic kind, int index, SKRect bounds)
    {
        if (!pictures.TryGetValue((kind, index), out var picture)) { DrawLive(canvas, kind, index, bounds); return; }
        using var restore = new SKAutoCanvasRestore(canvas);
        canvas.Translate(bounds.Left, bounds.Top);
        canvas.DrawPicture(picture);
    }

    private void DrawLive(SKCanvas canvas, ReportGraphic kind, int index, SKRect bounds)
    {
        Dispatcher.UIThread.VerifyAccess();
        Control visual = kind == ReportGraphic.Section
            ? sections[index] ?? (Control)new TextBlock { Text = Strings.ReportManualSection, TextWrapping = TextWrapping.Wrap,
                Foreground = Brush("TextSecondary"), FontFamily = AppTypography.Family, FontSize = AppTypography.Size("SecondarySize") }
            : kind == ReportGraphic.Beam ? beamRoot : Diagrams[(int)kind - 1];
        double scale = kind == ReportGraphic.Section ? PointsPerDip : drawingScale;
        Arrange(visual, new(bounds.Width / scale, bounds.Height / scale));
        var current = canvas.TotalMatrix;
        var pageTransform = new Matrix(current.ScaleX, current.SkewY, current.SkewX, current.ScaleY, current.TransX, current.TransY);
        visual.RenderTransformOrigin = RelativePoint.TopLeft;
        visual.RenderTransform = new MatrixTransform(Matrix.CreateScale(scale, scale)
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
