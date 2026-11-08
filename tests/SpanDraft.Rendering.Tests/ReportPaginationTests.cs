using SpanDraft.Reporting;
using Xunit;

namespace SpanDraft.Tests;

public sealed class ReportPaginationTests : ProjectReportTestSupport
{
    public ReportPaginationTests() : base(controls: false) { }


    [Theory]
    [InlineData(1)] [InlineData(25)] [InlineData(90)] [InlineData(250)]
    public void TablesFlowAcrossColumnsAndSummaryFollowsEveryRow(int count)
    {
        var source = SyntheticReportData.Create();
        ReportTable Table(string title, int rows) => new(title, ["Name"], [1],
            Enumerable.Range(0, rows).Select(i => (IReadOnlyList<string>)new[] { title + i }).ToArray());
        using var report = new ReportDocument(source with
        {
            Tables = [Table("Forces", count), Table("Moments", 1), Table("Distributed", 1), Table("Reactions", count)]
        }, new(130, (_, _, _) => { }));
        AssertCompleteTablesAndFinalSummary(report);
        var first = report.Pages[0];
        if (count >= 25)
        {
            Assert.Null(first.SummaryTop);
            var last = first.Tables.Last();
            Assert.True(ReportDocument.FooterTop - 14 - last.Bounds.Bottom < ReportDocument.RowHeight);
        }
        if (count >= 90)
        {
            Assert.Contains(report.Pages.Skip(1), page => page.Tables.GroupBy(t => t.Bounds.Left).Any(g => g.Count() > 1));
            Assert.Contains(report.Pages.Skip(1), page => page.Tables.Select(t => t.Bounds.Left).Distinct().Count() == 2);
        }
        for (int i = 0; i < report.Pages.Count; i++)
        {
            using var bitmap = report.RenderPage(i, 96);
            foreach (var fragment in report.Pages[i].Tables)
            {
                bool continued = fragment.Rows[0].Row > 0 || fragment.Rows[0].FirstLine > 0;
                string title = report.Data.Tables[fragment.Table].Title
                    + (continued ? " \u00b7 " + report.Data.Labels.Continuation : "");
                Assert.Contains(report.TextRuns, run => run.Text == title);
            }
        }
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void FirstPageStartsNextTableOnlyWhenACompleteDataRowFits(bool rowFits)
    {
        var source = SyntheticReportData.Create();
        ReportTable Table(string title, int rows) => new(title, ["Name"], [1],
            Enumerable.Range(0, rows).Select(i => (IReadOnlyList<string>)new[] { "R" + i }).ToArray());
        using var probe = new ReportDocument(source with { Tables = [Table("First", 200)] }, new(130, (_, _, _) => { }));
        var fragment = probe.Pages[0].Tables[0];
        float available = ReportDocument.FooterTop - 14 - fragment.Bounds.Top;
        // Same one-line headings; preserve the existing 12-point gap between tables.
        int count = (int)Math.Floor((available - 2 * fragment.HeadingHeight - 12 - ReportDocument.RowHeight) / ReportDocument.RowHeight);
        if (!rowFits) count++;
        using var report = new ReportDocument(source with { Tables = [Table("First", count), Table("Second", 200)] },
            new(130, (_, _, _) => { }));
        var next = report.Pages[0].Tables.Where(f => f.Table == 1).ToArray();
        if (rowFits) Assert.Single(Assert.Single(next).Rows);
        else Assert.Empty(next);
        AssertCompleteTablesAndFinalSummary(report);
    }

    [Fact]
    public void WrappedRowsThatFitFreshColumnsRemainWhole()
    {
        var source = SyntheticReportData.Create();
        var rows = Enumerable.Range(0, 24).Select(i => (IReadOnlyList<string>)new[]
            { i + " " + string.Join(" ", Enumerable.Repeat("long cell content", 16)) }).ToArray();
        using var report = new ReportDocument(source with { Tables = [new("Reactions", ["Name"], [1], rows)] },
            new(130, (_, _, _) => { }));
        Assert.True(report.Pages.Count > 1);
        var slices = report.Pages.SelectMany(p => p.Tables).SelectMany(t => t.Rows).ToArray();
        Assert.Equal(Enumerable.Range(0, rows.Length), slices.Select(s => s.Row));
        Assert.All(slices, s => { Assert.Equal(0, s.FirstLine); Assert.Equal(s.Cells.Max(c => c.Length), s.LineCount); });
        AssertCompleteTablesAndFinalSummary(report);
    }

    [Fact]
    public void ContinuationPageFlowsThroughShortTablesThenReactionsLeftToRightBeforeNextPage()
    {
        var source = SyntheticReportData.Create();
        ReportTable Table(string title, int count) => new(title, ["Name"], [1],
            Enumerable.Range(0, count).Select(i => (IReadOnlyList<string>)new[] { title + i }).ToArray());
        using var probe = new ReportDocument(source with { Tables = [Table("Stream", 300)] }, new(130, (_, _, _) => { }));
        int firstPageRows = Assert.Single(probe.Pages[0].Tables).Rows.Count;
        int columnRows = probe.Pages[1].Tables[0].Rows.Count;
        using var report = new ReportDocument(source with
        {
            Tables = [Table("Forces", firstPageRows), Table("Momente", 2),
                Table("Streckenlasten", 2), Table("Reaktionen", columnRows * 2)]
        }, new(130, (_, _, _) => { }));
        var page = report.Pages[1];
        var left = page.Tables.Where(f => f.Bounds.Left == ReportDocument.Margin).ToArray();
        var right = page.Tables.Where(f => f.Bounds.Left > ReportDocument.Margin).ToArray();
        Assert.Equal(new[] { 1, 2, 3 }, left.Select(f => f.Table));
        var continued = Assert.Single(right);
        Assert.Equal(3, continued.Table);
        Assert.Equal(page.BodyTop, left[0].Bounds.Top);
        Assert.Equal(page.BodyTop, continued.Bounds.Top);
        Assert.All(left.Zip(left.Skip(1)), pair => Assert.True(pair.First.Bounds.Bottom < pair.Second.Bounds.Top));
        Assert.Equal(left[^1].Rows[^1].Row + 1, continued.Rows[0].Row);
        Assert.True(ReportDocument.FooterTop - 14 - left[^1].Bounds.Bottom < ReportDocument.RowHeight);
        Assert.True(ReportDocument.FooterTop - 14 - continued.Bounds.Bottom < ReportDocument.RowHeight);
        Assert.Null(page.SummaryTop);
        var next = report.Pages[2].Tables[0];
        Assert.Equal(ReportDocument.Margin, next.Bounds.Left);
        Assert.Equal(continued.Rows[^1].Row + 1, next.Rows[0].Row);
        AssertCompleteTablesAndFinalSummary(report);
        using var bitmap = report.RenderPage(1, 96);
        Assert.Contains(report.TextRuns, r => r.Text == "Reaktionen \u00b7 " + report.Data.Labels.Continuation
            && r.Bounds.Left == continued.Bounds.Left && r.Bounds.Top == continued.Bounds.Top);
    }

    [Theory]
    [InlineData("Supports", false)] [InlineData("Forces", false)] [InlineData("Moments", false)]
    [InlineData("Distributed", false)] [InlineData("Reactions", false)] [InlineData("Future table", true)]
    public void FinalContinuationBalancesAnyTableAtWholeRowBoundaries(string title, bool wrapped)
    {
        var source = SyntheticReportData.Create();
        ReportTable Table(string name, int count) => new(name, ["Name"], [1],
            Enumerable.Range(0, count).Select(i => (IReadOnlyList<string>)new[] { name + i }).ToArray());
        using var probe = new ReportDocument(source with { Tables = [Table("Stream", 300)] }, new(130, (_, _, _) => { }));
        int firstPageRows = Assert.Single(probe.Pages[0].Tables).Rows.Count;
        string suffix = wrapped ? " " + string.Join(" ", Enumerable.Repeat("wrapped content", 6)) : "";
        var rows = Enumerable.Range(0, 18).Select(i => (IReadOnlyList<string>)new[] { title + i + suffix }).ToArray();
        using var report = new ReportDocument(source with
        {
            Tables = [Table("Forces", firstPageRows), new(title, ["Name"], [1], rows)]
        }, new(130, (_, _, _) => { }));
        Assert.Equal(probe.Pages[0].Tables[0].Bounds, report.Pages[0].Tables[0].Bounds);
        Assert.Equal(firstPageRows, Assert.Single(report.Pages[0].Tables).Rows.Count);
        var page = report.Pages[1];
        Assert.Equal(2, page.Tables.Count);
        var left = page.Tables[0]; var right = page.Tables[1];
        Assert.Equal(ReportDocument.Margin, left.Bounds.Left);
        Assert.True(right.Bounds.Left > left.Bounds.Right);
        Assert.Equal(page.BodyTop, left.Bounds.Top);
        Assert.Equal(page.BodyTop, right.Bounds.Top);
        Assert.Equal(9, left.Rows.Count);
        Assert.Equal(9, right.Rows.Count);
        Assert.Equal(left.Bounds.Bottom, right.Bounds.Bottom, 3);
        Assert.Equal(Enumerable.Range(0, 18), page.Tables.SelectMany(f => f.Rows).Select(r => r.Row));
        Assert.All(page.Tables.SelectMany(f => f.Rows), r =>
        {
            Assert.Equal(0, r.FirstLine);
            Assert.Equal(r.Cells.Max(c => c.Length), r.LineCount);
        });
        Assert.True(page.SummaryTop >= Math.Max(left.Bounds.Bottom, right.Bounds.Bottom) + 16);
        AssertCompleteTablesAndFinalSummary(report);
        using var bitmap = report.RenderPage(1, 96);
        Assert.Contains(report.TextRuns, r => r.Text == title + " \u00b7 " + report.Data.Labels.Continuation
            && r.Bounds.Left == right.Bounds.Left && r.Bounds.Top == right.Bounds.Top);
    }

    [Fact]
    public void FinalContinuationChoosesBestSplitInsideReactionsAfterShortTables()
    {
        var source = SyntheticReportData.Create();
        ReportTable Table(string title, int count) => new(title, ["Name"], [1],
            Enumerable.Range(0, count).Select(i => (IReadOnlyList<string>)new[] { title + i }).ToArray());
        using var probe = new ReportDocument(source with { Tables = [Table("Stream", 300)] }, new(130, (_, _, _) => { }));
        using var report = new ReportDocument(source with
        {
            Tables = [Table("Forces", probe.Pages[0].Tables[0].Rows.Count), Table("Momente", 1),
                Table("Streckenlasten", 2), Table("Reaktionen", 20)]
        }, new(130, (_, _, _) => { }));
        var page = report.Pages[1];
        var left = page.Tables.Where(f => f.Bounds.Left == ReportDocument.Margin).ToArray();
        var right = Assert.Single(page.Tables, f => f.Bounds.Left > ReportDocument.Margin);
        Assert.Equal(new[] { 1, 2, 3 }, left.Select(f => f.Table));
        Assert.Equal(3, right.Table);
        Assert.Equal(6, left[^1].Rows.Count);
        Assert.Equal(14, right.Rows.Count);
        Assert.Equal(6, right.Rows[0].Row);
        Assert.True(Math.Abs(left[^1].Bounds.Bottom - right.Bounds.Bottom) < ReportDocument.RowHeight);
        AssertCompleteTablesAndFinalSummary(report);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void FinalContinuationCanSplitBetweenTablesOrBetweenTallWholeRows(bool tallRows)
    {
        var source = SyntheticReportData.Create();
        ReportTable Table(string title, int count) => new(title, ["Name"], [1],
            Enumerable.Range(0, count).Select(i => (IReadOnlyList<string>)new[] { title + i }).ToArray());
        using var probe = new ReportDocument(source with { Tables = [Table("Stream", 300)] }, new(130, (_, _, _) => { }));
        var tables = new List<ReportTable> { Table("Forces", probe.Pages[0].Tables[0].Rows.Count) };
        if (tallRows)
            tables.Add(new("Tall", ["Name"], [1], Enumerable.Range(0, 2)
                .Select(i => (IReadOnlyList<string>)new[] { string.Join("\n", Enumerable.Repeat("Row" + i, 8)) }).ToArray()));
        else
            tables.AddRange([Table("First", 4), Table("Next", 4)]);
        using var report = new ReportDocument(source with { Tables = tables }, new(130, (_, _, _) => { }));
        var page = report.Pages[1];
        Assert.Equal(2, page.Tables.Count);
        var left = page.Tables[0]; var right = page.Tables[1];
        Assert.True(right.Bounds.Left > left.Bounds.Right);
        Assert.Equal(left.Bounds.Bottom, right.Bounds.Bottom, 3);
        Assert.All(page.Tables.SelectMany(f => f.Rows), row =>
        {
            Assert.Equal(0, row.FirstLine);
            Assert.Equal(row.Cells.Max(c => c.Length), row.LineCount);
        });
        if (tallRows)
        {
            Assert.Single(left.Rows);
            Assert.Single(right.Rows);
        }
        else
        {
            Assert.Equal(1, left.Table);
            Assert.Equal(2, right.Table);
            Assert.Equal(0, right.Rows[0].Row);
            using var bitmap = report.RenderPage(1, 96);
            Assert.Contains(report.TextRuns, r => r.Text == "Next" && r.Bounds.Left == right.Bounds.Left);
            Assert.DoesNotContain(report.TextRuns, r => r.Text == "Next \u00b7 " + report.Data.Labels.Continuation);
        }
        AssertCompleteTablesAndFinalSummary(report);
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void SingleShortRemainingTableStaysInLeftColumn(int rows)
    {
        var source = SyntheticReportData.Create();
        ReportTable Table(string title, int count) => new(title, ["Name"], [1],
            Enumerable.Range(0, count).Select(i => (IReadOnlyList<string>)new[] { title + i }).ToArray());
        using var probe = new ReportDocument(source with { Tables = [Table("Stream", 300)] }, new(130, (_, _, _) => { }));
        using var report = new ReportDocument(source with
        {
            Tables = [Table("Forces", probe.Pages[0].Tables[0].Rows.Count), Table("Short", rows)]
        }, new(130, (_, _, _) => { }));
        var fragment = Assert.Single(report.Pages[1].Tables);
        Assert.Equal(ReportDocument.Margin, fragment.Bounds.Left);
        Assert.Equal(rows, fragment.Rows.Count);
        AssertCompleteTablesAndFinalSummary(report);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void EveryTableCanFlowThroughBothColumnsAndMultiplePages(int longTable)
    {
        var source = SyntheticReportData.Create();
        string[] titles = ["Supports", "Forces", "Moments", "Distributed", "Reactions"];
        var tables = titles.Select((title, table) => new ReportTable(title, ["Name"], [1],
            Enumerable.Range(0, table == longTable ? 400 : 2)
                .Select(row => (IReadOnlyList<string>)new[] { $"T{table}R{row}" }).ToArray())).ToArray();
        using var report = new ReportDocument(source with
        {
            Tables = [new("Empty", ["Name"], [1], []), .. tables, new("Empty", ["Name"], [1], [])]
        }, new(130, (_, _, _) => { }));
        AssertCompleteTablesAndFinalSummary(report);
        var fragments = report.Pages.SelectMany(p => p.Tables).Where(f => f.Table == longTable + 1).ToArray();
        Assert.True(report.Pages.Count > 3);
        Assert.Contains(report.Pages.Skip(1), page => page.Tables.Where(f => f.Table == longTable + 1)
            .Select(f => f.Bounds.Left).Distinct().Count() == 2);
        Assert.True(fragments.Length > 4);
        foreach (var fragment in report.Pages.SelectMany(p => p.Tables))
        foreach (var slice in fragment.Rows)
            Assert.Equal($"T{fragment.Table - 1}R{slice.Row}", Assert.Single(slice.Cells[0]));
    }

    [Fact]
    public void FirstRowMovedToAnotherPageIsRewrappedWithoutContinuationTitle()
    {
        var source = SyntheticReportData.Create();
        ReportTable Table(string title, IReadOnlyList<IReadOnlyList<string>> rows) => new(title, ["Name"], [1], rows);
        var simpleRows = Enumerable.Range(0, 200).Select(i => (IReadOnlyList<string>)new[] { "R" + i }).ToArray();
        using var probe = new ReportDocument(source with { Tables = [Table("First", simpleRows)] }, new(130, (_, _, _) => { }));
        var first = probe.Pages[0].Tables[0];
        float available = ReportDocument.FooterTop - 14 - first.Bounds.Top;
        int count = (int)Math.Floor((available - 2 * first.HeadingHeight - 12 - ReportDocument.RowHeight) / ReportDocument.RowHeight);
        string longCell = string.Join(" ", Enumerable.Repeat("wrapped content", 18));
        using var report = new ReportDocument(source with
        {
            Tables = [Table("First", simpleRows.Take(count).ToArray()), Table("Second", [new[] { longCell }])]
        }, new(130, (_, _, _) => { }));
        Assert.DoesNotContain(report.Pages[0].Tables, f => f.Table == 1);
        var next = Assert.Single(report.Pages[1].Tables, f => f.Table == 1);
        var row = Assert.Single(next.Rows);
        Assert.Equal(0, row.FirstLine);
        Assert.Equal(row.Cells[0].Length, row.LineCount);
        Assert.True(next.Bounds.Width > first.Bounds.Width);
        using var narrow = new ReportDocument(source with { Tables = [Table("Second", [new[] { longCell }])] }, new(130, (_, _, _) => { }));
        Assert.True(narrow.Pages[0].Tables[0].Rows[0].LineCount > row.LineCount);
        using var bitmap = report.RenderPage(1, 96);
        Assert.Contains(report.TextRuns, r => r.Text == "Second");
        Assert.DoesNotContain(report.TextRuns, r => r.Text == "Second \u00b7 " + report.Data.Labels.Continuation);
        AssertCompleteTablesAndFinalSummary(report);
    }

    [Fact]
    public void OversizedFirstRowRepeatsHeadersAndContinuationAfterFirstSlice()
    {
        var source = SyntheticReportData.Create();
        string text = string.Join("\n", Enumerable.Range(0, 180).Select(i => $"Line {i}"));
        using var report = new ReportDocument(source with
        {
            Tables = [new("Arbitrary", ["Name", "Value"], [.7f, .3f], [new[] { text, "42" }, new[] { "End", "7" }])]
        }, new(130, (_, _, _) => { }));
        Assert.Empty(report.Pages[0].Tables);
        AssertCompleteTablesAndFinalSummary(report);
        var fragments = report.Pages.SelectMany(p => p.Tables).ToArray();
        Assert.True(fragments.Length > 2);
        Assert.All(fragments, f => Assert.Equal(fragments[0].Bounds.Width, f.Bounds.Width, 3));
        Assert.Equal(Enumerable.Range(0, 180).Select(i => $"Line {i}"), fragments.SelectMany(f => f.Rows)
            .Where(r => r.Row == 0).SelectMany(r => r.Cells[0].Skip(r.FirstLine).Take(r.LineCount)));
        for (int page = 1; page < report.Pages.Count; page++)
        {
            using var bitmap = report.RenderPage(page, 96);
            foreach (var fragment in report.Pages[page].Tables)
            {
                bool continued = fragment.Rows[0].Row > 0 || fragment.Rows[0].FirstLine > 0;
                Assert.Contains(report.TextRuns, run => run.Text == "Arbitrary" +
                    (continued ? " \u00b7 " + report.Data.Labels.Continuation : "") && run.Bounds.Top == fragment.Bounds.Top);
                Assert.Contains(report.TextRuns, run => run.Text == "Name" &&
                    run.Bounds.Top > fragment.Bounds.Top && run.Bounds.Bottom <= fragment.Bounds.Top + fragment.HeadingHeight);
            }
        }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void SummaryIsPlacedAfterUnreservedTableStream(bool largeSummary)
    {
        var source = SyntheticReportData.Create();
        var table = new ReportTable("Any table", ["Name"], [1], Enumerable.Range(0, 30)
            .Select(i => (IReadOnlyList<string>)new[] { "R" + i }).ToArray());
        var data = source with { Tables = [table] };
        using var baseline = new ReportDocument(data with { Indicators = [], Labels = data.Labels with { Assumptions = [] } }, new(130, (_, _, _) => { }));
        float summaryTop = baseline.Pages.Last().SummaryTop!.Value;
        // With no indicators the two headings consume 36 points; each short note adds 12.
        int notesThatFit = (int)Math.Floor((ReportDocument.FooterTop - 14 - summaryTop - 36) / 12);
        Assert.True(notesThatFit > 0);
        using var report = new ReportDocument(data with
        {
            Indicators = [], Labels = data.Labels with
            { Assumptions = Enumerable.Repeat("Calculation assumption", notesThatFit + (largeSummary ? 1 : 0)).ToArray() }
        }, new(130, (_, _, _) => { }));
        AssertCompleteTablesAndFinalSummary(report);
        Assert.Equal(baseline.Pages.SelectMany((p, page) => p.Tables.Select(f => (page, f.Table, f.Bounds, Rows: f.Rows.Count))),
            report.Pages.SelectMany((p, page) => p.Tables.Select(f => (page, f.Table, f.Bounds, Rows: f.Rows.Count))));
        int lastTablePage = report.Pages.Select((p, i) => (p, i)).Last(x => x.p.Tables.Count > 0).i;
        Assert.Equal(largeSummary ? lastTablePage + 1 : lastTablePage, report.Pages.Count - 1);
    }

    [Fact]
    public void EmptyStreamProducesSummaryAndImpossibleContentFailsWithoutLooping()
    {
        var source = SyntheticReportData.Create();
        using var empty = new ReportDocument(source with { Tables = [new("Empty", ["Name"], [1], [])] }, new(130, (_, _, _) => { }));
        AssertCompleteTablesAndFinalSummary(empty);
        Assert.All(empty.Pages, p => Assert.Empty(p.Tables));
        var impossible = new ReportTable("Table", [string.Join("\n", Enumerable.Repeat("Header", 100))], [1], [new[] { "Row" }]);
        Assert.Throws<InvalidOperationException>(() => new ReportDocument(source with { Tables = [impossible] }, new(130, (_, _, _) => { })));
        Assert.Throws<InvalidOperationException>(() => new ReportDocument(source with
        {
            Tables = [], Labels = source.Labels with { Assumptions = Enumerable.Repeat("Note", 100).ToArray() }
        }, new(130, (_, _, _) => { })));
    }

    [Fact]
    public void EmptyTablesAndManyRowsPaginateWithNoLostContentOrFooterOverlap()
    {
        var source = SyntheticReportData.Create(true);
        var tables = source.Tables.Select(t => t with { Rows = Enumerable.Range(0, 15).SelectMany(_ => t.Rows).ToArray() }).ToArray();
        using var report = new ReportDocument(source with { Tables = [tables[0], new("Empty", ["Name"], [1], []), .. tables.Skip(1)] },
            new(190, (_, _, _) => { }));
        Assert.True(report.Pages.Count > 2);
        for (int table = 0; table < report.Data.Tables.Count; table++)
        {
            var rows = report.Pages.SelectMany(p => p.Tables).Where(f => f.Table == table).SelectMany(f => f.Rows).Select(r => r.Row);
            Assert.Equal(Enumerable.Range(0, report.Data.Tables[table].Rows.Count), rows);
        }
        Assert.Single(report.Pages, p => p.SummaryTop is not null);
        for (int page = 0; page < report.Pages.Count; page++)
        { using var bitmap = report.RenderPage(page, 96); AssertTextContained(report); AssertTableHeadersAndBounds(report, page); }
    }

    [Fact]
    public void LongTextAndOversizedRowsAreWrappedAndCompletelyConsumed()
    {
        var source = SyntheticReportData.Create();
        string name = string.Concat(Enumerable.Repeat("UngetrennterSehrLangerName", 200));
        var data = source with
        {
            Metadata = [("Titel", string.Join(" ", Enumerable.Repeat("Sehr langer Berechnungstitel", 80)))],
            Tables = [new("Long table", ["Name", "Value"], [.5f, .5f], [new[] { name, "42" }, new[] { "Last row", "7" }])]
        };
        using var report = new ReportDocument(data, new(130, (_, _, _) => { }));
        var slices = report.Pages.SelectMany(p => p.Tables).SelectMany(f => f.Rows).Where(r => r.Row == 0).ToArray();
        Assert.True(slices.Length > 1);
        AssertCompleteTablesAndFinalSummary(report);
        Assert.Equal(name, string.Concat(slices.SelectMany(r => r.Cells[0].Skip(r.FirstLine).Take(r.LineCount))));
        for (int page = 0; page < report.Pages.Count; page++)
        { using var bitmap = report.RenderPage(page, 96); AssertTextContained(report); }
    }
}
