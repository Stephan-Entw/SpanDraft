using SpanDraft.Core.Materials;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Sections.Parametric;

namespace SpanDraft.Desktop.State;

/// <summary>The complete immutable, persistent project. Interaction and analysis are deliberately absent.</summary>
public sealed record ProjectState(EditorDocument Document, EditorPresentationState Presentation)
{
    public bool ContentEquals(ProjectState other) => DocumentContentEquals(Document, other.Document)
        && Presentation.ContentEquals(other.Presentation);

    public static bool DocumentContentEquals(EditorDocument a, EditorDocument b) => a.Length == b.Length
        && a.BendingAxis == b.BendingAxis && MaterialContentEquals(a.Material, b.Material) && SectionContentEquals(a.Section, b.Section)
        && a.Supports.SequenceEqual(b.Supports) && a.Loads.SequenceEqual(b.Loads)
        && a.DistributedLoads.SequenceEqual(b.DistributedLoads) && a.NamingState == b.NamingState;

    private static bool MaterialContentEquals(Material a, Material b) => a.Name == b.Name
        && a.YoungsModulus == b.YoungsModulus && a.YieldStrength == b.YieldStrength
        && a.Density == b.Density && a.PoissonRatio == b.PoissonRatio;

    private static bool SectionContentEquals(ISectionDefinition a, ISectionDefinition b)
    {
        if (ReferenceEquals(a, b)) return true;
        return (SectionDefinitionCompatibility.Normalize(a), SectionDefinitionCompatibility.Normalize(b)) switch
        {
            (RectangleSectionGeometry x, RectangleSectionGeometry y) => x.Width == y.Width && x.Height == y.Height,
            (RectangularHollowSectionGeometry x, RectangularHollowSectionGeometry y) => x.Width == y.Width
                && x.Height == y.Height && x.WallThickness == y.WallThickness && x.OuterRadius == y.OuterRadius,
            (CircleSectionGeometry x, CircleSectionGeometry y) => x.Diameter == y.Diameter,
            (CircularHollowSectionGeometry x, CircularHollowSectionGeometry y) => x.OuterDiameter == y.OuterDiameter
                && x.WallThickness == y.WallThickness,
            (ISectionGeometry x, ISectionGeometry y) => x.Height == y.Height && x.Width == y.Width
                && x.WebThickness == y.WebThickness && x.FlangeThickness == y.FlangeThickness && x.Radius == y.Radius,
            (USectionGeometry x, USectionGeometry y) => x.Height == y.Height && x.Width == y.Width
                && x.WebThickness == y.WebThickness && x.FlangeThickness == y.FlangeThickness && x.Radius == y.Radius,
            (TSectionGeometry x, TSectionGeometry y) => x.Height == y.Height && x.Width == y.Width
                && x.WebThickness == y.WebThickness && x.FlangeThickness == y.FlangeThickness && x.Radius == y.Radius,
            (AngleSectionGeometry x, AngleSectionGeometry y) => x.Width == y.Width && x.Height == y.Height
                && x.Thickness == y.Thickness && x.InnerRadius == y.InnerRadius,
            (ManualSectionDefinition x, ManualSectionDefinition y) => x.Area == y.Area && x.Axes.SequenceEqual(y.Axes),
            _ => false
        };
    }
}
