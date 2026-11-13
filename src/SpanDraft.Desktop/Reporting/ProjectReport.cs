using System.Globalization;
using Avalonia.Threading;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;
using SpanDraft.Reporting;
using SpanDraft.Reporting.Notation;

namespace SpanDraft.Desktop.Reporting;

internal static class ProjectReport
{
    internal static ReportDocument Capture(EditorViewModel editor, DateTimeOffset? createdAt = null)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (!editor.Presentation.IsSuccess) throw new InvalidOperationException(Strings.CalculationUnavailable);
        var state = editor.Session.CurrentRevision.State;
        if (state.Document.Material.StrengthType is null) throw new InvalidOperationException(Strings.MaterialStrengthResolve);
        var strengthSymbol = MaterialStrengthDisplay.SymbolText(state.Document.Material.StrengthType);
        var presentation = editor.Presentation;
        var overview = ProjectOverviewState.From(state.Document, presentation);
        var analysis = presentation.Result!;
        var profile = presentation.Options.Profile;
        // Given: material values and stored section properties. Result: section properties derived
        // from geometry and the calculated positions of the critical values.
        TechnicalText Given(TechnicalText label, double si, QuantityKind kind) =>
            label + QuantityFormatter.FormatInputEquation("", si, profile[kind], presentation.Options);
        bool givenSection = SectionDefinitionCompatibility.Normalize(state.Document.Section) is SpanDraft.Core.Sections.ManualSectionDefinition;
        string SectionProperty(double si, QuantityKind kind) => SectionDisplay.Property(givenSection, si, kind, presentation.Options);
        TechnicalText SectionValue(TechnicalText label, double si, QuantityKind kind) =>
            givenSection ? Given(label, si, kind) : label + " = " + SectionProperty(si, kind);
        string Position(double meters) => QuantityFormatter.FormatResultNumber(meters, QuantityKind.BeamLength, presentation.Options);
        string Location(double meters, SpanDraft.Solver.EvaluationSide? side) =>
            string.Format(CultureInfo.CurrentUICulture, Strings.ReportPosition, Position(meters),
                profile[QuantityKind.BeamLength].Symbol, side is null ? "" : side == SpanDraft.Solver.EvaluationSide.Left ? " (x−)" : " (x+)");
        var metadata = new List<(string Label, string Value)>();
        if (state.Info is { } info)
        {
            metadata.Add((Strings.CalculationTitle, info.CalculationTitle));
            if (info.ProjectTitle is { } project) metadata.Add((Strings.Project, project));
            if (info.Reference is { } reference) metadata.Add((Strings.Reference, reference));
            if (info.Author is { } author) metadata.Add((Strings.Author, author));
        }
        var axis = analysis.Beam.BendingAxisProperties;
        string axisName = state.Document.BendingAxis.ToString().ToLowerInvariant();
        var sectionProperties = new List<TechnicalText>
        { SectionValue(TechnicalSymbols.Inertia(axisName), axis.SecondMomentOfArea.MetersToTheFourth, QuantityKind.SecondMomentOfArea) };
        if (SectionProperty(axis.PositiveSectionModulus.CubicMeters, QuantityKind.SectionModulus)
            == SectionProperty(axis.NegativeSectionModulus.CubicMeters, QuantityKind.SectionModulus))
            sectionProperties.Add(SectionValue(TechnicalSymbols.Modulus(axisName), axis.PositiveSectionModulus.CubicMeters, QuantityKind.SectionModulus));
        else
        {
            sectionProperties.Add(SectionValue(TechnicalSymbols.Modulus(axisName, 1), axis.PositiveSectionModulus.CubicMeters, QuantityKind.SectionModulus));
            sectionProperties.Add(SectionValue(TechnicalSymbols.Modulus(axisName, -1), axis.NegativeSectionModulus.CubicMeters, QuantityKind.SectionModulus));
        }
        var engineering = analysis.Engineering;
        var timestamp = createdAt ?? DateTimeOffset.Now;
        var data = new ReportData(state.Info?.CalculationTitle ?? Strings.ReportHeading, state.Info?.Author ?? "",
            ApplicationMetadata.Version, timestamp, timestamp.ToLocalTime().ToString("d", CultureInfo.CurrentCulture), metadata,
            overview.Section, sectionProperties, overview.Material,
            [Given(Strings.MaterialYoungsModulus + " " + TechnicalSymbols.YoungsModulus, state.Document.Material.YoungsModulus.Pascals, QuantityKind.YoungsModulus),
             Given(MaterialStrengthDisplay.LabelText(state.Document.Material.StrengthType), state.Document.Material.YieldStrength.Pascals, QuantityKind.MaterialStrength)],
            new ReportTable[]
            {
                new(Strings.Supports, [Strings.OverviewName, Strings.OverviewType, TechnicalSymbols.Axis("x") + " [" + profile[QuantityKind.BeamLength].Symbol + "]"], [.25f, .50f, .25f],
                    overview.Supports.Select(s => (IReadOnlyList<string>)new[] { s.Name, s.Type, s.Position }).ToArray(), LeftAlignedColumn: 1),
                new(Strings.Forces, [Strings.OverviewName, Strings.OverviewValue, TechnicalSymbols.Axis("x") + " [" + profile[QuantityKind.BeamLength].Symbol + "]"], [.18f, .47f, .35f],
                    overview.Forces.Select(l => (IReadOnlyList<string>)new[] { l.Name, l.Value, l.Position }).ToArray()),
                new(Strings.Moments, [Strings.OverviewName, Strings.OverviewValue, TechnicalSymbols.Axis("x") + " [" + profile[QuantityKind.BeamLength].Symbol + "]"], [.18f, .47f, .35f],
                    overview.Moments.Select(l => (IReadOnlyList<string>)new[] { l.Name, l.Value, l.Position }).ToArray()),
                new(Strings.DistributedLoads, [Strings.OverviewName, Strings.OverviewValue, TechnicalSymbols.Axis("x") + " [" + profile[QuantityKind.BeamLength].Symbol + "]"], [.18f, .47f, .35f],
                    overview.DistributedLoads.Select(l => (IReadOnlyList<string>)new[] { l.Name, l.Value, l.Position }).ToArray()),
                new(Strings.Reactions, [Strings.Support, overview.ReactionXHeaderText, overview.ReactionYHeaderText, overview.ReactionMomentHeaderText],
                    [.16f, .24f, .30f, .30f], overview.Reactions.Select(r => (IReadOnlyList<string>)new[]
                    { r.Name, Missing(r.Rx), Missing(r.Ry), Missing(r.Moment) }).ToArray())
            }.Where(t => t.Rows.Count > 0).ToArray(),
            [
                new(Strings.ReportDeflection + " " + TechnicalSymbols.MaximumDisplacement, presentation.Displacement,
                    Location(engineering.CriticalTransverseDisplacement.Position.Meters, engineering.CriticalTransverseDisplacement.Side)),
                new(Strings.ReportMoment + " " + TechnicalSymbols.MaximumMoment, presentation.Moment,
                    Location(engineering.CriticalBendingMoment.Position.Meters, engineering.CriticalBendingMoment.Side)),
                new(Strings.ReportStress + " " + TechnicalSymbols.MaximumStress, presentation.Stress, null),
                new(Strings.ReportSafety + " " + TechnicalSymbols.SafetyFactor + " = " + strengthSymbol + " / " + TechnicalSymbols.MaximumStress, presentation.SafetyFactor, null)
            ], new(Strings.ReportHeading.ToUpperInvariant(), Strings.ReportContinuation, Strings.ReportPage, Strings.Section, Strings.ReportMaterial,
                Strings.ReportSummary, Strings.ReportNotes, [Strings.ReportTheory + " " + RoundingSentence(presentation.Options.SignificantDigits)],
                Strings.CalculationTitle, Strings.Project, Strings.Reference, Strings.Author),
            MaterialNoteDisplay.Resolve(state.Document.Material.Note, CultureInfo.CurrentUICulture) is { } note
                ? Strings.MaterialNote + ": " + note : null);
        var visuals = new ReportVisuals(state, presentation, data);
        var report = new ReportDocument(data, visuals.Graphics);
        try { visuals.Freeze(report); return report; }
        catch { report.Dispose(); throw; }
    }

    private static string Missing(string value) => value.Length == 0 ? "-" : value;

    /// <summary>The sentence appended to the calculation-model paragraph that states the display rounding.</summary>
    internal static string RoundingSentence(int significantDigits) =>
        string.Format(CultureInfo.CurrentUICulture, Strings.ReportRounding, SettingsViewModel.SignificantDigitLabel(significantDigits));
}
