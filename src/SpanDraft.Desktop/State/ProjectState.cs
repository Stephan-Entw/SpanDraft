using SpanDraft.Core.Materials;
using SpanDraft.Core.Sections;

namespace SpanDraft.Desktop.State;

/// <summary>The complete immutable, persistent project. Interaction and analysis are deliberately absent.</summary>
public sealed record ProjectState(EditorDocument Document, EditorPresentationState Presentation)
{
    public bool ContentEquals(ProjectState other) => DocumentContentEquals(Document, other.Document)
        && Presentation.ContentEquals(other.Presentation);

    public static bool DocumentContentEquals(EditorDocument a, EditorDocument b) => a.Length == b.Length
        && MaterialContentEquals(a.Material, b.Material) && SectionContentEquals(a.Section, b.Section)
        && a.Supports.SequenceEqual(b.Supports) && a.Loads.SequenceEqual(b.Loads)
        && a.DistributedLoads.SequenceEqual(b.DistributedLoads) && a.NamingState == b.NamingState;

    private static bool MaterialContentEquals(Material a, Material b) => a.Name == b.Name
        && a.YoungsModulus == b.YoungsModulus && a.YieldStrength == b.YieldStrength;

    private static bool SectionContentEquals(Section a, Section b) => (a, b) switch
    {
        (RectangleSection x, RectangleSection y) => x.Width == y.Width && x.Height == y.Height,
        (RectangularHollowSection x, RectangularHollowSection y) => x.Width == y.Width && x.Height == y.Height
            && x.WallThickness == y.WallThickness,
        (CircleSection x, CircleSection y) => x.Diameter == y.Diameter,
        (CircularHollowSection x, CircularHollowSection y) => x.OuterDiameter == y.OuterDiameter
            && x.WallThickness == y.WallThickness,
        (CustomSection x, CustomSection y) => x.Area == y.Area && x.SecondMomentOfArea == y.SecondMomentOfArea
            && x.SectionModulus == y.SectionModulus,
        _ => false
    };
}
