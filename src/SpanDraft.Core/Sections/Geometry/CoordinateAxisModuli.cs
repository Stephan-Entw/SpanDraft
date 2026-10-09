using SpanDraft.Core.Units;

namespace SpanDraft.Core.Sections.Geometry;

/// <summary>Coordinate-axis moduli: positive sides are +z for y-y and +y for z-z.</summary>
internal readonly record struct CoordinateAxisModuli(
    SectionModulus WyPositive, SectionModulus WyNegative,
    SectionModulus WzPositive, SectionModulus WzNegative);
