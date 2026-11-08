using SpanDraft.Core.Sections.Parametric;
using SpanDraft.Core.Units;

namespace SpanDraft.Tests;

internal static class SectionTestShapes
{

    internal static IParametricSectionDefinition Shape(int kind, double b = 6, double h = 10, double t = 1, double tf = 1, double r = 0) => kind switch
    {
        0 => new RectangleSectionGeometry(Length.FromMeters(b), Length.FromMeters(h)),
        1 => new RectangularHollowSectionGeometry(Length.FromMeters(b), Length.FromMeters(h), Length.FromMeters(t), Length.FromMeters(r)),
        2 => new CircleSectionGeometry(Length.FromMeters(b)),
        3 => new CircularHollowSectionGeometry(Length.FromMeters(b), Length.FromMeters(t)),
        4 => new ISectionGeometry(Length.FromMeters(h), Length.FromMeters(b), Length.FromMeters(t), Length.FromMeters(tf), Length.FromMeters(r)),
        5 => new USectionGeometry(Length.FromMeters(h), Length.FromMeters(b), Length.FromMeters(t), Length.FromMeters(tf), Length.FromMeters(r)),
        6 => new TSectionGeometry(Length.FromMeters(h), Length.FromMeters(b), Length.FromMeters(t), Length.FromMeters(tf), Length.FromMeters(r)),
        7 => new AngleSectionGeometry(Length.FromMeters(b), Length.FromMeters(h), Length.FromMeters(t), Length.FromMeters(r)),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}
