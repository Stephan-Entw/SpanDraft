using SpanDraft.Core.Supports;
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
        if (state.Document.Segments.Any(segment => segment.Material.StrengthType is null))
            throw new InvalidOperationException(Strings.MaterialStrengthResolve);
        var presentation = editor.Presentation;
        var overview = ProjectOverviewState.From(state.Document, presentation);
        var analysis = presentation.Result!;
        // The section and material blocks and the stress indicators describe the governing segment, which is the only one for
        // a beam of one segment. Several segments are listed in their own table.
        bool several = state.Document.HasSeveralSegments;
        int governing = analysis.Engineering.GoverningSegmentIndex;
        var governingSegment = state.Document.Segments[governing];
        var strengthSymbol = MaterialStrengthDisplay.SymbolText(governingSegment.Material.StrengthType);
        var profile = presentation.Options.Profile;
        // Given: material values and stored section properties. Result: section properties derived
        // from geometry and the calculated positions of the critical values.
        TechnicalText Given(TechnicalText label, double si, QuantityKind kind) =>
            label + QuantityFormatter.FormatInputEquation("", si, profile[kind], presentation.Options);
        bool givenSection = SectionDefinitionCompatibility.Normalize(governingSegment.Section) is SpanDraft.Core.Sections.ManualSectionDefinition;
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
        var axis = analysis.Beam.Segments[governing].BendingAxisProperties;
        string axisName = governingSegment.BendingAxis.ToString().ToLowerInvariant();
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
        var indicators = new List<ReportIndicator>
        {
                new(Strings.ReportDeflection + " " + TechnicalSymbols.MaximumDisplacement, presentation.Displacement,
                    Location(engineering.CriticalTransverseDisplacement.Position.Meters, engineering.CriticalTransverseDisplacement.Side)),
                new(Strings.ReportMoment + " " + TechnicalSymbols.MaximumMoment, presentation.Moment,
                    Location(engineering.CriticalBendingMoment.Position.Meters, engineering.CriticalBendingMoment.Side)),
                new(Strings.ReportStress + " " + TechnicalSymbols.MaximumStress, presentation.Stress,
                    several ? Location(engineering.BendingStressPosition.Meters, engineering.BendingStressSide) : null),
                new(Strings.ReportSafety + " " + TechnicalSymbols.SafetyFactor + " = " + strengthSymbol + " / " + TechnicalSymbols.MaximumStress, presentation.SafetyFactor, null)
        };
        if (several)
            indicators.Add(new(Strings.ReportGoverningSegment, (governing + 1).ToString(CultureInfo.CurrentUICulture), null));
        var timestamp = createdAt ?? DateTimeOffset.Now;
        var data = new ReportData(state.Info?.CalculationTitle ?? Strings.ReportHeading, state.Info?.Author ?? "",
            ApplicationMetadata.Version, timestamp, timestamp.ToLocalTime().ToString("d", CultureInfo.CurrentCulture), metadata,
            overview.Segments[governing].Section, sectionProperties, overview.Segments[governing].Material,
            [Given(Strings.MaterialYoungsModulus + " " + TechnicalSymbols.YoungsModulus, governingSegment.Material.YoungsModulus.Pascals, QuantityKind.YoungsModulus),
             Given(MaterialStrengthDisplay.LabelText(governingSegment.Material.StrengthType), governingSegment.Material.YieldStrength.Pascals, QuantityKind.MaterialStrength)],
            new ReportTable?[]
            {
                several ? SegmentTable(overview, profile) : null,
                new(Strings.Supports, [Strings.OverviewName, Strings.OverviewType, TechnicalSymbols.Axis("x") + " [" + profile[QuantityKind.BeamLength].Symbol + "]"], [.25f, .50f, .25f],
                    overview.Supports.Select(s => (IReadOnlyList<string>)new[] { s.Name, s.Type, s.Position }).ToArray(), LeftAlignedColumn: 1),
                new(Strings.MomentReleases, [Strings.OverviewName, TechnicalSymbols.Axis("x") + " [" + profile[QuantityKind.BeamLength].Symbol + "]",
                        overview.RotationLeftHeaderText, overview.RotationRightHeaderText, overview.ReleaseShearHeaderText],
                    [.14f, .18f, .22f, .22f, .24f], overview.MomentReleases.Zip(overview.MomentReleaseResults, (release, result) =>
                        (IReadOnlyList<string>)new[] { release.Name, release.Position, result.RotationLeft, result.RotationRight, result.Shear }).ToArray()),
                new(Strings.Forces, [Strings.OverviewName, Strings.OverviewValue, TechnicalSymbols.Axis("x") + " [" + profile[QuantityKind.BeamLength].Symbol + "]"], [.18f, .47f, .35f],
                    overview.Forces.Select(l => (IReadOnlyList<string>)new[] { l.Name, l.Value, l.Position }).ToArray()),
                new(Strings.Moments, [Strings.OverviewName, Strings.OverviewValue, TechnicalSymbols.Axis("x") + " [" + profile[QuantityKind.BeamLength].Symbol + "]"], [.18f, .47f, .35f],
                    overview.Moments.Select(l => (IReadOnlyList<string>)new[] { l.Name, l.Value, l.Position }).ToArray()),
                new(Strings.DistributedLoads, [Strings.OverviewName, Strings.OverviewValue, TechnicalSymbols.Axis("x") + " [" + profile[QuantityKind.BeamLength].Symbol + "]"], [.16f, .52f, .32f],
                    overview.DistributedLoads.Select(l => (IReadOnlyList<string>)new[] { l.Name, l.Value, l.Position }).ToArray()),
                new(Strings.Reactions, [Strings.Support, overview.ReactionXHeaderText, overview.ReactionYHeaderText, overview.ReactionMomentHeaderText],
                    [.16f, .24f, .30f, .30f], overview.Reactions.Select(r => (IReadOnlyList<string>)new[]
                    { r.Name, Missing(r.Rx), Missing(r.Ry), Missing(r.Moment) }).ToArray())
            }.Where(t => t is { Rows.Count: > 0 }).Select(t => t!).ToArray(),
            indicators.ToArray(), new(Strings.ReportHeading.ToUpperInvariant(), Strings.ReportContinuation, Strings.ReportPage,
                several ? string.Format(CultureInfo.CurrentUICulture, Strings.ReportSegmentSection, governing + 1) : Strings.Section,
                several ? string.Format(CultureInfo.CurrentUICulture, Strings.ReportSegmentMaterial, governing + 1) : Strings.ReportMaterial,
                Strings.ReportSummary, Strings.ReportNotes, [(several ? Strings.ReportSegmentTheory : Strings.ReportTheory) + (state.Document.Supports.Any(s => s.Type == SupportType.Spring) ? " " + Strings.ReportSpringTheory : "")
                    + (state.Document.MomentReleases.Count > 0 ? " " + Strings.ReportMomentReleaseTheory : "")
                    + " " + RoundingSentence(presentation.Options.SignificantDigits)],
                Strings.CalculationTitle, Strings.Project, Strings.Reference, Strings.Author));
        var visuals = new ReportVisuals(state, presentation, data);
        var report = new ReportDocument(data, visuals.Graphics);
        try { visuals.Freeze(report); return report; }
        catch { report.Dispose(); throw; }
    }

    // One row per segment: number, range, section and material. The table sits in the narrow column beside the drawings,
    // so the second moment of area and the section modulus stay with the governing segment's block.
    private static ReportTable SegmentTable(ProjectOverviewState overview, UnitProfile profile) => new(Strings.SetupSegments,
        [Strings.SegmentNumber, TechnicalSymbols.Axis("x") + " [" + profile[QuantityKind.BeamLength].Symbol + "]",
            Strings.Section, Strings.Material],
        [.12f, .31f, .27f, .30f], overview.Segments.Select(segment => (IReadOnlyList<string>)new[]
            { segment.Number.ToString(CultureInfo.CurrentUICulture), segment.Range, segment.Section, segment.Material }).ToArray(),
        LeftAlignedColumn: 2);

    private static string Missing(string value) => value.Length == 0 ? "-" : value;

    /// <summary>The sentence appended to the calculation-model paragraph that states the display rounding.</summary>
    internal static string RoundingSentence(int significantDigits) =>
        string.Format(CultureInfo.CurrentUICulture, Strings.ReportRounding, SettingsViewModel.SignificantDigitLabel(significantDigits));
}
