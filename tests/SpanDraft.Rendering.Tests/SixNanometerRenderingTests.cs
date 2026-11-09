using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using SkiaSharp;
using SpanDraft.Core.Materials;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Sections.Parametric;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Reporting;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.Views;
using Xunit;

#pragma warning disable xUnit1031
namespace SpanDraft.Tests;

[Collection("Avalonia")]
public sealed class SixNanometerRenderingTests : ProjectReportTestSupport
{
    private static Guid Id(int value) => new(value, 0, 0, new byte[8]);
    private static Length M(double value) => Length.FromMeters(value);

    private static ProjectState Create(string title, double supportA, double[] forces)
    {
        var section = new RectangularHollowSectionGeometry(Length.FromMillimeters(60), Length.FromMillimeters(100),
            Length.FromMillimeters(4), Length.FromMillimeters(6));
        var material = new Material("S235JR", Pressure.FromPascals(210e9), Pressure.FromMegapascals(235), MaterialStrengthType.Re);
        EditorSupport[] supports =
        [
            new(Id(1), M(supportA), SupportType.Pinned, "A"),
            new(Id(2), M(1), SupportType.Roller, "B")
        ];
        EditorPointLoad[] loads = forces.Select((x, i) => (EditorPointLoad)new EditorPointForce(Id(100 + i), M(x),
            Force.FromNewtons(i == 0 ? -5000 : -3000), "F" + (i + 1))).ToArray();
        var document = new EditorDocument(M(1), material, section, SectionAxisDesignation.Y, supports, loads);
        return new(document, new EditorPresentationState(), new(title, "6 nm", "TEST", "Technik"));
    }

    [Theory]
    [InlineData("zwei-kraefte", 0, new[] { 0.10054, 0.100540006 })]
    [InlineData("neben-lager", 0.10054, new[] { 0.100540006, 0.5 })]
    public void ReportAndWorkspaceStayReadable(string name, double supportA, double[] forces)
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");
        var (main, _) = Open(Create(name, supportA, forces));
        var editor = main.Editor!;
        using var report = ProjectReport.Capture(editor, Now);
        AssertTextContained(report);

        string? directory = Environment.GetEnvironmentVariable("SPANDRAFT_SIX_NM_OUTPUT");
        if (directory is null)
            return;
        Directory.CreateDirectory(directory);
        for (int page = 0; page < report.Pages.Count; page++)
        {
            using var bitmap = report.RenderPage(page, 110);
            using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(Path.Combine(directory, $"{name}-bericht-{page}.png"), data.ToArray());
        }
        var view = new EditorView { DataContext = editor };
        var window = new Window { Content = view };
        window.Measure(new Size(1250, 900)); window.Arrange(new Rect(0, 0, 1250, 900));
        view.Measure(new Size(1250, 900)); view.Arrange(new Rect(0, 0, 1250, 900));
        using var image = new RenderTargetBitmap(new PixelSize(1250, 900));
        image.Render(view);
        image.Save(Path.Combine(directory, $"{name}-arbeitsbereich.png"), PngBitmapEncoderOptions.Default);
    }
}
