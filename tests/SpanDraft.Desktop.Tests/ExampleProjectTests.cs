using System.Globalization;
using SpanDraft.Analysis;
using SpanDraft.Desktop.Examples;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.State;
using SpanDraft.Engineering;
using SpanDraft.Solver;
using Xunit;

namespace SpanDraft.Tests;

/// <summary>
/// The shipped examples as the application embeds them (no fixtures): loadable in both languages, mechanically identical,
/// solvable, and in agreement with closed-form references computed here from the published parameters only.
/// </summary>
public sealed class ExampleProjectTests
{
    private const double G0 = 9.80665;

    public static TheoryData<string, string> Files() => new()
    {
        { "einfeldtraeger", "de" }, { "einfeldtraeger", "en" },
        { "kragarm", "de" }, { "kragarm", "en" },
        { "abgesetzte-welle", "de" }, { "abgesetzte-welle", "en" },
    };

    private static ProjectState Read(string id, string language)
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language == "de" ? "de-DE" : "en-US");
            var bytes = new EmbeddedExampleProjectSource().ReadAsync(id).GetAwaiter().GetResult();
            Assert.NotNull(bytes);
            return ProjectFileCodec.Deserialize(bytes);
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }

    private static BeamAnalysisResult Analyze(ProjectState state)
    {
        var outcome = BeamAnalysis.Analyze(state.Document.ToBeamModel());
        Assert.True(outcome.IsSuccess, outcome.Failure?.ToString());
        return outcome.Result!;
    }

    private static void Near(double expected, double actual, double relative = 1e-6) =>
        Assert.True(Math.Abs(actual - expected) <= relative * Math.Abs(expected), $"Expected {expected}, actual {actual}.");

    private static BeamNodeResult Node(BeamAnalysisResult result, double position) =>
        Assert.Single(result.Solution.Nodes, n => Math.Abs(n.Position.Meters - position) < 1e-12 && n.ReactionY is not null);

    [Fact]
    public void EveryCatalogExampleIsEmbeddedInBothLanguagesAndNothingElse()
    {
        var names = typeof(EmbeddedExampleProjectSource).Assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith("SpanDraft.Desktop.Examples.", StringComparison.Ordinal)).Order(StringComparer.Ordinal);
        var expected = ExampleCatalog.All.SelectMany(e => new[] { "de", "en" }.Select(l => $"SpanDraft.Desktop.Examples.{e.Id}.{l}.spandraft"))
            .Order(StringComparer.Ordinal);
        Assert.Equal(expected, names);
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void EachEmbeddedExampleLoadsWithLocalizedInformationAndSolves(string id, string language)
    {
        var state = Read(id, language);
        Assert.NotNull(state.Info);
        Assert.Equal(language == "de" ? "SpanDraft-Beispiele" : "SpanDraft examples", state.Info.ProjectTitle);
        Assert.StartsWith(language == "de" ? "Hilfe: " : "Help: ", state.Info.Reference);
        Assert.Null(state.Info.Author);
        Analyze(state);
    }

    [Theory]
    [InlineData("einfeldtraeger")]
    [InlineData("kragarm")]
    [InlineData("abgesetzte-welle")]
    public void TheGermanAndEnglishExampleAreMechanicallyIdentical(string id)
    {
        var de = Read(id, "de");
        var en = Read(id, "en");
        Assert.NotEqual(de.Info, en.Info);
        Assert.Equal(ProjectFileCodec.Serialize(de with { Info = null }), ProjectFileCodec.Serialize(en with { Info = null }));
    }

    [Theory]
    [InlineData("de")]
    [InlineData("en")]
    public void TheSimplySupportedBeamMatchesTheHandFormulas(string language)
    {
        // L = 2 m, F = 1 kN at L/2, S235JR (E = 210 GPa, Re = 235 MPa), rectangle 40 × 80 mm about y-y.
        const double l = 2, f = 1000, e = 210e9, b = 0.04, h = 0.08, re = 235e6;
        double i = b * h * h * h / 12, w = b * h * h / 6;
        var result = Analyze(Read("einfeldtraeger", language));
        Near(f / 2, Node(result, 0).ReactionY!.Value.Newtons);
        Near(f / 2, Node(result, l).ReactionY!.Value.Newtons);
        var engineering = result.Engineering;
        Near(f * l / 4, engineering.BendingMomentMagnitude.NewtonMeters);                       // 500 Nm
        Near(f * l * l * l / (48 * e * i), engineering.TransverseDisplacementMagnitude.Meters);  // 0.465 mm
        Near(l / 2, engineering.CriticalTransverseDisplacement.Position.Meters);
        Near(f * l / 4 / w, engineering.MaximumBendingStress.Pascals);                          // 11.72 N/mm²
        Near(re / (f * l / 4 / w), engineering.SafetyFactor);
    }

    [Theory]
    [InlineData("de")]
    [InlineData("en")]
    public void TheCantileverWithSelfWeightMatchesTheHandFormulas(string language)
    {
        // L = 1.5 m, q = 2 kN/m over the whole length plus self-weight g = ρ·A·g0, S235JR (ρ = 7850 kg/m³), 40 × 80 mm.
        const double l = 1.5, q = 2000, e = 210e9, b = 0.04, h = 0.08, rho = 7850, re = 235e6;
        double i = b * h * h * h / 12, w = b * h * h / 6, g = rho * b * h * G0, total = q + g;
        var result = Analyze(Read("kragarm", language));
        var support = Node(result, 0);
        Near(total * l, support.ReactionY!.Value.Newtons);                                  // 3369.5 N
        Near(total * l * l / 2, Math.Abs(support.ReactionMoment!.Value.NewtonMeters));       // 2527.1 Nm
        var engineering = result.Engineering;
        Near(total * l * l / 2, engineering.BendingMomentMagnitude.NewtonMeters);
        Assert.Equal(0, engineering.CriticalBendingMoment.Position.Meters, 12);
        Near(total * l * l * l * l / (8 * e * i), engineering.TransverseDisplacementMagnitude.Meters); // 3.97 mm
        Near(l, engineering.CriticalTransverseDisplacement.Position.Meters);
        Near(total * l * l / 2 / w, engineering.MaximumBendingStress.Pascals);               // 59.2 N/mm²
        Near(re / (total * l * l / 2 / w), engineering.SafetyFactor);
    }

    [Theory]
    [InlineData("de")]
    [InlineData("en")]
    public void TheSteppedShaftMatchesTheLeverRuleAndItsGoverningStep(string language)
    {
        // Bearings at x = 0 and x = 0.4 m, F = 5 kN at a = 0.15 m; Ø30 (0–0.1 m), Ø40 (0.1–0.3 m), Ø30 (0.3–0.4 m);
        // 42CrMo4 +QT with Rp0.2 = 650 MPa.
        const double l = 0.4, f = 5000, a = 0.15, step = 0.1, rp = 650e6;
        double ra = f * (l - a) / l, rb = f * a / l;
        static double W(double d) => Math.PI * d * d * d / 32;
        double atStep = ra * step / W(0.03), atLoad = ra * a / W(0.04);
        var result = Analyze(Read("abgesetzte-welle", language));
        Near(ra, Node(result, 0).ReactionY!.Value.Newtons);                                  // 3125 N
        Near(rb, Node(result, l).ReactionY!.Value.Newtons);                                  // 1875 N
        var engineering = result.Engineering;
        Near(ra * a, engineering.BendingMomentMagnitude.NewtonMeters);                       // 468.75 Nm
        Near(a, engineering.CriticalBendingMoment.Position.Meters);
        // The thinner shoulder governs although the moment there is smaller: 117.9 N/mm² > 74.6 N/mm².
        Assert.True(atStep > atLoad);
        Assert.Equal(0, engineering.GoverningSegmentIndex);
        Near(step, engineering.BendingStressPosition.Meters);
        Near(atStep, engineering.MaximumBendingStress.Pascals);
        Near(atLoad, engineering.SegmentAssessments[1].MaximumBendingStress.Pascals);
        Near(rp / atStep, engineering.SafetyFactor);
    }
}
