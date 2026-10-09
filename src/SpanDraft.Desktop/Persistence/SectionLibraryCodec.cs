using System.Text.Json;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Sections.Parametric;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Libraries;
using SpanDraft.Desktop.State;
using static SpanDraft.Desktop.Persistence.LibraryJson;

namespace SpanDraft.Desktop.Persistence;

public static class SectionLibraryCodec
{
    public const string Format = "SpanDraft.SectionLibrary";
    public const int Version = 1;

    public static byte[] Serialize(UserSectionLibrary library)
    {
        ArgumentNullException.ThrowIfNull(library);
        return JsonSerializer.SerializeToUtf8Bytes(new SectionLibraryV1
        {
            Format = Format, FormatVersion = Version,
            Entries = library.All.Select(entry => new SectionLibraryEntryV1
                { Name = entry.Name, Section = SectionToDto(entry.Section) }).ToArray()
        }, Options);
    }

    public static UserSectionLibrary Deserialize(ReadOnlySpan<byte> bytes) =>
        Read<SectionLibraryV1, UserSectionLibrary>(bytes, Format, Version, file =>
            new(Required(file.Entries, "entries").Select(entry =>
            {
                Required(entry, "entry");
                return new SectionLibraryEntry(Required(entry.Name, "name"),
                    SectionFromDto(Required(entry.Section, "section")));
            })));

    private static Length L(double value, string field) => Length.FromMeters(Number(value, field));

    private static LibrarySectionV1 SectionToDto(ISectionDefinition section) =>
        SectionDefinitionCompatibility.Normalize(section) switch
        {
            RectangleSectionGeometry s => new RectangleSectionLibraryV1
            { Width = s.Width.Meters, Height = s.Height.Meters },
            RectangularHollowSectionGeometry s => new RectangularHollowSectionLibraryV1
            { Width = s.Width.Meters, Height = s.Height.Meters, WallThickness = s.WallThickness.Meters, OuterRadius = s.OuterRadius.Meters },
            CircleSectionGeometry s => new CircleSectionLibraryV1
            { Diameter = s.Diameter.Meters },
            CircularHollowSectionGeometry s => new CircularHollowSectionLibraryV1
            { OuterDiameter = s.OuterDiameter.Meters, WallThickness = s.WallThickness.Meters },
            ISectionGeometry s => new ISectionLibraryV1
            { Height = s.Height.Meters, Width = s.Width.Meters, WebThickness = s.WebThickness.Meters, FlangeThickness = s.FlangeThickness.Meters, Radius = s.Radius.Meters },
            USectionGeometry s => new USectionLibraryV1
            { Height = s.Height.Meters, Width = s.Width.Meters, WebThickness = s.WebThickness.Meters, FlangeThickness = s.FlangeThickness.Meters, Radius = s.Radius.Meters },
            TSectionGeometry s => new TSectionLibraryV1
            { Height = s.Height.Meters, Width = s.Width.Meters, WebThickness = s.WebThickness.Meters, FlangeThickness = s.FlangeThickness.Meters, Radius = s.Radius.Meters },
            AngleSectionGeometry s => new AngleSectionLibraryV1
            { Width = s.Width.Meters, Height = s.Height.Meters, Thickness = s.Thickness.Meters, InnerRadius = s.InnerRadius.Meters },
            ManualSectionDefinition s => new ManualSectionLibraryV1
            {
                Area = s.Area.SquareMeters,
                Axes = s.Axes.Select(a => new LibraryManualAxisV1 { Designation = AxisToWire(a.AxisDesignation),
                    SecondMomentOfArea = a.SecondMomentOfArea.MetersToTheFourth,
                    SectionModulus = a.PositiveSectionModulus.CubicMeters }).ToArray()
            },
            _ => throw new LibraryFormatException("Unknown section type.")
        };

    private static ISectionDefinition SectionFromDto(LibrarySectionV1 section) => section switch
    {
        RectangleSectionLibraryV1 s => new RectangleSectionGeometry(L(s.Width, "width"), L(s.Height, "height")),
        RectangularHollowSectionLibraryV1 s => new RectangularHollowSectionGeometry(L(s.Width, "width"), L(s.Height, "height"), L(s.WallThickness, "wallThickness"), L(s.OuterRadius, "outerRadius")),
        CircleSectionLibraryV1 s => new CircleSectionGeometry(L(s.Diameter, "diameter")),
        CircularHollowSectionLibraryV1 s => new CircularHollowSectionGeometry(L(s.OuterDiameter, "outerDiameter"), L(s.WallThickness, "wallThickness")),
        ISectionLibraryV1 s => new ISectionGeometry(L(s.Height, "height"), L(s.Width, "width"), L(s.WebThickness, "webThickness"), L(s.FlangeThickness, "flangeThickness"), L(s.Radius, "radius")),
        USectionLibraryV1 s => new USectionGeometry(L(s.Height, "height"), L(s.Width, "width"), L(s.WebThickness, "webThickness"), L(s.FlangeThickness, "flangeThickness"), L(s.Radius, "radius")),
        TSectionLibraryV1 s => new TSectionGeometry(L(s.Height, "height"), L(s.Width, "width"), L(s.WebThickness, "webThickness"), L(s.FlangeThickness, "flangeThickness"), L(s.Radius, "radius")),
        AngleSectionLibraryV1 s => new AngleSectionGeometry(L(s.Width, "width"), L(s.Height, "height"), L(s.Thickness, "thickness"), L(s.InnerRadius, "innerRadius")),
        ManualSectionLibraryV1 s => ManualFromDto(s),
        _ => throw new LibraryFormatException("Unknown section type.")
    };

    private static ManualSectionDefinition ManualFromDto(ManualSectionLibraryV1 section)
    {
        var axes = Required(section.Axes, "axes");
        if (axes.Length is < 1 or > 2) throw new LibraryFormatException("Manual sections require one or two axes.");
        ManualSectionAxis Map(LibraryManualAxisV1 entry)
        {
            var a = Required(entry, "axis");
            return new(AxisFromWire(a.Designation),
                SecondMomentOfArea.FromMetersToTheFourth(Number(a.SecondMomentOfArea, "secondMomentOfArea")),
                SectionModulus.FromCubicMeters(Number(a.SectionModulus, "sectionModulus")));
        }
        return new(Area.FromSquareMeters(Number(section.Area, "area")), Map(axes[0]), axes.Length == 2 ? Map(axes[1]) : null);
    }

    private static string AxisToWire(SectionAxisDesignation axis) => axis switch
    {
        SectionAxisDesignation.Y => "y", SectionAxisDesignation.Z => "z",
        SectionAxisDesignation.U => "u", SectionAxisDesignation.V => "v",
        _ => throw new LibraryFormatException("Unknown section axis.")
    };

    private static SectionAxisDesignation AxisFromWire(string value) => value switch
    {
        "y" => SectionAxisDesignation.Y, "z" => SectionAxisDesignation.Z,
        "u" => SectionAxisDesignation.U, "v" => SectionAxisDesignation.V,
        _ => throw new LibraryFormatException("Unknown section axis.")
    };
}
