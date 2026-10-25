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

    private readonly SectionPreviewControl? section;
    private readonly Dictionary<ReportGraphic, SKPicture> pictures = [];

    internal ReportVisuals(ProjectState state, AnalysisPresentationState presentation, ReportData data)
    {
        var inputs = new Inputs(state.Document, presentation, state.Presentation, data);
        if (SectionDefinitionCompatibility.Normalize(state.Document.Section) is IParametricSectionDefinition parametric)
        {
            var type = SectionDefinitionViewModel.FormOf(parametric);
            section = new SectionPreviewControl { Geometry = SectionPreviewGeometryResolver.FromSection(type,
                SectionDefinitionViewModel.PresetValues(parametric), parametric), SelectedAxis = state.Document.BendingAxis,
                UnitProfile = presentation.Options.Profile, IsHitTestVisible = false, Compact = true };
            foreach (string key in new[] { "CanvasBackground", "AppBackground", "SurfacePressed", "TextSecondary", "TextPrimary", "Accent" })
                section.Resources.Add(key, Brush(key));
        }
        double width = ReportDocument.LeftWidth / PointsPerDip;
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
                    Presentation = inputs.Presentation };
                foreach (string key in new[] { "CanvasBackground", "TextSecondary", "TextPrimary", "Border", "AxisStroke", "Accent" })
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
            var initial = layout.Update(inputs.Document, width, SchematicMetrics.MinimumBeamPaneHeight,
                outerMargins: margins, supportSymbolScale: SupportSymbolScale, loadSymbolScale: LoadSymbolScale)!;
            var request = BeamRenderState.Create(inputs.Document, initial, Measure,
                presentation: inputs.EditorPresentation, profile: profile, loadSymbolScale: LoadSymbolScale);
            var frame = layout.Update(inputs.Document, width, request.MinimumPaneHeight,
                request.RequiredBelowBeamSpace, margins, SupportSymbolScale, LoadSymbolScale)!;
            Beam.Scene = BeamRenderState.Create(inputs.Document, frame, Measure,
                presentation: inputs.EditorPresentation, profile: profile, loadSymbolScale: LoadSymbolScale);
            Beam.Height = frame.Viewport.Height;
            Axis.AxisLayout = CoordinateAxisLayout.Create(frame.Layout, width, Measure, unit: profile[QuantityKind.BeamLength]);
            Axis.Height = Axis.AxisLayout.PaneHeight;
            Canvas.SetTop(Axis, Beam.Height);
            Arrange(beamRoot, new(width, Beam.Height + Axis.Height));
            Graphics = new((float)(beamRoot.Height * drawingScale), Draw);

            // Use the real page composition: plot height can change the required tick-label width.
            using var report = new ReportDocument(inputs.Data, Graphics);
            int graphicPage = report.Pages.Select((p, i) => (p, i)).First(p => p.p.HasGraphics).i;
            double maximumBeamHeight = ReportDocument.FooterTop - 14 - report.Pages[graphicPage].BodyTop - 188;
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
                SchematicText.Measure(d.TickLabel(t), Typeface.Default, 12).Width));
            double startWidth = Measure(Axis.AxisLayout.Labels.Single(l => l.Role == AxisEndpointRole.Start).Text).Width;
            double endWidth = Measure(Axis.AxisLayout.Labels.Single(l => l.Role == AxisEndpointRole.End).Text).Width;
            double unitWidth = SchematicText.Measure(TechnicalSymbols.Axis("x") + " [" + Axis.AxisLayout.UnitSymbol + "]", Typeface.Default, AnnotationFontSize).Width;
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
        foreach (var area in report.TechnicalAreas(report.Pages.Select((p, i) => (p, i)).First(p => p.p.HasGraphics).i))
        {
            using var recorder = new SKPictureRecorder();
            var bounds = new SKRect(0, 0, area.Bounds.Width, area.Bounds.Height);
            var canvas = recorder.BeginRecording(bounds);
            DrawLive(canvas, area.Kind, bounds);
            var picture = recorder.EndRecording();
            report.Own(picture);
            pictures.Add(area.Kind, picture);
        }
    }

    private static Size Measure(string text) => SchematicText.Measure(text, Typeface.Default, AnnotationFontSize);

    private void Draw(SKCanvas canvas, ReportGraphic kind, SKRect bounds)
    {
        if (!pictures.TryGetValue(kind, out var picture)) { DrawLive(canvas, kind, bounds); return; }
        using var restore = new SKAutoCanvasRestore(canvas);
        canvas.Translate(bounds.Left, bounds.Top);
        canvas.DrawPicture(picture);
    }

    private void DrawLive(SKCanvas canvas, ReportGraphic kind, SKRect bounds)
    {
        Dispatcher.UIThread.VerifyAccess();
        Control visual = kind == ReportGraphic.Section
            ? section ?? (Control)new TextBlock { Text = Strings.ReportManualSection, TextWrapping = TextWrapping.Wrap,
                Foreground = Brush("TextSecondary"), FontSize = 12 }
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
