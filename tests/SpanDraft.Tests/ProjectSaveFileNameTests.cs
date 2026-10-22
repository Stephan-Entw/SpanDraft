using SpanDraft.Desktop;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.ViewModels;
using Xunit;
using static SpanDraft.Tests.ProjectTestSupport;

namespace SpanDraft.Tests;

public sealed class ProjectSaveFileNameTests
{
    [Theory]
    [InlineData("Träger oben", "Träger oben.spandraft")]
    [InlineData("Äußere Stütze 日本語 🏗️", "Äußere Stütze 日本語 🏗️.spandraft")]
    [InlineData("Träger / oben: Variante 2", "Träger - oben - Variante 2.spandraft")]
    [InlineData("Beam <> : / \\ | ? * upper", "Beam - upper.spandraft")]
    [InlineData("Beam - / : - upper", "Beam - upper.spandraft")]
    [InlineData("Beam -- upper", "Beam -- upper.spandraft")]
    [InlineData("Beam\0\u001f upper", "Beam - upper.spandraft")]
    [InlineData("  Beam. .  ", "Beam.spandraft")]
    [InlineData(" Beam.SPANDRAFT ", "Beam.spandraft")]
    [InlineData("CON", "_CON.spandraft")]
    [InlineData("prn", "_prn.spandraft")]
    [InlineData("AUX.txt", "_AUX.txt.spandraft")]
    [InlineData("NUL", "_NUL.spandraft")]
    [InlineData("CONIN$", "_CONIN$.spandraft")]
    [InlineData("CONOUT$", "_CONOUT$.spandraft")]
    [InlineData("COM1", "_COM1.spandraft")]
    [InlineData("COM9.part", "_COM9.part.spandraft")]
    [InlineData("LPT1", "_LPT1.spandraft")]
    [InlineData("LPT9", "_LPT9.spandraft")]
    [InlineData("COM¹", "_COM¹.spandraft")]
    [InlineData("LPT³", "_LPT³.spandraft")]
    [InlineData("COM10", "COM10.spandraft")]
    [InlineData("CONSOLE", "CONSOLE.spandraft")]
    [InlineData(null, "Untitled.spandraft")]
    [InlineData("", "Untitled.spandraft")]
    [InlineData(" / : * ? ", "Untitled.spandraft")]
    [InlineData(" . . ", "Untitled.spandraft")]
    public void SuggestionsAreSafeWithoutChangingOrdinaryText(string? title, string expected) =>
        Assert.Equal(expected, ProjectSaveFileName.Suggest(null, title, "Untitled"));

    [Theory]
    [InlineData("de-DE", "Unbenannt.spandraft")]
    [InlineData("en-US", "Untitled.spandraft")]
    public void UnusableTitleUsesTheLocalizedFallback(string culture, string expected)
    {
        using var scope = new UiCultureScope(culture);
        Assert.Equal(expected, ProjectSaveFileName.Suggest(null, "???", Strings.Untitled));
    }

    [Theory]
    [InlineData("x")] [InlineData("😀")] [InlineData("e\u0301")] [InlineData("👩‍🔧")]
    public void LongSuggestionsEndAtTextElementBoundaries(string element)
    {
        string prefix = new('A', 119);
        string suggestion = ProjectSaveFileName.Suggest(null, prefix + string.Concat(Enumerable.Repeat(element, 200)), "Untitled");
        Assert.Equal(prefix + (element.Length == 1 ? element : "") + ".spandraft", suggestion);
        Assert.InRange(suggestion[..^".spandraft".Length].Length, 1, 120);
        Assert.Equal("Untitled.spandraft", ProjectSaveFileName.Suggest(null, "a" + new string('\u0301', 200), "Untitled"));
        string reserved = ProjectSaveFileName.Suggest(null, "CON." + new string('x', 200), "Untitled");
        Assert.StartsWith("_CON.", reserved); Assert.Equal(120, reserved[..^".spandraft".Length].Length);
    }

    [Fact]
    public void ExistingPathHasPriorityAndKeepsItsFilename() =>
        Assert.Equal("Saved.SPANDRAFT", ProjectSaveFileName.Suggest(TestPath("Saved.SPANDRAFT"), "New title", "Untitled"));

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task FirstSaveSuggestsTheCommittedTitleAndSavedProjectsKeepTheirFileIdentity(bool saveAs)
    {
        var app = new ProjectTestSupport.App(); var session = app.Main.Session!;
        app.Main.EditProject(); app.Main.Setup.CalculationTitleInput.Text = "Träger oben";
        Assert.True(await app.Main.Setup.ConfirmAsync());
        Assert.Null(session.FilePath); Assert.Contains(Strings.Untitled, app.Main.WindowTitle);
        app.Main.EditProject(); app.Main.Setup.CalculationTitleInput.Text = "Unconfirmed text";
        Assert.False(await app.Main.SaveAsync(saveAs)); Assert.Equal(0, app.Dialogs.SaveQuestions);
        Assert.True(await app.Main.CancelSetupAsync());
        app.Dialogs.SavePath = null;
        Assert.False(await app.Main.SaveAsync(saveAs));
        Assert.Equal("Träger oben.spandraft", app.Dialogs.SuggestedFileName); Assert.Null(session.FilePath);
        app.Dialogs.SavePath = TestPath("Chosen name.spandraft");
        Assert.True(await app.Main.SaveAsync(saveAs));
        string path = session.FilePath!; var savepoint = session.SavedRevisionId;
        app.Main.EditProject(); app.Main.Setup.CalculationTitleInput.Text = "Different title";
        Assert.True(await app.Main.Setup.ConfirmAsync());
        Assert.Equal(path, session.FilePath); Assert.Equal(savepoint, session.SavedRevisionId);
        Assert.Equal("Chosen name.spandraft* — SpanDraft", app.Main.WindowTitle);
        app.Dialogs.SavePath = null;
        Assert.False(await app.Main.SaveAsync(saveAs: true));
        Assert.Equal("Chosen name.spandraft", app.Dialogs.SuggestedFileName); Assert.Equal(path, session.FilePath);
    }
}
