using SpanDraft.Reporting;
using SpanDraft.Reporting.Notation;

namespace SpanDraft.Tests;

// Fixed composer input: pagination does not need a solver, ViewModel or Avalonia visuals.
internal static class SyntheticReportData
{
    internal static ReportData Create(bool overflow = false)
    {
        var data = new ReportData(
            Title: "Einfach gelagerter Balken",
            Author: "Technik",
            Version: "1.1.0",
            CreatedAt: new DateTimeOffset(2026, 10, 23, 12, 34, 56, TimeSpan.FromHours(2)),
            Date: "23.10.2026",
            Metadata: [("Titel", "Einfach gelagerter Balken"), ("Projekt", "Neutrales Beispiel"), ("Referenz", "BEISPIEL-01"), ("Erstellt von", "Technik")],
            Section: "Rechteckrohr 60 × 100 × 4 mm (r = 6 mm)",
            SectionProperties: [new TechnicalText(new TechnicalRun("I", TechnicalPosition.Normal, TechnicalRole.Variable), new TechnicalRun("y", TechnicalPosition.Subscript, TechnicalRole.Variable), new TechnicalRun(" = "), new TechnicalRun("1,56\u00a0·\u00a010⁶ mm⁴")), new TechnicalText(new TechnicalRun("W", TechnicalPosition.Normal, TechnicalRole.Variable), new TechnicalRun("y", TechnicalPosition.Subscript, TechnicalRole.Variable), new TechnicalRun(" = "), new TechnicalRun("31\u202f200 mm³"))],
            Material: "S235JR",
            MaterialProperties: [new TechnicalText(new TechnicalRun("Elastizitätsmodul "), new TechnicalRun("E", TechnicalPosition.Normal, TechnicalRole.Variable), new TechnicalRun(" = "), new TechnicalRun("210 GPa")), new TechnicalText(new TechnicalRun("Streckgrenze "), new TechnicalRun("R", TechnicalPosition.Normal, TechnicalRole.Variable), new TechnicalRun("e", TechnicalPosition.Subscript, TechnicalRole.Text), new TechnicalRun(" = "), new TechnicalRun("235 MPa"))],
            Tables: [
            new("Lager", ["Name", "Typ", new TechnicalText(new TechnicalRun("x", TechnicalPosition.Normal, TechnicalRole.Variable), new TechnicalRun(" ["), new TechnicalRun("mm"), new TechnicalRun("]"))], [0.25f, 0.5f, 0.25f],
                [new[] { "A", "Festlager", "0" }, new[] { "B", "Loslager", "2\u202f000" }], LeftAlignedColumn: 1),
            new("Kräfte", ["Name", "Wert", new TechnicalText(new TechnicalRun("x", TechnicalPosition.Normal, TechnicalRole.Variable), new TechnicalRun(" ["), new TechnicalRun("mm"), new TechnicalRun("]"))], [0.18f, 0.47f, 0.35f],
                [new[] { "F1", "-1\u202f000 N", "800" }]),
            new("Streckenlasten", ["Name", "Wert", new TechnicalText(new TechnicalRun("x", TechnicalPosition.Normal, TechnicalRole.Variable), new TechnicalRun(" ["), new TechnicalRun("mm"), new TechnicalRun("]"))], [0.18f, 0.47f, 0.35f],
                [new[] { "q1", "-500 N/m", "200…1\u202f800" }]),
            new("Reaktionen", ["Lager", new TechnicalText(new TechnicalRun("R", TechnicalPosition.Normal, TechnicalRole.Variable), new TechnicalRun("x", TechnicalPosition.Subscript, TechnicalRole.Variable), new TechnicalRun(" ["), new TechnicalRun("N"), new TechnicalRun("]")), new TechnicalText(new TechnicalRun("R", TechnicalPosition.Normal, TechnicalRole.Variable), new TechnicalRun("y", TechnicalPosition.Subscript, TechnicalRole.Variable), new TechnicalRun(" ["), new TechnicalRun("N"), new TechnicalRun("]")), new TechnicalText(new TechnicalRun("M", TechnicalPosition.Normal, TechnicalRole.Variable), new TechnicalRun(" ["), new TechnicalRun("N·m"), new TechnicalRun("]"))], [0.16f, 0.24f, 0.3f, 0.3f],
                [new[] { "A", "0", "1\u202f000", "-" }, new[] { "B", "-", "800", "-" }])
        ],
            Indicators: [
            new(new TechnicalText(new TechnicalRun("Max. Durchbiegung "), new TechnicalRun("|"), new TechnicalRun("w", TechnicalPosition.Normal, TechnicalRole.Variable), new TechnicalRun("|"), new TechnicalRun("max", TechnicalPosition.Subscript, TechnicalRole.Text)), "0,785 mm", "x = 963 mm"),
            new(new TechnicalText(new TechnicalRun("Max. Biegemoment "), new TechnicalRun("|"), new TechnicalRun("M", TechnicalPosition.Normal, TechnicalRole.Variable), new TechnicalRun("|"), new TechnicalRun("max", TechnicalPosition.Subscript, TechnicalRole.Text)), "710 N·m", "x = 800 mm (x−)"),
            new(new TechnicalText(new TechnicalRun("Max. elastische Biegespannung "), new TechnicalRun("σ", TechnicalPosition.Normal, TechnicalRole.Variable), new TechnicalRun("b,max", TechnicalPosition.Subscript, TechnicalRole.Text)), "22,8 MPa", null),
            new(new TechnicalText(new TechnicalRun("Sicherheitsfaktor "), new TechnicalRun("S", TechnicalPosition.Normal, TechnicalRole.Variable), new TechnicalRun(" = "), new TechnicalRun("R", TechnicalPosition.Normal, TechnicalRole.Variable), new TechnicalRun("e", TechnicalPosition.Subscript, TechnicalRole.Text), new TechnicalRun(" / "), new TechnicalRun("σ", TechnicalPosition.Normal, TechnicalRole.Variable), new TechnicalRun("b,max", TechnicalPosition.Subscript, TechnicalRole.Text)), "10,3", null)
        ],
            Labels: new("BERECHNUNGSBERICHT", "Fortsetzung", "Seite {0} von {1}", "Querschnitt", "Material", "Maßgebende Kennwerte", "Berechnungsmodell", ["Berechnung nach Euler-Bernoulli, linear-elastisch und für kleine Verformungen. Werkstoff und Querschnitt sind über die Balkenlänge konstant; Schubverformungen werden vernachlässigt."], "Titel", "Projekt", "Referenz", "Erstellt von"));
        return overflow ? data with
        {
            Title = "Mehrfach gelagerter Balken",
            Metadata = [("Titel", "Mehrfach gelagerter Balken"), ("Projekt", "Neutrales Beispiel"), ("Referenz", "BEISPIEL-01"), ("Erstellt von", "Technik")],
            Tables = [
            new("Lager", ["Name", "Typ", new TechnicalText(new TechnicalRun("x", TechnicalPosition.Normal, TechnicalRole.Variable), new TechnicalRun(" ["), new TechnicalRun("mm"), new TechnicalRun("]"))], [0.25f, 0.5f, 0.25f],
                [new[] { "A", "Festlager", "0" }, new[] { "B", "Loslager", "1\u202f000" }, new[] { "C", "Loslager", "2\u202f000" }, new[] { "D", "Loslager", "3\u202f000" }, new[] { "E", "Loslager", "4\u202f000" }, new[] { "F", "Loslager", "5\u202f000" }, new[] { "G", "Loslager", "6\u202f000" }, new[] { "H", "Loslager", "7\u202f000" }], LeftAlignedColumn: 1),
            new("Kräfte", ["Name", "Wert", new TechnicalText(new TechnicalRun("x", TechnicalPosition.Normal, TechnicalRole.Variable), new TechnicalRun(" ["), new TechnicalRun("mm"), new TechnicalRun("]"))], [0.18f, 0.47f, 0.35f],
                [new[] { "F1", "-4\u202f000 N", "350" }, new[] { "F2", "-5\u202f000 N", "1\u202f000" }, new[] { "F3", "-6\u202f000 N", "1\u202f650" }, new[] { "F4", "-7\u202f000 N", "2\u202f300" }, new[] { "F5", "-8\u202f000 N", "2\u202f950" }, new[] { "F6", "-9\u202f000 N", "3\u202f600" }, new[] { "F7", "-10\u202f000 N", "4\u202f250" }, new[] { "F8", "-11\u202f000 N", "4\u202f900" }, new[] { "F9", "-12\u202f000 N", "5\u202f550" }, new[] { "F10", "-13\u202f000 N", "6\u202f200" }]),
            new("Momente", ["Name", "Wert", new TechnicalText(new TechnicalRun("x", TechnicalPosition.Normal, TechnicalRole.Variable), new TechnicalRun(" ["), new TechnicalRun("mm"), new TechnicalRun("]"))], [0.18f, 0.47f, 0.35f],
                [new[] { "M1", "2\u202f500 N·m", "3\u202f200" }]),
            new("Streckenlasten", ["Name", "Wert", new TechnicalText(new TechnicalRun("x", TechnicalPosition.Normal, TechnicalRole.Variable), new TechnicalRun(" ["), new TechnicalRun("mm"), new TechnicalRun("]"))], [0.18f, 0.47f, 0.35f],
                [new[] { "q1", "-3\u202f000 N/m", "200…2\u202f400" }, new[] { "q2", "-5\u202f000 N/m", "3\u202f600…6\u202f800" }]),
            new("Reaktionen", ["Lager", new TechnicalText(new TechnicalRun("R", TechnicalPosition.Normal, TechnicalRole.Variable), new TechnicalRun("x", TechnicalPosition.Subscript, TechnicalRole.Variable), new TechnicalRun(" ["), new TechnicalRun("N"), new TechnicalRun("]")), new TechnicalText(new TechnicalRun("R", TechnicalPosition.Normal, TechnicalRole.Variable), new TechnicalRun("y", TechnicalPosition.Subscript, TechnicalRole.Variable), new TechnicalRun(" ["), new TechnicalRun("N"), new TechnicalRun("]")), new TechnicalText(new TechnicalRun("M", TechnicalPosition.Normal, TechnicalRole.Variable), new TechnicalRun(" ["), new TechnicalRun("N·m"), new TechnicalRun("]"))], [0.16f, 0.24f, 0.3f, 0.3f],
                [new[] { "A", "0", "2\u202f750", "-" }, new[] { "B", "-", "11\u202f800", "-" }, new[] { "C", "-", "13\u202f000", "-" }, new[] { "D", "-", "14\u202f600", "-" }, new[] { "E", "-", "15\u202f900", "-" }, new[] { "F", "-", "23\u202f100", "-" }, new[] { "G", "-", "24\u202f500", "-" }, new[] { "H", "-", "1\u202f980", "-" }])
        ],
            Indicators = [
            new(new TechnicalText(new TechnicalRun("Max. Durchbiegung "), new TechnicalRun("|"), new TechnicalRun("w", TechnicalPosition.Normal, TechnicalRole.Variable), new TechnicalRun("|"), new TechnicalRun("max", TechnicalPosition.Subscript, TechnicalRole.Text)), "0,244 mm", "x = 4\u202f430 mm"),
            new(new TechnicalText(new TechnicalRun("Max. Biegemoment "), new TechnicalRun("|"), new TechnicalRun("M", TechnicalPosition.Normal, TechnicalRole.Variable), new TechnicalRun("|"), new TechnicalRun("max", TechnicalPosition.Subscript, TechnicalRole.Text)), "2\u202f220 N·m", "x = 6\u202f000 mm (x−)"),
            new(new TechnicalText(new TechnicalRun("Max. elastische Biegespannung "), new TechnicalRun("σ", TechnicalPosition.Normal, TechnicalRole.Variable), new TechnicalRun("b,max", TechnicalPosition.Subscript, TechnicalRole.Text)), "71,1 MPa", null),
            new(new TechnicalText(new TechnicalRun("Sicherheitsfaktor "), new TechnicalRun("S", TechnicalPosition.Normal, TechnicalRole.Variable), new TechnicalRun(" = "), new TechnicalRun("R", TechnicalPosition.Normal, TechnicalRole.Variable), new TechnicalRun("e", TechnicalPosition.Subscript, TechnicalRole.Text), new TechnicalRun(" / "), new TechnicalRun("σ", TechnicalPosition.Normal, TechnicalRole.Variable), new TechnicalRun("b,max", TechnicalPosition.Subscript, TechnicalRole.Text)), "3,31", null)
        ]
        } : data;
    }
}
