using System.Globalization;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Resources;

namespace SpanDraft.Desktop.State;

public sealed record ProjectOverviewItem(Guid Id, string Name, string Type, string Value, string Position);
public sealed record ProjectOverviewReaction(Guid Id, string Name, string Rx, string Ry, string Moment);

/// <summary>Read-only display projection of a committed document and its current analysis.</summary>
public sealed class ProjectOverviewState
{
    private ProjectOverviewState(EditorDocument document, AnalysisPresentationState analysis)
    {
        Section = SectionDisplay.Name(document.Section);
        Material = document.Material.Name;
        Length = string.Format(CultureInfo.CurrentUICulture, Strings.OverviewLength, Position(document.Length));
        Analysis = analysis;
        Supports = Array.AsReadOnly(document.Supports.Select(s => new ProjectOverviewItem(s.Id, s.Name,
            SupportTypeName(s.Type), "", Position(s.Position))).ToArray());
        Loads = Array.AsReadOnly(document.Loads.Select(l => new ProjectOverviewItem(l.Id, l.Name,
            l.Kind == PointLoadKind.Force ? "F" : "M",
            UiNumbers.Compact(l.Value) + (l.Kind == PointLoadKind.Force ? " N" : " Nm"), Position(l.Position)))
            .Concat(document.DistributedLoads.Select(l => new ProjectOverviewItem(l.Id, l.Name,
                "q", UiNumbers.Compact(l.Intensity.NewtonsPerMeter) + " N/m",
                Position(l.StartPosition) + "…" + Position(l.EndPosition))))
            .ToArray());
        Reactions = Array.AsReadOnly(analysis.Result is { } result
            ? document.Supports.Select(s =>
            {
                var node = result.Solution.Nodes.Single(n => n.Position == s.Position);
                return new ProjectOverviewReaction(s.Id, s.Name,
                    node.ReactionX is { } rx ? Reaction(rx.Newtons, QuantityKind.AxialForce) : "",
                    node.ReactionY is { } ry ? Reaction(ry.Newtons, QuantityKind.TransverseForce) : "",
                    node.ReactionMoment is { } moment ? Reaction(moment.NewtonMeters, QuantityKind.Moment) : "");
            }).ToArray()
            : []);
    }

    public static ProjectOverviewState From(EditorDocument document, AnalysisPresentationState analysis) => new(document, analysis);

    public string Section { get; }
    public string Material { get; }
    public string Length { get; }
    public IReadOnlyList<ProjectOverviewItem> Supports { get; }
    public IReadOnlyList<ProjectOverviewItem> Loads { get; }
    public IReadOnlyList<ProjectOverviewReaction> Reactions { get; }
    public AnalysisPresentationState Analysis { get; }
    public bool HasSupports => Supports.Count > 0;
    public bool HasLoads => Loads.Count > 0;
    public string ReactionXHeader => "Rx [" + Analysis.Options.Profile[QuantityKind.AxialForce].Symbol + "]";
    public string ReactionYHeader => "Ry [" + Analysis.Options.Profile[QuantityKind.TransverseForce].Symbol + "]";
    public string ReactionMomentHeader => "M [" + Analysis.Options.Profile[QuantityKind.Moment].Symbol + "]";

    private string Reaction(double siValue, QuantityKind kind) => QuantityFormatter.FormatNumber(siValue, kind,
        Analysis.Options.Profile, Analysis.Options.Mode, Analysis.References);

    private static string Position(Length position) => UiNumbers.Compact(position.Millimeters);

    private static string SupportTypeName(SupportType type) => type switch
    {
        SupportType.Fixed => Strings.FixedSupport,
        SupportType.Pinned => Strings.PinnedSupport,
        SupportType.Roller => Strings.RollerSupport,
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };
}
