using SpanDraft.Core.Sections;
using SpanDraft.Core.Units;
using static SpanDraft.Desktop.Persistence.ProjectFileCodec;

namespace SpanDraft.Desktop.Persistence;

/// <summary>Historical V1 input migrates to the current contract without file I/O or invented data.</summary>
internal static class ProjectFileV1Reader
{
    internal static ProjectFileV2 Migrate(ProjectFileV1 file)
    {
        try
        {
            Required(file, "project");
            if (file.Format != Format) throw new ProjectFormatException("Not a SpanDraft project.");
            if (file.FormatVersion != 1) throw new ProjectFormatException("Unsupported project format version.");
            var d = Required(file.Document, "document");
            var m = Required(d.Material, "material");
            var s = Required(d.Section, "section");
            var n = Required(d.NamingState, "namingState");
            var p = Required(file.Presentation, "presentation");
            Length L(double? value, string field) => Length.FromMeters(Number(value, field));
            Section legacy = s.Type switch
            {
                "rectangle" => new RectangleSection(L(s.Width, "width"), L(s.Height, "height")),
                "rectangularHollow" => new RectangularHollowSection(L(s.Width, "width"), L(s.Height, "height"),
                    L(s.WallThickness, "wallThickness")),
                "circle" => new CircleSection(L(s.Diameter, "diameter")),
                "circularHollow" => new CircularHollowSection(L(s.OuterDiameter, "outerDiameter"), L(s.WallThickness, "wallThickness")),
                "custom" => new CustomSection(Area.FromSquareMeters(Number(s.Area, "area")),
                    SecondMomentOfArea.FromMetersToTheFourth(Number(s.SecondMomentOfArea, "secondMomentOfArea")),
                    SectionModulus.FromCubicMeters(Number(s.SectionModulus, "sectionModulus"))),
                _ => throw new ProjectFormatException("Unknown section type.")
            };
            // Preserve V1's validation of all known optional fields, even those irrelevant to this shape.
            foreach (var value in new[] { s.Width, s.Height, s.WallThickness, s.Diameter, s.OuterDiameter,
                s.Area, s.SecondMomentOfArea, s.SectionModulus })
                if (value is { } v) Number(v, "section");
            return new()
            {
                Format = Format, FormatVersion = ProjectFileCodec.Version,
                Document = new()
                {
                    Length = d.Length, BendingAxis = "y", Section = SectionToDto(legacy),
                    Material = new() { Name = m.Name, YoungsModulus = m.YoungsModulus, YieldStrength = m.YieldStrength },
                    Supports = Required(d.Supports, "supports").Select(entry =>
                    {
                        var x = Required(entry, "support");
                        return new SupportV2 { Id = x.Id, Name = x.Name, Type = x.Type, Position = x.Position };
                    }).ToArray(),
                    PointLoads = Required(d.PointLoads, "pointLoads").Select(entry =>
                    {
                        var x = Required(entry, "pointLoad");
                        return new PointLoadV2 { Id = x.Id, Name = x.Name, Type = x.Type, Position = x.Position, Value = x.Value };
                    }).ToArray(),
                    DistributedLoads = Required(d.DistributedLoads, "distributedLoads").Select(entry =>
                    {
                        var x = Required(entry, "distributedLoad");
                        return new DistributedLoadV2 { Id = x.Id, Name = x.Name, StartPosition = x.StartPosition,
                            EndPosition = x.EndPosition, Intensity = x.Intensity };
                    }).ToArray(),
                    NamingState = new() { NextSupportOrdinal = n.NextSupportOrdinal, NextForceNumber = n.NextForceNumber,
                        NextMomentNumber = n.NextMomentNumber, NextDistributedLoadNumber = n.NextDistributedLoadNumber }
                },
                Presentation = new()
                {
                    AnnotationOffsets = Required(p.AnnotationOffsets, "annotationOffsets").Select(entry =>
                    {
                        var x = Required(entry, "annotationOffset");
                        return new OffsetV2 { EntityId = x.EntityId, Dx = x.Dx, Dy = x.Dy };
                    }).ToArray()
                }
            };
        }
        catch (Exception e) when (e is ArgumentException or OverflowException)
        { throw new ProjectFormatException("Invalid V1 project data: " + e.Message, e); }
    }
}
