using System.Globalization;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Resources;
using SpanDraft.Reporting.Notation;

namespace SpanDraft.Desktop.State;

/// <summary>One beam segment of the overview: its range in the display length unit, section, material and bending axis.</summary>
public sealed record ProjectOverviewSegment(int Number, string Range, string Section, string Material, string Axis);

public sealed record ProjectOverviewItem(Guid Id, string Name, string Type, string Value, string Position);
/// <summary>
/// One reaction row per support station. <see cref="Id"/> identifies the row's single support; a row
/// shared by several supports at one station represents no single editable entity and has none.
/// </summary>
public sealed record ProjectOverviewReaction(Guid? Id, string Name, string Rx, string Ry, string Moment)
{
    public bool IsEditable => Id is not null;
}

/// <summary>Read-only display projection of a committed document and its current analysis.</summary>
public sealed class ProjectOverviewState
{
    private ProjectOverviewState(EditorDocument document, AnalysisPresentationState analysis)
    {
        Analysis = analysis;
        Segments = Array.AsReadOnly(document.Segments.Select((segment, index) =>
        {
            var (start, end) = document.SegmentRange(index);
            return new ProjectOverviewSegment(index + 1, Position(start) + "…" + Position(end),
                segment.SectionDisplayName ?? SectionDisplay.Name(segment.Section, analysis.Options),
                MaterialDisplay.Format(segment.Material), segment.BendingAxis.ToString().ToLowerInvariant());
        }).ToArray());
        // The single-segment summary; a beam of several segments is described by Segments only.
        Section = Segments.Count == 1 ? Segments[0].Section : "";
        Material = Segments.Count == 1 ? Segments[0].Material : "";
        Length = string.Format(CultureInfo.CurrentUICulture, Strings.OverviewLength, Position(document.Length),
            analysis.Options.Profile[QuantityKind.BeamLength].Symbol);
        Supports = Array.AsReadOnly(document.Supports.Select(s => new ProjectOverviewItem(s.Id, s.Name,
            SupportTypeName(s), "", Position(s.Position))).ToArray());
        IReadOnlyList<ProjectOverviewItem> PointLoads(PointLoadKind kind) => Array.AsReadOnly(document.Loads
            .Where(l => l.Kind == kind).Select(l => new ProjectOverviewItem(l.Id, l.Name,
                kind == PointLoadKind.Force ? "F" : "M",
                QuantityFormatter.FormatInput(l.Value, analysis.Options.Profile[kind == PointLoadKind.Force
                    ? QuantityKind.TransverseForce : QuantityKind.Moment], analysis.Options), Position(l.Position))).ToArray());
        Forces = PointLoads(PointLoadKind.Force);
        Moments = PointLoads(PointLoadKind.Moment);
        // A self-weight intensity is calculated from area and density; a user load's intensity is given.
        string Intensity(double startIntensity, double endIntensity, bool calculated)
        {
            var unit = analysis.Options.Profile[QuantityKind.DistributedLoad];
            if (calculated)
                return QuantityFormatter.FormatResult(startIntensity, QuantityKind.DistributedLoad, analysis.Options);
            // A linear load shows both ends as "q₁ … q₂" with the unit once; a line break may only follow the ellipsis.
            return startIntensity == endIntensity
                ? QuantityFormatter.FormatInput(startIntensity, unit, analysis.Options)
                : QuantityFormatter.FormatInputNumber(startIntensity, unit, analysis.Options) + " … "
                    + QuantityFormatter.FormatInput(endIntensity, unit, analysis.Options).Replace(' ', '\u00A0');
        }
        ProjectOverviewItem Distributed(Guid id, string name, string type, double startIntensity, double endIntensity,
            Length start, Length end, bool calculated = false) =>
            new(id, name, type, Intensity(startIntensity, endIntensity, calculated), Position(start) + "…" + Position(end));
        // One self-weight row per segment, each with its own intensity and range.
        DistributedLoads = Array.AsReadOnly(document.SelfWeightLoads
            .Select(weight => Distributed(document.SelfWeight!.Id, EditorSelfWeight.Symbol, EditorSelfWeight.Symbol,
                weight.StartIntensity.NewtonsPerMeter, weight.EndIntensity.NewtonsPerMeter, weight.StartPosition,
                weight.EndPosition, calculated: true))
            .Concat(document.DistributedLoads.Select(l => Distributed(l.Id, l.Name, "q", l.StartIntensity.NewtonsPerMeter,
                l.EndIntensity.NewtonsPerMeter, l.StartPosition, l.EndPosition))).ToArray());
        // The solver reports one common reaction per station and restrained direction. Supports sharing
        // a station therefore share one row; no individual split is invented for them.
        Reactions = Array.AsReadOnly(analysis.Result is { } result
            ? document.Supports.GroupBy(s => s.Position).Select(station =>
            {
                var node = result.Solution.Nodes.Single(n => n.Position == station.Key);
                return new ProjectOverviewReaction(station.Count() == 1 ? station.First().Id : null,
                    string.Join(" + ", station.Select(s => s.Name)),
                    node.ReactionX is { } rx ? Reaction(rx.Newtons, QuantityKind.AxialForce) : "",
                    node.ReactionY is { } ry ? Reaction(ry.Newtons, QuantityKind.TransverseForce) : "",
                    node.ReactionMoment is { } moment ? Reaction(moment.NewtonMeters, QuantityKind.Moment) : "");
            }).ToArray()
            : []);
    }

