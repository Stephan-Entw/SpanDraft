using System.Globalization;
using System.Reflection;
using Avalonia.Platform;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using SkiaSharp;
using SpanDraft.Desktop.Resources;
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
            if (args.Length != 2 || args[0] is not ("--generate" or "--check"))
                throw new ArgumentException("Usage: SpanDraft.HelpAssets <--generate|--check> <asset-root>");
            string output = Path.GetFullPath(args[1]);
            if (args[0] == "--check")
            {
                ValidateAssets(output);
                CheckExamples(output);
                return 0;
            }
            // Offscreen Skia initialization mirrors the existing rendering tests and needs no native render timer.
            AppBuilder.Configure<App>().UseWindowingSubsystem(InitializeCursors, "Help assets")
                .UseStandardRuntimePlatformSubsystem().UseSkia().UseHarfBuzz().SetupWithoutStarting();
            Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            Save(Support(SupportType.Fixed), "shared/lager-einspannung");
            Save(Support(SupportType.Pinned), "shared/lager-festlager"); Save(Support(SupportType.Roller), "shared/lager-loslager");
            Save(Support(SupportType.Spring, new SpringData(TranslationalStiffness.FromNewtonsPerMeter(1e6), null)), "shared/lager-wegfeder");
            Save(Support(SupportType.Spring, new SpringData(null, RotationalStiffness.FromNewtonMetersPerRadian(1e5))), "shared/lager-drehfeder");
            Save(LoadSigns(PointLoadKind.Force), "shared/punktkraft-vorzeichen");
            Save(LoadSigns(PointLoadKind.Moment), "shared/punktmoment-vorzeichen"); Save(LoadSigns(pointKind: null), "shared/streckenlast-vorzeichen");
            Save(Coordinates(), "shared/koordinaten"); Save(Section(), "shared/querschnitt-achsen");
            foreach (string language in new[] { "de", "en" })
            {
                CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language == "de" ? "de-DE" : "en-US");
                Save(Example(withLoad: false), language + "/beispiel-lagerung");
                Save(Example(withLoad: true), language + "/beispiel-belastung");
                foreach (var (name, diagram) in Results()) Save(diagram, language + "/" + name);
                Save(CantileverExample(), language + "/beispiel-kragarm");
                Save(ShaftExample(), language + "/beispiel-welle");
            }
            HelpExamples.Generate(output);
            ValidateAssets(output);
            CheckExamples(output);
            Console.WriteLine($"Generated {PngCount} transparent SpanDraft help PNGs and {HelpExamples.All.Count * HelpExamples.Languages.Count} example projects in {output}");
            return 0;
            void Save(Control control, string name)
            {
                var size = new Size(control.Width, control.Height);
                // Explicit layout scaling keeps geometric clips aligned without a hidden bitmap DPI transform.
                var scaled = new Viewbox { Width = size.Width * 2, Height = size.Height * 2, Child = control };
                var exportSize = new Size(scaled.Width, scaled.Height);
                scaled.Measure(exportSize); scaled.Arrange(new Rect(exportSize));
                using var bitmap = new RenderTargetBitmap(new PixelSize((int)exportSize.Width, (int)exportSize.Height));
                bitmap.Render(scaled);
                string path = Path.Combine(output, name + ".png");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                bitmap.Save(path, PngBitmapEncoderOptions.Default);
            }
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    private const int PngCount = 24;

    private static void CheckExamples(string output)
    {
        var problems = HelpExamples.Check(output);
        if (problems.Count > 0) throw new InvalidOperationException(string.Join(Environment.NewLine, problems));
        Console.WriteLine("Validated the example projects against their definitions.");
    }
    private static void ValidateAssets(string output)
    {
        var files = Directory.GetFiles(output, "*.png", SearchOption.AllDirectories);
        if (files.Length != PngCount) throw new InvalidOperationException($"Expected {PngCount} help PNGs, found {files.Length}.");
        foreach (string file in files)
        {
            using var bitmap = SKBitmap.Decode(file) ?? throw new InvalidOperationException($"Invalid PNG: {file}");
            if (bitmap.AlphaType == SKAlphaType.Opaque
                || new[] { bitmap.GetPixel(0, 0), bitmap.GetPixel(bitmap.Width - 1, 0),
                    bitmap.GetPixel(0, bitmap.Height - 1), bitmap.GetPixel(bitmap.Width - 1, bitmap.Height - 1) }.Any(p => p.Alpha != 0)
                || !bitmap.Pixels.Any(p => p.Alpha != 0))
                throw new InvalidOperationException($"Missing transparent exterior or visible drawing: {file}");
        }
        foreach (string file in Directory.GetFiles(Path.Combine(output, "de"), "*.png"))
            if (File.ReadAllBytes(file).SequenceEqual(File.ReadAllBytes(Path.Combine(output, "en", Path.GetFileName(file)))))
                throw new InvalidOperationException($"Localized PNGs are identical: {file}");
        Console.WriteLine($"Validated {PngCount} visible PNGs with transparent exteriors and distinct DE/EN assets.");
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
        var page = new Canvas { Width = width, Height = height, Background = Brushes.Transparent };
        // Detached controls have no TopLevel resource host. Supply the App's actual light-theme brushes.
        foreach (string key in new[] { "CanvasBackground", "TextSecondary", "TextPrimary", "BeamStroke", "Border", "AxisStroke", "Accent", "DiagramStroke", "DiagramHatch", "DiagramExtremum", "Error", "SurfacePressed", "AppBackground" })
            page.Resources.Add(key, key is "CanvasBackground" or "AppBackground" ? Brushes.Transparent : AppBrush(key));
        return page;
    }
    private static void Add(Canvas page, Control control, double x, double y, double width, double height)
    {
        control.Width = width; control.Height = height; Canvas.SetLeft(control, x); Canvas.SetTop(control, y); page.Children.Add(control);
    }
    private static void Text(Canvas page, string text, double x, double y, double width = 200, double size = 17) =>
        Add(page, new TextBlock { Text = text, FontSize = size, Foreground = AppBrush("TextPrimary"), TextAlignment = TextAlignment.Center }, x, y, width, 52);
    private static EditorDocument Document() => new(Length.FromMeters(2), MaterialCatalogCodec.LoadBuiltIn().Find("S235JR")!,
        new RectangleSectionGeometry(Length.FromMillimeters(40), Length.FromMillimeters(80)), SectionAxisDesignation.Y);
    private static BeamCanvas Beam(EditorDocument document, double width, double height, double beamY,
        double leftMargin = 60, double rightMargin = 60)
    {
        var frame = new BeamLayoutFrame(StationLayout.Compute(document.Length.Meters, leftMargin, width - rightMargin, []), new(width, height, beamY));
        var scene = BeamRenderState.Create(document, frame, text => SchematicText.Measure(text, Typeface.Default, 13));
        return new() { Scene = scene with { Annotations = [] }, BeamBrush = AppBrush("BeamStroke"), CanvasBackgroundBrush = Brushes.Transparent,
            AccentBrush = AppBrush("Accent"), GhostBrush = AppBrush("AxisStroke"), ErrorBrush = AppBrush("Error") };
    }
    private static Canvas Example(bool withLoad)
    {
        var page = Page(880, 208);
        var document = HelpExamples.SimplySupportedBeam();
        double middle = document.Length.Meters / 2;
        if (!withLoad) document = document.WithLoads([]);
        var beam = Beam(document, 760, 176, 86);
        Add(page, beam, 60, 16, 760, 176);
        double X(double position) => 60 + beam.Scene!.Frame.Layout.Transform.PhysicalToScreen(position);
        Text(page, Strings.PinnedSupport, X(0) - 90, 145, 180, 18);
        Text(page, Strings.RollerSupport, X(document.Length.Meters) - 90, 145, 180, 18);
        if (withLoad)
        {
            Text(page, "F", X(middle) - 50, 24, 100);
            Text(page, "L/2", X(middle) - 50, 145, 100);
        }
        return page;
    }
    /// <summary>The cantilever example: fixed support, distributed load q1 and self-weight g over the whole length.</summary>
    private static Canvas CantileverExample()
    {
        var page = Page(880, 208);
        var document = HelpExamples.Cantilever();
        var beam = Beam(document, 760, 176, 96, leftMargin: 80);
        Add(page, beam, 60, 16, 760, 176);
        double X(double position) => 60 + beam.Scene!.Frame.Layout.Transform.PhysicalToScreen(position);
        Text(page, Strings.FixedSupport, X(0) - 90, 155, 180, 18);
        Text(page, "q1 + g", X(document.Length.Meters / 2) - 60, 46, 120);
        Text(page, "L = " + document.Length.Meters.ToString("0.0", CultureInfo.CurrentCulture) + " m", X(document.Length.Meters / 2) - 80, 155, 160, 18);
        return page;
    }
    /// <summary>The stepped shaft example: three segments (Ø30, Ø40, Ø30), two bearings and F1 at x = 0.15 m.</summary>
    private static Canvas ShaftExample()
    {
        var page = Page(880, 208);
        var document = HelpExamples.SteppedShaft();
        var beam = Beam(document, 760, 176, 96);
        Add(page, beam, 60, 16, 760, 176);
        double X(double position) => 60 + beam.Scene!.Frame.Layout.Transform.PhysicalToScreen(position);
        Text(page, Strings.PinnedSupport, X(0) - 90, 155, 180, 18);
        Text(page, Strings.RollerSupport, X(document.Length.Meters) - 90, 155, 180, 18);
        Text(page, "F1", X(0.15) - 50, 40, 100);
        foreach (var (start, end, diameter) in new[] { (0.0, 0.1, "Ø30"), (0.1, 0.3, "Ø40"), (0.3, 0.4, "Ø30") })
            Text(page, diameter, X((start + end) / 2) - 50, 118, 100, 15);
        return page;
    }
    private static Canvas Support(SupportType type, SpringData? spring = null)
    {
        var page = Page(96, 76);
        var doc = Document().WithSupports([new(Guid.NewGuid(), Length.FromMeters(type == SupportType.Fixed ? 0 : 1), type, "A", spring)]);
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
        var doc = HelpExamples.SimplySupportedBeam();
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
            Add(page, new ResultDiagram { Kind = kind, Presentation = presentation, StationLayout = layout, BackgroundBrush = Brushes.Transparent }, 20, 12, 720, 230);
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
