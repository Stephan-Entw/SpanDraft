using System.Text.Json.Serialization;

namespace SpanDraft.Desktop.Persistence;

// Independent V1 library wire contract; not a project DTO.
internal sealed record SectionLibraryV1
{
    public required string Format { get; init; }
    public required int FormatVersion { get; init; }
    public required SectionLibraryEntryV1[] Entries { get; init; }
}

internal sealed record SectionLibraryEntryV1
{
    public required string Name { get; init; }
    public required LibrarySectionV1 Section { get; init; }
}

[JsonConverter(typeof(LibrarySectionV1Converter))]
internal abstract record LibrarySectionV1
{
    public abstract string Type { get; }
}

internal sealed record RectangleSectionLibraryV1 : LibrarySectionV1
{
    public override string Type => "rectangle";
    public required double Width { get; init; }
    public required double Height { get; init; }
}

internal sealed record RectangularHollowSectionLibraryV1 : LibrarySectionV1
{
    public override string Type => "rectangularHollow";
    public required double Width { get; init; }
    public required double Height { get; init; }
    public required double WallThickness { get; init; }
    public required double OuterRadius { get; init; }
}

internal sealed record CircleSectionLibraryV1 : LibrarySectionV1
{
    public override string Type => "circle";
    public required double Diameter { get; init; }
}

internal sealed record CircularHollowSectionLibraryV1 : LibrarySectionV1
{
    public override string Type => "circularHollow";
    public required double OuterDiameter { get; init; }
    public required double WallThickness { get; init; }
}

internal sealed record ISectionLibraryV1 : LibrarySectionV1
{
    public override string Type => "iSection";
    public required double Height { get; init; }
    public required double Width { get; init; }
    public required double WebThickness { get; init; }
    public required double FlangeThickness { get; init; }
    public required double Radius { get; init; }
}

internal sealed record USectionLibraryV1 : LibrarySectionV1
{
    public override string Type => "uSection";
    public required double Height { get; init; }
    public required double Width { get; init; }
    public required double WebThickness { get; init; }
    public required double FlangeThickness { get; init; }
    public required double Radius { get; init; }
}

internal sealed record TSectionLibraryV1 : LibrarySectionV1
{
    public override string Type => "tSection";
    public required double Height { get; init; }
    public required double Width { get; init; }
    public required double WebThickness { get; init; }
    public required double FlangeThickness { get; init; }
    public required double Radius { get; init; }
}

internal sealed record AngleSectionLibraryV1 : LibrarySectionV1
{
    public override string Type => "angle";
    public required double Width { get; init; }
    public required double Height { get; init; }
    public required double Thickness { get; init; }
    public required double InnerRadius { get; init; }
}

internal sealed record ManualSectionLibraryV1 : LibrarySectionV1
{
    public override string Type => "manual";
    public required double Area { get; init; }
    public required LibraryManualAxisV1[] Axes { get; init; }
}

internal sealed record LibraryManualAxisV1
{
    public required string Designation { get; init; }
    public required double SecondMomentOfArea { get; init; }
    public required double SectionModulus { get; init; }
}