    public static ProjectOverviewState From(EditorDocument document, AnalysisPresentationState analysis) => new(document, analysis);

    public IReadOnlyList<ProjectOverviewSegment> Segments { get; }
    public bool HasSeveralSegments => Segments.Count > 1;
    public string Section { get; }
    public string Material { get; }
    public string Length { get; }
    public IReadOnlyList<ProjectOverviewItem> Supports { get; }
    public IReadOnlyList<ProjectOverviewItem> Forces { get; }
    public IReadOnlyList<ProjectOverviewItem> Moments { get; }
    public IReadOnlyList<ProjectOverviewItem> DistributedLoads { get; }
    public IReadOnlyList<ProjectOverviewReaction> Reactions { get; }
    public AnalysisPresentationState Analysis { get; }
    public bool HasSupports => Supports.Count > 0;
    public bool HasForces => Forces.Count > 0;
    public bool HasMoments => Moments.Count > 0;
    public bool HasDistributedLoads => DistributedLoads.Count > 0;
    public bool HasReactions => Reactions.Count > 0;
    public string ReactionXHeader => ReactionXHeaderText.PlainText;
    public TechnicalText ReactionXHeaderText => TechnicalSymbols.ReactionX + " [" + Analysis.Options.Profile[QuantityKind.AxialForce].Symbol + "]";
    public string ReactionYHeader => ReactionYHeaderText.PlainText;
    public TechnicalText ReactionYHeaderText => TechnicalSymbols.ReactionY + " [" + Analysis.Options.Profile[QuantityKind.TransverseForce].Symbol + "]";
    public string ReactionMomentHeader => ReactionMomentHeaderText.PlainText;
    public TechnicalText ReactionMomentHeaderText => TechnicalSymbols.Moment + " [" + Analysis.Options.Profile[QuantityKind.Moment].Symbol + "]";
    public string PositionHeader => string.Format(CultureInfo.CurrentUICulture, Strings.OverviewPosition,
        Analysis.Options.Profile[QuantityKind.BeamLength].Symbol);

    private string Reaction(double siValue, QuantityKind kind) => QuantityFormatter.FormatResultNumber(siValue, kind,
        Analysis.Options);

    private string Position(Length position) => QuantityFormatter.FormatInputNumber(position.Meters, Analysis.Options.Profile[QuantityKind.BeamLength],
        Analysis.Options);

    private string SupportTypeName(EditorSupport support) => support.Type switch
    {
        SupportType.Fixed => Strings.FixedSupport,
        SupportType.Pinned => Strings.PinnedSupport,
        SupportType.Roller => Strings.RollerSupport,
        SupportType.Spring => SpringDescription(support.Spring!),
        _ => throw new ArgumentOutOfRangeException(nameof(support))
    };

    /// <summary>The spring support with its stiffness values in the display units, one per acting spring.</summary>
    private string SpringDescription(SpringData spring)
    {
        var options = Analysis.Options;
        var parts = new List<string> { Strings.SpringSupport };
        if (spring.Translational is { } translational)
            parts.Add("k = " + QuantityFormatter.FormatInput(translational.NewtonsPerMeter,
                options.Profile[QuantityKind.TranslationalStiffness], options));
        if (spring.RigidTranslation)
            parts.Add(Strings.SpringRigidTranslation);
        if (spring.Rotational is { } rotational)
            parts.Add("kθ = " + QuantityFormatter.FormatInput(rotational.NewtonMetersPerRadian,
                options.Profile[QuantityKind.RotationalStiffness], options));
        return string.Join(", ", parts);
    }
}
