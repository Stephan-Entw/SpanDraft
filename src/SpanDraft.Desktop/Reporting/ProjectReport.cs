using System.Globalization;
using Avalonia.Threading;
using SpanDraft.Core.Sections;
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
        string Quantity(double si, QuantityKind kind) => QuantityFormatter.Format(si, kind, profile,
            presentation.Options.Mode, kind == QuantityKind.SafetyFactor ? null : presentation.References);
        string Position(double meters) => InputQuantityFormatter.Display(meters, profile[QuantityKind.BeamLength]);
        string Location(double meters, SpanDraft.Solver.EvaluationSide? side) =>
            string.Format(CultureInfo.CurrentUICulture, Strings.ReportPosition, Position(meters),
                profile[QuantityKind.BeamLength].Symbol, side is null ? "" : side == SpanDraft.Solver.EvaluationSide.Left ? " (x−)" : " (x+)");
        var metadata = new List<(string Label, string Value)>();
        if (state.Info is { } info)
        {
            metadata.Add((Strings.CalculationTitle, info.CalculationTitle));
            if (info.ProjectTitle is { } project) metadata.Add((Strings.Project, project));
            if (info.Reference is { } reference) metadata.Add((Strings.Reference, reference));
            metadata.Add((Strings.Author, info.Author));
        }
        var axis = analysis.Beam.BendingAxisProperties;
        string axisName = state.Document.BendingAxis.ToString().ToLowerInvariant();
        var sectionProperties = new List<TechnicalText>
        { TechnicalSymbols.Inertia(axisName) + " = " + Quantity(axis.SecondMomentOfArea.MetersToTheFourth, QuantityKind.SecondMomentOfArea) };
        if (Quantity(axis.PositiveSectionModulus.CubicMeters, QuantityKind.SectionModulus)
            == Quantity(axis.NegativeSectionModulus.CubicMeters, QuantityKind.SectionModulus))
            sectionProperties.Add(TechnicalSymbols.Modulus(axisName) + " = " + Quantity(axis.PositiveSectionModulus.CubicMeters, QuantityKind.SectionModulus));
        else
        {
            sectionProperties.Add(TechnicalSymbols.Modulus(axisName, 1) + " = " + Quantity(axis.PositiveSectionModulus.CubicMeters, QuantityKind.SectionModulus));
            sectionProperties.Add(TechnicalSymbols.Modulus(axisName, -1) + " = " + Quantity(axis.NegativeSectionModulus.CubicMeters, QuantityKind.SectionModulus));
        }
        var engineering = analysis.Engineering;
        var timestamp = createdAt ?? DateTimeOffset.Now;
        var data = new ReportData(state.Info?.CalculationTitle ?? Strings.ReportHeading, state.Info?.Author ?? "",
            ApplicationMetadata.Version, timestamp, timestamp.ToLocalTime().ToString("d", CultureInfo.CurrentCulture), metadata,
            (state.Document.SectionDisplayName ?? ShapeName(state.Document.Section)) + " · " + axisName + "-" + axisName, sectionProperties, overview.Material,
            [TechnicalSymbols.YoungsModulus + " = " + InputQuantityFormatter.WithUnit(state.Document.Material.YoungsModulus.Pascals, profile[QuantityKind.Stress]),
             MaterialStrengthDisplay.LabelText(state.Document.Material.StrengthType) + " = " + InputQuantityFormatter.WithUnit(state.Document.Material.YieldStrength.Pascals, profile[QuantityKind.Stress])],
            [
                new(Strings.Supports, [Strings.OverviewName, Strings.OverviewType, TechnicalSymbols.Axis("x") + " [" + profile[QuantityKind.BeamLength].Symbol + "]"], [.17f, .48f, .35f],
                    overview.Supports.Select(s => (IReadOnlyList<string>)new[] { s.Name, s.Type, s.Position }).ToArray()),
                new(Strings.Loads, [Strings.OverviewName, Strings.OverviewValue, TechnicalSymbols.Axis("x") + " [" + profile[QuantityKind.BeamLength].Symbol + "]"], [.16f, .42f, .42f],
                    overview.Loads.Select(l => (IReadOnlyList<string>)new[] { l.Name, l.Value, l.Position }).ToArray()),
                new(Strings.Reactions, [Strings.Support, overview.ReactionXHeaderText, overview.ReactionYHeaderText, overview.ReactionMomentHeaderText],
                    [.16f, .24f, .30f, .30f], overview.Reactions.Select(r => (IReadOnlyList<string>)new[]
                    { r.Name, Missing(r.Rx), Missing(r.Ry), Missing(r.Moment) }).ToArray())
            ],
            [
                new(Strings.ReportDeflection + " " + TechnicalSymbols.MaximumDisplacement, presentation.Displacement,
                    Location(engineering.CriticalTransverseDisplacement.Position.Meters, engineering.CriticalTransverseDisplacement.Side)),
                new(Strings.ReportMoment + " " + TechnicalSymbols.MaximumMoment, presentation.Moment,
                    Location(engineering.CriticalBendingMoment.Position.Meters, engineering.CriticalBendingMoment.Side)),
                new(Strings.ReportStress + " " + TechnicalSymbols.MaximumStress, presentation.Stress, null),
                new(Strings.ReportSafety + " " + TechnicalSymbols.SafetyFactor + " = " + strengthSymbol + " / " + TechnicalSymbols.MaximumStress, presentation.SafetyFactor, null)
            ], new(Strings.ReportHeading, Strings.ReportContinuation, Strings.ReportPage, Strings.Section, Strings.ReportMaterial,
                Strings.ReportSummary, Strings.ReportNotes, [Strings.ReportTheory, Strings.ReportLimits]));
        var visuals = new ReportVisuals(state, presentation, data);
        var report = new ReportDocument(data, visuals.Graphics);
        try { visuals.Freeze(report); return report; }
        catch { report.Dispose(); throw; }
    }

    private static string ShapeName(ISectionDefinition section) => SectionDefinitionViewModel.FormOf(
        SectionDefinitionCompatibility.Normalize(section)) switch
    {
        SectionEditorType.Rectangle => Strings.ShapeRectangle,
        SectionEditorType.RectangularHollow => Strings.ShapeRectangularHollow,
        SectionEditorType.Circle => Strings.ShapeCircle,
        SectionEditorType.CircularHollow => Strings.ShapeCircularHollow,
        SectionEditorType.ISection => Strings.SectionDefinitionShapeI,
        SectionEditorType.USection => Strings.ShapeU,
        SectionEditorType.TSection => Strings.ShapeT,
        SectionEditorType.Angle => Strings.ShapeAngle,
        _ => Strings.SectionDefinitionCustomName
    };

    private static string Missing(string value) => value.Length == 0 ? "-" : value;
}
