using System.Globalization;
using System.Text;
using Avalonia.Input;
using SpanDraft.Desktop.Reporting;
using SpanDraft.Desktop.Resources;
using SpanDraft.Reporting;
using Xunit;

namespace SpanDraft.Tests;

[Collection("Avalonia")]
public sealed class ReportLayoutTests : ProjectReportTestSupport
{

    [Theory]
    [InlineData("de-DE")] [InlineData("en-US")]
    public void SummaryUsesFullWidthWithInlinePositionsAndAlignedValues(string culture)
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        var (main, _) = Open(ReportTestProjects.Create());
        using var captured = ProjectReport.Capture(main.Editor!, Now);
        Assert.All(captured.Data.Indicators.Take(2), indicator => Assert.StartsWith("x = ", indicator.Position));
        var indicators = captured.Data.Indicators.Select((indicator, index) => index < 2
            ? indicator with { Position = $"x = 1234567{CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator}890123 mm (x−)" }
            : indicator).ToArray();
        using var report = new ReportDocument(captured.Data with { Indicators = indicators }, new(80, (_, _, _) => { }));
        int page = report.Pages.Select((value, index) => (value, index)).Single(p => p.value.SummaryTop.HasValue).index;
        using var bitmap = report.RenderPage(page);
        float top = report.Pages[page].SummaryTop!.Value + 19;
        var values = indicators.Select(indicator => Assert.Single(report.TextRuns,
            run => run.Text == indicator.Value && run.Bounds.Top >= top).Bounds).ToArray();
        var labels = indicators.Select(indicator => Assert.Single(report.TextRuns,
            run => indicator.Label.PlainText.StartsWith(run.Text, StringComparison.Ordinal) && run.Text.Length > 0 && run.Bounds.Top >= top).Bounds).ToArray();
        Assert.Equal(values[0].Right, values[1].Right, 3);
        Assert.Equal(values[2].Right, values[3].Right, 3);
        Assert.Equal(ReportDocument.PageWidth - ReportDocument.Margin, values[2].Right, 3);
        Assert.Equal(ReportDocument.Margin, labels[0].Left, 3);
        Assert.Equal(labels[0].Left, labels[1].Left, 3);
        Assert.Equal(labels[2].Left, labels[3].Left, 3);
        Assert.True(values[0].Right < labels[2].Left);
        Assert.True(values[0].Right - labels[0].Left > values[2].Right - labels[2].Left);
        for (int index = 0; index < indicators.Length; index++)
        {
            Assert.Equal(labels[index].Top, values[index].Top);
            Assert.True(labels[index].Right < values[index].Left);
            if (indicators[index].Position is not { } position) continue;
            var bounds = Assert.Single(report.TextRuns, run => run.Text == position && run.Bounds.Top == labels[index].Top).Bounds;
            Assert.True(labels[index].Right < bounds.Left);
            Assert.True(bounds.Right < values[index].Left);
            Assert.True(bounds.Height < labels[index].Height);
        }
        Assert.Equal(labels[0].Top, labels[2].Top);
        Assert.Equal(labels[1].Top, labels[3].Top);
        Assert.True(labels[0].Bottom < labels[1].Top);
        string directory = Path.Combine(RepositoryRoot(), "output", "pdf", "production");
        Directory.CreateDirectory(directory);
        using var png = File.Create(Path.Combine(directory, "summary-long-positions-" + culture + ".png"));
        report.WritePng(png, page);
    }

    [Theory]
    [InlineData("de-DE")] [InlineData("en-US")]
    public void SectionDesignationKeepsQuantitiesAndRadiusClausesTogether(string culture)
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        var (main, _) = Open(ReportTestProjects.Create());
        using var original = ProjectReport.Capture(main.Editor!, Now);
        static float Measure(string text) { using var layout = new ReportTextLayout(text, 8.5f); return layout.Width; }
        static string Prefix(string partialGroup, string completeGroup) => Enumerable.Range(1, 30)
            .Select(n => new string('W', n) + " ").First(prefix => Measure(prefix + partialGroup) <= ReportDocument.RightWidth
                && Measure(prefix + completeGroup) > ReportDocument.RightWidth);
        (string Name, string[] Groups)[] cases =
        [
            (Prefix("10", "10 mm") + "10 mm · y-y", ["10 mm"]),
            (Prefix("(r =", "(r = 5 mm)") + "(r = 5 mm) · y-y", ["(r = 5 mm)"]),
            ("I/H-Profil 100 × 80 × 10 mm (r = 5 mm) · y-y", ["10 mm", "(r = 5 mm)"]),
            ("I/H-Profil 100 × 80 × 10,5 mm (r = 5,5 mm) · y-y", ["10,5 mm", "(r = 5,5 mm)"]),
            ("Custom I/H section 4 × 3 × 3/8 in (r = 0.2 in) · y-y", ["3/8 in", "(r = 0.2 in)"]),
            ("I/H-Profil\n100 × 80 × 10 mm (r = 5 mm) · y-y", ["10 mm", "(r = 5 mm)"]),
            ("Custom section with ordinary word boundaries", [])
        ];
        foreach (var (name, groups) in cases)
        {
            using var report = new ReportDocument(original.Data with { Section = name }, new(130, (_, _, _) => { }));
            using var bitmap = report.RenderPage(0, 96);
            var lines = SectionDesignationRuns(report, 0);
            Assert.True(lines.Length > 1, name);
            Assert.Equal(name.Replace('\n', ' '), string.Join(" ", lines.Select(r => r.Text)));
            Assert.Equal(name, report.Data.Section);
            Assert.DoesNotContain(report.Data.Metadata, m => m.Label == report.Data.Labels.Section);
            foreach (var group in groups) Assert.Contains(lines, line => line.Text.Contains(group, StringComparison.Ordinal));
            AssertInformationBlocksSeparated(report, 0); AssertTextContained(report);
        }
    }

    [Theory]
    [InlineData("S235JR (1.0038) · A36", new[] { "S235JR (1.0038) · A36" })]
    [InlineData("S235JR · A36 · SS400", new[] { "S235JR · A36 · SS400" })]
    [InlineData("X5CrNi18-10 (1.4301) · AISI 304 · UNS S30400", new[] { "X5CrNi18-10 (1.4301)", "AISI 304 · UNS S30400" })]
    [InlineData("X5CrNi18-10 (1.4301)", new[] { "X5CrNi18-10 (1.4301)" })]
    public void MaterialDesignationKeepsStandardsTogetherWhenWrapping(string designation, string[] expectedLines)
    {
        var (main, _) = Open(ReportTestProjects.Create());
        using var original = ProjectReport.Capture(main.Editor!, Now);
        using var report = new ReportDocument(original.Data with { Material = designation }, new(130, (_, _, _) => { }));
        using var bitmap = report.RenderPage(0, 96);
        var lines = MaterialDesignationRuns(report);
        Assert.Equal(expectedLines, lines.Select(r => r.Text));
        Assert.Equal(designation, report.Data.Material);
        Assert.DoesNotContain(report.Data.Metadata, m => m.Label == report.Data.Labels.Material);
        Assert.All(lines.Zip(lines.Skip(1)), pair =>
        {
            Assert.Equal(pair.First.Bounds.Left, pair.Second.Bounds.Left);
            Assert.True(pair.First.Bounds.Bottom < pair.Second.Bounds.Top);
        });
        AssertTextContained(report); AssertInformationBlocksSeparated(report, 0);
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void MaterialDesignationWrapsOnlyTheOversizedBlock(bool wideName)
    {
        string wide = new('W', 24);
        string designation = wideName ? wide + " · AISI 304" : "S235JR · " + wide;
        var (main, _) = Open(ReportTestProjects.Create());
        using var original = ProjectReport.Capture(main.Editor!, Now);
        using var report = new ReportDocument(original.Data with { Material = designation }, new(130, (_, _, _) => { }));
        using var bitmap = report.RenderPage(0, 96);
        var lines = MaterialDesignationRuns(report);
        Assert.Equal(3, lines.Length);
        if (wideName)
        {
            Assert.Equal(wide, string.Concat(lines.Take(2).Select(r => r.Text)));
            Assert.Equal("AISI 304", lines[^1].Text);
        }
        else
        {
            Assert.Equal("S235JR", lines[0].Text);
            Assert.Equal(wide, string.Concat(lines.Skip(1).Select(r => r.Text)));
        }
        Assert.DoesNotContain(report.Data.Metadata, m => m.Label == report.Data.Labels.Material);
        AssertTextContained(report); AssertInformationBlocksSeparated(report, 0);
    }

    [Fact]
    public void MaterialDesignationWithoutStandardsStillUsesNormalWrapping()
    {
        string designation = new('W', 24);
        var (main, _) = Open(ReportTestProjects.Create());
        using var original = ProjectReport.Capture(main.Editor!, Now);
        using var report = new ReportDocument(original.Data with { Material = designation }, new(130, (_, _, _) => { }));
        using var bitmap = report.RenderPage(0, 96);
        var lines = MaterialDesignationRuns(report);
        Assert.Equal(2, lines.Length);
        Assert.Equal(designation, string.Concat(lines.Select(r => r.Text)));
        Assert.DoesNotContain(report.Data.Metadata, m => m.Label == report.Data.Labels.Material);
        AssertTextContained(report); AssertInformationBlocksSeparated(report, 0);
    }

    [Fact]
    public void LongMaterialDesignationPreservesEveryStandardInReportMetadata()
    {
        var state = ReportTestProjects.Create();
        var standards = Enumerable.Range(0, 30).Select(i => $"Long standard designation {i}").ToArray();
        var material = LibraryTestSupport.WithMetadata(state.Document.Material, state.Document.Material.Category, "1.0038", standards);
        var (main, _) = Open(state with { Document = state.Document with { Material = material } });
        using var report = ProjectReport.Capture(main.Editor!, Now);
        string expected = "S235JR (1.0038) · " + string.Join(" · ", standards);
        Assert.Equal(expected, report.Data.Material);
        Assert.Contains(report.Data.Metadata, m => m.Label == report.Data.Labels.Material && m.Value == expected);
        for (int page = 0; page < report.Pages.Count; page++)
        {
            using var bitmap = report.RenderPage(page, 96); AssertTextContained(report);
            if (!report.Pages[page].HasGraphics) continue;
            var lines = MaterialDesignationRuns(report);
            Assert.Equal(3, lines.Length);
            Assert.Equal("S235JR (1.0038)", lines[0].Text);
            Assert.Equal("…", lines[^1].Text);
            AssertInformationBlocksSeparated(report, page);
        }
    }

    [Theory]
    [InlineData("de-DE")] [InlineData("en-US")]
    public void HeaderCentersTheTitleAboveThreeAlignedMetadataColumns(string culture)
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        var (main, _) = Open(ReportTestProjects.Create());
        using var report = ProjectReport.Capture(main.Editor!, Now);
        using var bitmap = report.RenderPage(0, 96);
        var heading = Assert.Single(report.TextRuns, r => r.Text == report.Data.Labels.Heading).Bounds;
        var title = Assert.Single(report.TextRuns, r => r.Text == report.Data.Title).Bounds;
        Assert.Equal(ReportDocument.PageWidth / 2, heading.MidX, 3);
        Assert.Equal(heading.MidX, title.MidX, 3);
        Assert.True(title.Top > heading.Bottom);
        Assert.True(title.Top - heading.Bottom <= heading.Height / 2);
        Assert.True(title.Height > heading.Height * 1.5f);
        Assert.DoesNotContain(report.TextRuns, r => r.Text == report.Data.Labels.Title);
        string[] labels = [report.Data.Labels.Project, report.Data.Labels.Reference, report.Data.Labels.Author];
        var columns = labels.Select(label => Assert.Single(report.TextRuns, r => r.Text == label).Bounds).ToArray();
        Assert.True(columns[0].Top > title.Bottom);
        Assert.True(columns[0].Top - title.Bottom <= title.Height);
        Assert.All(columns, bounds => Assert.Equal(columns[0].Top, bounds.Top));
        Assert.True(columns[1].Left - columns[0].Left > columns[2].Left - columns[1].Left);
        var values = labels.Select(label => report.Data.Metadata.Single(m => m.Label == label).Value)
            .Select(value => Assert.Single(report.TextRuns, r => r.Text == value).Bounds).ToArray();
        for (int i = 0; i < columns.Length; i++)
        {
            Assert.Equal(columns[i].Left, values[i].Left);
            Assert.True(columns[i].Bottom < values[i].Top);
            Assert.True(values[i].Bottom < report.Pages[0].BodyTop - 12);
            if (i + 1 < columns.Length) Assert.True(values[i].Right < columns[i + 1].Left);
        }
        Assert.All(values, bounds => Assert.Equal(values[0].Top, bounds.Top));
    }

    [Theory]
    [InlineData("de-DE")] [InlineData("en-US")]
    public void HeaderWrapsLongTitleAndValuesWithinTheExistingHeaderArea(string culture)
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        var state = ReportTestProjects.Create() with
        {
            Info = new(string.Join(" ", Enumerable.Repeat("Trägernachweis für die Bühnenkonstruktion", 3)),
                "Erweiterung des bestehenden Veranstaltungsgebäudes im nördlichen Projektbereich",
                "Referenz zur Entwurfsplanung im Oktober 2026", "Ingenieurbüro für Tragwerksplanung und Konstruktion")
        };
        var (main, _) = Open(state);
        using var report = ProjectReport.Capture(main.Editor!, Now);
        using var bitmap = report.RenderPage(0, 96);
        Assert.True(report.Pages[0].HasGraphics);
        var header = report.TextRuns.Where(r => r.Bounds.Top < report.Pages[0].BodyTop - 12).ToArray();
        string[] labels = [report.Data.Labels.Project, report.Data.Labels.Reference, report.Data.Labels.Author];
        var columns = labels.Select(label => Assert.Single(header, r => r.Text == label).Bounds).ToArray();
        var title = header.Skip(1).TakeWhile(r => r.Bounds.Top < columns[0].Top).ToArray();
        Assert.True(title.Length > 1);
        Assert.Equal(state.Info.CalculationTitle, string.Join(" ", title.Select(r => r.Text)));
        Assert.All(title, r => Assert.Equal(ReportDocument.PageWidth / 2, r.Bounds.MidX, 3));
        for (int i = 0; i < columns.Length; i++)
        {
            var values = header.Where(r => r.Bounds.Left == columns[i].Left && r.Bounds.Top > columns[i].Bottom).ToArray();
            Assert.Equal(report.Data.Metadata.Single(m => m.Label == labels[i]).Value, string.Join(" ", values.Select(r => r.Text)));
            float right = i + 1 < columns.Length ? columns[i + 1].Left : ReportDocument.PageWidth - ReportDocument.Margin;
            Assert.All(values, r => Assert.True(r.Bounds.Right <= right));
        }
        Assert.All(header, r => Assert.True(r.Bounds.Bottom < report.Pages[0].BodyTop - 12));
        AssertTextContained(report);
    }

    [Theory]
    [InlineData("de-DE")] [InlineData("en-US")]
    public void MissingOptionalProjectInformationKeepsAllHeaderLabelsVisible(string culture)
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        var state = ReportTestProjects.Create();
        var (main, _) = Open(state); using var full = ProjectReport.Capture(main.Editor!, Now);
        var (compactMain, _) = Open(state with { Info = new(state.Info!.CalculationTitle, null, null, state.Info.Author) });
        using var compact = ProjectReport.Capture(compactMain.Editor!, Now);
        Assert.Equal(full.Data.Metadata.Count - 2, compact.Data.Metadata.Count);
        Assert.All(compact.Data.Metadata, row => Assert.False(string.IsNullOrWhiteSpace(row.Value)));
        Assert.True(compact.Pages[0].BodyTop <= full.Pages[0].BodyTop);
        Assert.DoesNotContain(compact.Data.Metadata, row => row.Value is "Neutrales Beispiel" or "BEISPIEL-01");
        using var fullBitmap = full.RenderPage(0, 96);
        using var compactBitmap = compact.RenderPage(0, 96);
        string[] labels = [compact.Data.Labels.Project, compact.Data.Labels.Reference, compact.Data.Labels.Author];
        var columns = labels.Select(label => Assert.Single(compact.TextRuns, r => r.Text == label).Bounds).ToArray();
        Assert.All(columns, bounds => Assert.Equal(columns[0].Top, bounds.Top));
        for (int i = 0; i < labels.Length; i++)
            Assert.Equal(Assert.Single(full.TextRuns, r => r.Text == labels[i]).Bounds.Left, columns[i].Left);
        foreach (var column in columns.Take(2))
        {
            var missing = Assert.Single(compact.TextRuns, r => r.Text == "-" && r.Bounds.Left == column.Left
                && r.Bounds.Top > column.Bottom && r.Bounds.Bottom < compact.Pages[0].BodyTop - 12);
            Assert.True(missing.Bounds.Top > column.Bottom);
        }
        var author = Assert.Single(compact.TextRuns, r => r.Text == compact.Data.Labels.Author).Bounds;
        Assert.Equal(Assert.Single(full.TextRuns, r => r.Text == full.Data.Labels.Author).Bounds.Left, author.Left);
        var title = Assert.Single(compact.TextRuns, r => r.Text == compact.Data.Title).Bounds;
        Assert.True(title.Bottom < author.Top);
        Assert.All(compact.TextRuns.Where(r => r.Bounds.Top < compact.Pages[0].BodyTop - 12),
            r => Assert.True(r.Bounds.Bottom < compact.Pages[0].BodyTop - 12));
    }

    [Theory]
    [InlineData("de-DE", true)] [InlineData("en-US", true)]
    [InlineData("de-DE", false)] [InlineData("en-US", false)]
    public void MissingAuthorOmitsItsLabelValueAndPlaceholderWithoutAddingHeaderSpace(string culture, bool hasOtherInfo)
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        var state = ReportTestProjects.Create();
        var info = state.Info!;
        state = state with { Info = new("Authorless calculation", hasOtherInfo ? info.ProjectTitle : null,
            hasOtherInfo ? info.Reference : null, "Author") };
        var (main, _) = Open(state); using var full = ProjectReport.Capture(main.Editor!, Now);
        var (withoutAuthor, _) = Open(state with { Info = new("Authorless calculation", state.Info.ProjectTitle, state.Info.Reference, null) });
        using var report = ProjectReport.Capture(withoutAuthor.Editor!, Now);
        Assert.Equal("Authorless calculation", report.Data.Title); Assert.Empty(report.Data.Author);
        Assert.DoesNotContain(report.Data.Metadata, row => row.Label == report.Data.Labels.Author);
        Assert.Equal(full.Data.Metadata.Where(row => row.Label != full.Data.Labels.Author), report.Data.Metadata);
        using var fullBitmap = full.RenderPage(0, 96);
        using var bitmap = report.RenderPage(0, 96);
        Assert.DoesNotContain(report.TextRuns, run => run.Text == report.Data.Labels.Author || run.Text == "Author");
        Assert.True(report.Pages[0].BodyTop <= full.Pages[0].BodyTop);
        foreach (string label in new[] { report.Data.Labels.Project, report.Data.Labels.Reference, report.Data.Title })
            Assert.Equal(Assert.Single(full.TextRuns, r => r.Text == label).Bounds,
                Assert.Single(report.TextRuns, r => r.Text == label).Bounds);
        var header = report.TextRuns.Where(r => r.Bounds.Top < report.Pages[0].BodyTop - 12).ToArray();
        Assert.Equal(hasOtherInfo ? 0 : 2, header.Count(r => r.Text == "-"));
        AssertTextContained(report);
        using var pdf = new MemoryStream(); report.WritePdf(pdf);
        string text = Encoding.Latin1.GetString(pdf.ToArray());
        Assert.Contains("/Title (Authorless calculation)", text);
        Assert.Contains("/Creator (SpanDraft " + ApplicationMetadata.Version + ")", text);
        Assert.DoesNotContain("/Author (Author)", text);
    }

    [Theory]
    [InlineData("de-DE")] [InlineData("en-US")]
    public void MissingMetadataDoesNotShrinkTheTitleOrColumnLabels(string culture)
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        var (main, _) = Open(ReportTestProjects.Create());
        using var full = ProjectReport.Capture(main.Editor!, Now);
        using var missing = new ReportDocument(full.Data with { Metadata = [] }, new(80, (_, _, _) => { }));
        using var fullBitmap = full.RenderPage(0, 96);
        using var missingBitmap = missing.RenderPage(0, 96);
        foreach (string text in new[] { full.Data.Title, full.Data.Labels.Project, full.Data.Labels.Reference, full.Data.Labels.Author })
        {
            var expected = Assert.Single(full.TextRuns, r => r.Text == text).Bounds;
            var actual = Assert.Single(missing.TextRuns, r => r.Text == text).Bounds;
            Assert.Equal(expected, actual);
        }
        var placeholders = missing.TextRuns.Where(r => r.Text == "-" && r.Bounds.Top < missing.Pages[0].BodyTop - 12).ToArray();
        Assert.Equal(3, placeholders.Length);
        foreach (var placeholder in placeholders)
        {
            var label = Assert.Single(missing.TextRuns, r => r.Bounds.Left == placeholder.Bounds.Left &&
                new[] { full.Data.Labels.Project, full.Data.Labels.Reference, full.Data.Labels.Author }.Contains(r.Text));
            Assert.True(label.Bounds.Bottom < placeholder.Bounds.Top);
            Assert.True(placeholder.Bounds.Bottom < missing.Pages[0].BodyTop - 12);
        }
    }

    [Fact]
    public void TallMeasuredHeaderMovesMetadataToItsOwnPageBeforeItCanDisplaceTheDiagrams()
    {
        var (main, _) = Open(ReportTestProjects.Create());
        using var captured = ProjectReport.Capture(main.Editor!, Now);
        using var report = new ReportDocument(captured.Data with
        {
            Metadata = [(captured.Data.Labels.Title, captured.Data.Title), (captured.Data.Labels.Reference, new string('r', 1100))]
        }, new(260, (_, _, _) => { }));
        Assert.False(report.Pages[0].HasGraphics);
        Assert.Contains(report.Pages, p => p.HasGraphics);
        for (int page = 0; page < report.Pages.Count; page++)
        {
            using var bitmap = report.RenderPage(page, 96);
            AssertTextContained(report);
        }
    }

    [Fact]
    public void LongMaterialAndSectionNamesStayCompleteWithoutOverlappingOtherBlocks()
    {
        var (main, _) = Open(ReportTestProjects.Create()); using var original = ProjectReport.Capture(main.Editor!, Now);
        string name = string.Join(" ", Enumerable.Repeat("Very long custom library name", 150));
        using var report = new ReportDocument(original.Data with { Section = name, Material = name }, new(130, (_, _, _) => { }));
        Assert.Contains(report.Data.Metadata, m => m.Label == report.Data.Labels.Section && m.Value == name);
        Assert.Contains(report.Data.Metadata, m => m.Label == report.Data.Labels.Material && m.Value == name);
        Assert.True(report.Pages.Count > 1);
        for (int page = 0; page < report.Pages.Count; page++)
        { using var bitmap = report.RenderPage(page, 96); AssertTextContained(report); }
    }
}
