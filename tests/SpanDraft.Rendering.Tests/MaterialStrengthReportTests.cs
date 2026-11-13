using System.Text;
using SpanDraft.Core.Materials;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Reporting;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.ViewModels;
using SpanDraft.Reporting;
using Xunit;
using static SpanDraft.Tests.ProjectTestSupport;

namespace SpanDraft.Tests;

[Collection("Avalonia")]
public sealed class MaterialStrengthReportTests : MaterialStrengthTestSupport
{

    // Recovery completes on a worker; keep Avalonia access on its owning test thread.
#pragma warning disable xUnit1031
    [Fact]
    public void LegacyProjectRequiresConfirmationBeforeSaveAndReportButRetainsRecoveryAndHistory()
    {
        using var environment = new DesktopControlEnvironment();
        var app = new App(create: false);
        var path = TestPath("legacy-strength.spandraft");
        var original = LegacyProject(); app.Files.Data[path] = original;
        Assert.True(app.Main.OpenAsync(path).GetAwaiter().GetResult());
        Assert.False(app.Main.Session!.IsDirty);
        Assert.False(app.Main.SaveAsync().GetAwaiter().GetResult()); Assert.False(app.Main.SaveAsync(saveAs: true).GetAwaiter().GetResult());
        Assert.Equal(0, app.Dialogs.SaveQuestions); Assert.Empty(app.Files.Writes);
        Assert.False(app.Main.ReportAsync().GetAwaiter().GetResult());
        Assert.Throws<InvalidOperationException>(() => ProjectReport.Capture(app.Main.Editor!));
        Assert.All(app.Dialogs.Errors, message => Assert.Contains(Strings.MaterialStrengthResolve, message));
        app.ChangeLength(5);
        app.Delay.ReleaseAll(); app.Recovery.DrainAsync().GetAwaiter().GetResult();
        var recovered = (app.Recovery.ReadAsync().GetAwaiter().GetResult())!;
        Assert.Null(recovered.State.Document.Material.StrengthType);
        Assert.Equal(5, recovered.State.Document.Length.Meters);
        var calls = app.Analyses;
        app.Main.EditProject(); app.Main.Setup.ShowMaterials();
        var selection = Assert.IsType<MaterialSelectionViewModel>(app.Main.Setup.CurrentStep);
        Assert.Contains(Strings.MaterialStrengthUnspecified, selection.CurrentEntry!.StrengthSummary);
        selection.UseSelected();
        var editor = selection.Editor!;
        Assert.Null(editor.StrengthType); Assert.False(editor.CanConfirm);
        editor.StrengthType = editor.StrengthTypes[1];
        Assert.True(editor.ConfirmAsync().GetAwaiter().GetResult()); selection.UseSelected(); Assert.True(app.Main.Setup.ConfirmAsync().GetAwaiter().GetResult());
        Assert.Equal(calls, app.Analyses);
        Assert.True(app.Main.Session.IsDirty);
        Assert.True(app.Main.Undo()); Assert.Null(app.Main.Editor!.Document.Material.StrengthType);
        Assert.False(app.Main.SaveAsync().GetAwaiter().GetResult());
        Assert.True(app.Main.Redo()); Assert.Equal(MaterialStrengthType.Rp02, app.Main.Editor.Document.Material.StrengthType);
        Assert.Equal(calls, app.Analyses);
        Assert.True(app.Main.SaveAsync().GetAwaiter().GetResult());
        Assert.Equal(MaterialStrengthType.Rp02, ProjectFileCodec.Deserialize(app.Files.Data[path]).Document.Material.StrengthType);
        Assert.NotEqual(original, app.Files.Data[path]);
        using var report = ProjectReport.Capture(app.Main.Editor);
        Assert.Contains("Rp0,2", report.Data.Indicators[3].Label.PlainText);
    }

    [Theory]
    [InlineData(MaterialStrengthType.Re, "Re", "de-DE")]
    [InlineData(MaterialStrengthType.Rp02, "Rp0,2", "de-DE")]
    [InlineData(MaterialStrengthType.Re, "Re", "en-US")]
    [InlineData(MaterialStrengthType.Rp02, "Rp0,2", "en-US")]
    public async Task ReportUsesStoredTypeInMaterialFormulaAndRenderedPdf(MaterialStrengthType type, string symbol, string culture)
    {
        using var environment = new DesktopControlEnvironment();
        using var scope = new UiCultureScope(culture);
        var files = new Files(); var path = TestPath("typed-report.spandraft");
        files.Data[path] = ProjectFileCodec.Serialize(WithMaterial(Typed(type)));
        var main = new MainWindowViewModel(files: files);
        Assert.True(await main.OpenAsync(path));
        using var report = ProjectReport.Capture(main.Editor!);
        Assert.Contains(report.Data.MaterialProperties, p => p.PlainText.StartsWith(MaterialStrengthDisplay.Label(type) + " = ", StringComparison.Ordinal));
        Assert.Equal(Strings.ReportSafety + " S = " + symbol + " / σb,max", report.Data.Indicators[3].Label.PlainText);
        Assert.StartsWith(Strings.ReportTheory + " ", Assert.Single(report.Data.Labels.Assumptions), StringComparison.Ordinal);
        using var pdf = new MemoryStream(); report.WritePdf(pdf);
        Assert.Contains(report.TextRuns, r => r.Text.Contains(symbol, StringComparison.Ordinal));
        Assert.DoesNotContain(report.TextRuns, r => r.Text.Contains("fy", StringComparison.Ordinal));
        Assert.All(report.TextRuns, run =>
        {
            Assert.True(run.Bounds.Left >= ReportDocument.Margin - .1f, run.Text);
            Assert.True(run.Bounds.Right <= ReportDocument.PageWidth - ReportDocument.Margin + .1f, run.Text);
            Assert.True(run.Bounds.Top >= ReportDocument.Margin - .1f, run.Text);
            Assert.True(run.Bounds.Bottom <= ReportDocument.PageHeight - ReportDocument.Margin + 1, run.Text);
        });
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(pdf.ToArray(), 0, 5));
    }
}
