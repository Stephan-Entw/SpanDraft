using System.Globalization;
using System.Reflection;
using Avalonia.Platform;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Skia;
using SpanDraft.Analysis;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Sections.Parametric;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Desktop;
using SpanDraft.Desktop.Controls;
using SpanDraft.Desktop.Layout;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.Sections;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length != 1) throw new ArgumentException("Usage: SpanDraft.HelpAssets <output-directory>");
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");
            // Offscreen Skia initialization mirrors the existing rendering tests and needs no native render timer.
            AppBuilder.Configure<App>().UseWindowingSubsystem(InitializeCursors, "Help assets")
                .UseStandardRuntimePlatformSubsystem().UseSkia().UseHarfBuzz().SetupWithoutStarting();
            Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
            string output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
            Save(Example(withLoad: false), "beispiel-lagerung"); Save(Example(withLoad: true), "beispiel-belastung");
            Save(Support(SupportType.Fixed), "lager-einspannung");
            Save(Support(SupportType.Pinned), "lager-festlager"); Save(Support(SupportType.Roller), "lager-loslager");
            Save(LoadSigns(PointLoadKind.Force), "punktkraft-vorzeichen");
            Save(LoadSigns(PointLoadKind.Moment), "punktmoment-vorzeichen"); Save(LoadSigns(pointKind: null), "streckenlast-vorzeichen");
            Save(Coordinates(), "koordinaten");
            Save(Section(), "querschnitt-achsen");
            foreach (var (name, diagram) in Results()) Save(diagram, name);
            Console.WriteLine($"Generated thirteen SpanDraft help PNGs in {output}");
            return 0;
            void Save(Control control, string name)
            {
                var size = new Size(control.Width, control.Height);
                control.Measure(size); control.Arrange(new Rect(size));
                using var bitmap = new RenderTargetBitmap(new PixelSize((int)size.Width * 2, (int)size.Height * 2), new Vector(192, 192));
                bitmap.Render(control);
                bitmap.Save(Path.Combine(output, name + ".png"), PngBitmapEncoderOptions.Default);
            }
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    private static void InitializeCursors()
    {
        // App styles resolve standard cursors even though the exported controls have no input surface.
        var locator = typeof(AvaloniaLocator).GetProperty("CurrentMutable")!.GetValue(null)!;
        var registration = locator.GetType().GetMethods().Single(m => m.Name == "Bind" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0)
            .MakeGenericMethod(typeof(ICursorFactory)).Invoke(locator, null)!;
        registration.GetType().GetMethod("ToConstant")!.MakeGenericMethod(typeof(ICursorFactory))
            .Invoke(registration, [DispatchProxy.Create<ICursorFactory, CursorFactory>()]);
    }
    public class CursorFactory : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args) => DispatchProxy.Create<ICursorImpl, CursorHandle>();
    }
    public class CursorHandle : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args) => null;
    }
    private static IBrush AppBrush(string key)
    {
        if (Application.Current!.Resources.TryGetResource(key, ThemeVariant.Light, out var value) && value is IBrush brush)
            return brush;
        throw new InvalidOperationException($"Missing App drawing brush: {key}");
    }
    private static Canvas Page(double width, double height)
    {
        var page = new Canvas { Width = width, Height = height, Background = AppBrush("CanvasBackground") };
        // Detached controls have no TopLevel resource host. Supply the App's actual light-theme brushes.
        foreach (string key in new[] { "CanvasBackground", "TextSecondary", "TextPrimary", "BeamStroke", "Border", "AxisStroke", "Accent", "Error", "SurfacePressed", "AppBackground" })
            page.Resources.Add(key, AppBrush(key));
        return page;
    }
    private static void Add(Canvas page, Control control, double x, double y, double width, double height)
    {
        control.Width = width; control.Height = height; Canvas.SetLeft(control, x); Canvas.SetTop(control, y); page.Children.Add(control);
    }
    private static void Text(Canvas page, string text, double x, double y, double width = 200, double size = 17) =>
        Add(page, new TextBlock { Text = text, FontSize = size, Foreground = AppBrush("TextPrimary"), TextAlignment = TextAlignment.Center }, x, y, width, 52);
    private static EditorDocument Document() => new(Length.FromMeters(2), MaterialCatalogCodec.LoadBuiltIn().Find("S235JR")!.Material,
        new RectangleSectionGeometry(Length.FromMillimeters(40), Length.FromMillimeters(80)), SectionAxisDesignation.Y);
    private static BeamCanvas Beam(EditorDocument document, double width, double height, double beamY,
        double leftMargin = 60, double rightMargin = 60)
    {
        var frame = new BeamLayoutFrame(StationLayout.Compute(document.Length.Meters, leftMargin, width - rightMargin, []), new(width, height, beamY));
        var scene = BeamRenderState.Create(document, frame, text => SchematicText.Measure(text, Typeface.Default, 13));
        return new() { Scene = scene with { Annotations = [] }, BeamBrush = AppBrush("BeamStroke"), CanvasBackgroundBrush = AppBrush("CanvasBackground"),
            AccentBrush = AppBrush("Accent"), GhostBrush = AppBrush("AxisStroke"), ErrorBrush = AppBrush("Error") };
    }
    private static Canvas Example(bool withLoad)
    {
        var page = Page(880, 208);
        var document = Document().WithSupports([
            new(Guid.NewGuid(), Length.FromMeters(0), SupportType.Pinned, "A"),
            new(Guid.NewGuid(), Length.FromMeters(2), SupportType.Roller, "B")]);
        double middle = document.Length.Meters / 2;
        if (withLoad)
            document = document.WithLoads([new EditorPointForce(Guid.NewGuid(), Length.FromMeters(middle), Force.FromNewtons(-1000), "F")]);
        var beam = Beam(document, 760, 176, 86);
        Add(page, beam, 60, 16, 760, 176);
        double X(double position) => 60 + beam.Scene!.Frame.Layout.Transform.PhysicalToScreen(position);
        Text(page, "Festlager", X(0) - 90, 145, 180, 18);
        Text(page, "Loslager", X(document.Length.Meters) - 90, 145, 180, 18);
        if (withLoad)
        {
            Text(page, "F", X(middle) - 50, 24, 100);
            Text(page, "L/2", X(middle) - 50, 145, 100);
        }
        return page;
    }
    private static Canvas Support(SupportType type)
    {
        var page = Page(96, 76);
        var doc = Document().WithSupports([new(Guid.NewGuid(), Length.FromMeters(type == SupportType.Fixed ? 0 : 1), type, "A")]);
        Add(page, Beam(doc, 96, 76, 30, type == SupportType.Fixed ? 48 : 12, 12), 0, 0, 96, 76);
        return page;
    }
    // A null point kind selects the product's distributed-load rendering.
    private static Canvas LoadSigns(PointLoadKind? pointKind)
    {
        var page = Page(640, 112);
        for (int i = 0; i < 2; i++)
        {
            double sign = i == 0 ? 1 : -1;
            var doc = Document();
            if (pointKind == PointLoadKind.Force)
                doc = doc.WithLoads([new EditorPointForce(Guid.NewGuid(), Length.FromMeters(1), Force.FromNewtons(sign), "F")]);
            else if (pointKind == PointLoadKind.Moment)
                doc = doc.WithLoads([new EditorPointMoment(Guid.NewGuid(), Length.FromMeters(1), Moment.FromNewtonMeters(sign), "M")]);
            else
                doc = doc.WithDistributedLoads([new(Guid.NewGuid(), Length.FromMeters(.5), Length.FromMeters(1.5), ForcePerLength.FromNewtonsPerMeter(sign), "q")]);
            Add(page, Beam(doc, 280, 100, 62, 24, 24), 20 + i * 320, 6, 280, 100);
        }
        return page;
    }
    private static Canvas Coordinates()
    {
        var page = Page(880, 300);
        Add(page, Beam(Document(), 760, 160, 105), 60, 90, 760, 160);
        Text(page, "x = 0", 70, 217, 100); Text(page, "x = L", 710, 217, 100);
        Add(page, new AxialArrow(), 260, 210, 180, 25); Text(page, "+x / +u", 290, 245, 120);
        Add(page, new Glyph(PointLoadKind.Force, 1, "AxisStroke"), 495, 75, 50, 56); Text(page, "+w", 465, 30, 110);
        Add(page, new Glyph(PointLoadKind.Moment, 1, "AxisStroke"), 615, 100, 60, 60); Text(page, "+θ", 590, 55, 110);
        return page;
    }
    private static Canvas Section()
    {
        var page = Page(650, 500);
        var rectangle = new RectangleSectionGeometry(Length.FromMillimeters(60), Length.FromMillimeters(100));
        var geometry = SectionPreviewGeometryResolver.FromSection(SectionEditorType.Rectangle,
            new Dictionary<string, double> { ["b"] = .06, ["h"] = .1 }, rectangle);
        geometry = geometry with { Dimensions = geometry.Dimensions.ToDictionary(p => p.Key, p => p.Value with { IsConfirmed = false }) };
        Add(page, new SectionPreviewControl { Geometry = geometry, SelectedAxis = null }, 40, 20, 570, 460);
        return page;
    }
    private static IEnumerable<(string Name, Control Diagram)> Results()
    {
        var doc = Document().WithSupports([
            new(Guid.NewGuid(), Length.FromMeters(0), SupportType.Pinned, "A"),
            new(Guid.NewGuid(), Length.FromMeters(2), SupportType.Roller, "B")])
            .WithLoads([new EditorPointForce(Guid.NewGuid(), Length.FromMeters(1), Force.FromNewtons(-1000), "F")]);
        var presentation = AnalysisPresentationState.FromOutcome(BeamAnalysis.Analyze(doc.ToBeamModel()));
        if (!presentation.IsSuccess) throw new InvalidOperationException("The help reference model did not solve.");
        var layout = new BeamLayoutState().Update(doc, 720, 240)!.Layout;
        (ResultDiagramKind Kind, string Name)[] diagrams =
        [
            (ResultDiagramKind.TransverseDisplacement, "diagramm-durchbiegung"),
            (ResultDiagramKind.ShearForce, "diagramm-querkraft"),
            (ResultDiagramKind.BendingMoment, "diagramm-biegemoment")
        ];
        foreach (var (kind, name) in diagrams)
        {
            var page = Page(760, 254);
            Add(page, new ResultDiagram { Kind = kind, Presentation = presentation, StationLayout = layout }, 20, 12, 720, 230);
            yield return (name, page);
        }
    }
    // Technical illustrations reuse product glyphs; only the axial annotation has its own arrow.
    private sealed class Glyph(PointLoadKind kind, double value, string brushKey = "BeamStroke") : Control
    {
        public override void Render(DrawingContext context)
        {
            double y = kind == PointLoadKind.Force ? Bounds.Height : Bounds.Height / 2;
            var visual = new PointLoadVisual(null, new(Length.FromMeters(0), kind, value), "", Bounds.Width / 2, y, false);
            PointLoadSymbol.Draw(context, new(kind, visual.X, visual.Y, [visual]), AppBrush(brushKey));
        }
    }
    private sealed class AxialArrow : Control
    {
        public override void Render(DrawingContext context)
        {
            var pen = new Pen(AppBrush("AxisStroke"), SchematicMetrics.SymbolStrokeWidth);
            double y = Bounds.Height / 2, end = Bounds.Width - 2;
            context.DrawLine(pen, new(2, y), new(end, y));
            context.DrawLine(pen, new(end - 8, y - 4), new(end, y));
            context.DrawLine(pen, new(end - 8, y + 4), new(end, y));
        }
    }
}
