using System.Text.Json;
using System.Text.Json.Serialization;
using SpanDraft.Core.Materials;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Core.Validation;
using SpanDraft.Desktop.State;

namespace SpanDraft.Desktop.Persistence;

// These DTOs are the wire contract, never serialized Core or editor types.
public sealed record ProjectFileV1
{
    public required string Format { get; init; }
    public required int FormatVersion { get; init; }
    public required DocumentV1 Document { get; init; }
    public required PresentationV1 Presentation { get; init; }
}

public sealed record DocumentV1
{
    public required double Length { get; init; }
    public required MaterialV1 Material { get; init; }
    public required SectionV1 Section { get; init; }
    public required SupportV1[] Supports { get; init; }
    public required PointLoadV1[] PointLoads { get; init; }
    public required DistributedLoadV1[] DistributedLoads { get; init; }
    public required NamingV1 NamingState { get; init; }
}

public sealed record MaterialV1
{
    public required string Name { get; init; }
    public required double YoungsModulus { get; init; }
    public required double YieldStrength { get; init; }
}

public sealed record SectionV1
{
    public required string Type { get; init; }
    public double? Width { get; init; }
    public double? Height { get; init; }
    public double? WallThickness { get; init; }
    public double? Diameter { get; init; }
    public double? OuterDiameter { get; init; }
    public double? Area { get; init; }
    public double? SecondMomentOfArea { get; init; }
    public double? SectionModulus { get; init; }
}

public sealed record SupportV1
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string Type { get; init; }
    public required double Position { get; init; }
}

public sealed record PointLoadV1
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string Type { get; init; }
    public required double Position { get; init; }
    public required double Value { get; init; }
}

public sealed record DistributedLoadV1
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required double StartPosition { get; init; }
    public required double EndPosition { get; init; }
    public required double Intensity { get; init; }
}

public sealed record NamingV1
{
    public required long NextSupportOrdinal { get; init; }
    public required long NextForceNumber { get; init; }
    public required long NextMomentNumber { get; init; }
    public required long NextDistributedLoadNumber { get; init; }
}

public sealed record PresentationV1
{
    public required OffsetV1[] AnnotationOffsets { get; init; }
}

public sealed record OffsetV1
{
    public required Guid EntityId { get; init; }
    public required double Dx { get; init; }
    public required double Dy { get; init; }
}

public sealed class ProjectFormatException(string message, Exception? inner = null) : Exception(message, inner);

public static class ProjectFileCodec
{
    public const string Format = "SpanDraft.Project";
    public const int Version = 1;
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        RespectNullableAnnotations = true,
        AllowDuplicateProperties = false
    };

    public static byte[] Serialize(ProjectState state) => JsonSerializer.SerializeToUtf8Bytes(ToDto(state), JsonOptions);

    public static ProjectState Deserialize(ReadOnlySpan<byte> json)
    {
        try
        {
            return FromDto(JsonSerializer.Deserialize<ProjectFileV1>(json, JsonOptions)
                ?? throw new ProjectFormatException("Project root must be an object."));
        }
        catch (Exception e) when (e is JsonException or ArgumentException or OverflowException)
        {
            throw new ProjectFormatException("Invalid project data: " + e.Message, e);
        }
    }

    public static ProjectFileV1 ToDto(ProjectState state)
    {
        var d = state.Document;
        var n = d.NamingState;
        return new()
        {
            Format = Format, FormatVersion = Version,
            Document = new()
            {
                Length = d.Length.Meters,
                Material = new() { Name = d.Material.Name, YoungsModulus = d.Material.YoungsModulus.Pascals,
                    YieldStrength = d.Material.YieldStrength.Pascals },
                Section = SectionToDto(d.Section),
                Supports = d.Supports.Select(s => new SupportV1 { Id = s.Id, Name = s.Name,
                    Type = s.Type switch { SupportType.Fixed => "fixed", SupportType.Pinned => "pinned",
                        SupportType.Roller => "roller", _ => throw new ProjectFormatException("Unknown support type.") },
                    Position = s.Position.Meters }).ToArray(),
                PointLoads = d.Loads.Select(l => new PointLoadV1 { Id = l.Id, Name = l.Name,
                    Type = l.Kind switch { PointLoadKind.Force => "force", PointLoadKind.Moment => "moment",
                        _ => throw new ProjectFormatException("Unknown point load type.") },
                    Position = l.Position.Meters, Value = l.Value }).ToArray(),
                DistributedLoads = d.DistributedLoads.Select(l => new DistributedLoadV1 { Id = l.Id, Name = l.Name,
                    StartPosition = l.StartPosition.Meters, EndPosition = l.EndPosition.Meters,
                    Intensity = l.Intensity.NewtonsPerMeter }).ToArray(),
                NamingState = new() { NextSupportOrdinal = n.NextSupportOrdinal, NextForceNumber = n.NextForceNumber,
                    NextMomentNumber = n.NextMomentNumber, NextDistributedLoadNumber = n.NextDistributedLoadNumber }
            },
            Presentation = new() { AnnotationOffsets = state.Presentation.AnnotationOffsets.Select(o =>
                new OffsetV1 { EntityId = o.Key, Dx = o.Value.Dx, Dy = o.Value.Dy }).ToArray() }
        };
    }

    private static SectionV1 SectionToDto(Section section) => section switch
    {
        RectangleSection s => new() { Type = "rectangle", Width = s.Width.Meters, Height = s.Height.Meters },
        RectangularHollowSection s => new() { Type = "rectangularHollow", Width = s.Width.Meters,
            Height = s.Height.Meters, WallThickness = s.WallThickness.Meters },
        CircleSection s => new() { Type = "circle", Diameter = s.Diameter.Meters },
        CircularHollowSection s => new() { Type = "circularHollow", OuterDiameter = s.OuterDiameter.Meters,
            WallThickness = s.WallThickness.Meters },
        CustomSection s => new() { Type = "custom", Area = s.Area.SquareMeters,
            SecondMomentOfArea = s.SecondMomentOfArea.MetersToTheFourth, SectionModulus = s.SectionModulus.CubicMeters },
        _ => throw new ProjectFormatException("Unknown section type.")
    };

    public static ProjectState FromDto(ProjectFileV1 file)
    {
        try { return MapValidated(file); }
        catch (Exception e) when (e is ArgumentException or OverflowException)
        { throw new ProjectFormatException("Invalid project data: " + e.Message, e); }
    }

    private static ProjectState MapValidated(ProjectFileV1 file)
    {
        if (file.Format != Format) throw new ProjectFormatException("Not a SpanDraft project.");
        if (file.FormatVersion != Version) throw new ProjectFormatException("Unsupported project format version.");
        var d = Required(file.Document, "document");
        var m = Required(d.Material, "material");
        var s = Required(d.Section, "section");
        var n = Required(d.NamingState, "namingState");
        var p = Required(file.Presentation, "presentation");
        var ids = new HashSet<Guid>();
        Guid EntityId(Guid id)
        {
            if (id == Guid.Empty || !ids.Add(id)) throw new ProjectFormatException("Entity IDs must be non-empty and unique.");
            return id;
        }
        Section section = s.Type switch
        {
            "rectangle" => new RectangleSection(L(s.Width, "width"), L(s.Height, "height")),
            "rectangularHollow" => new RectangularHollowSection(L(s.Width, "width"), L(s.Height, "height"), L(s.WallThickness, "wallThickness")),
            "circle" => new CircleSection(L(s.Diameter, "diameter")),
            "circularHollow" => new CircularHollowSection(L(s.OuterDiameter, "outerDiameter"), L(s.WallThickness, "wallThickness")),
            "custom" => new CustomSection(Area.FromSquareMeters(Number(s.Area, "area")),
                SecondMomentOfArea.FromMetersToTheFourth(Number(s.SecondMomentOfArea, "secondMomentOfArea")),
                SectionModulus.FromCubicMeters(Number(s.SectionModulus, "sectionModulus"))),
            _ => throw new ProjectFormatException("Unknown section type.")
        };
        // Optional known geometry fields must also be finite when present.
        foreach (var value in new[] { s.Width, s.Height, s.WallThickness, s.Diameter, s.OuterDiameter,
            s.Area, s.SecondMomentOfArea, s.SectionModulus })
            if (value is { } v) Number(v, "section");
        var document = new EditorDocument(L(d.Length, "length"),
            new Material(Required(m.Name, "material.name"), Pressure.FromPascals(Number(m.YoungsModulus, "youngsModulus")),
                Pressure.FromPascals(Number(m.YieldStrength, "yieldStrength"))), section,
            Required(d.Supports, "supports").Select(entry =>
            {
                var x = Required(entry, "support");
                return new EditorSupport(EntityId(x.Id), L(x.Position, "position"), x.Type switch
                { "fixed" => SupportType.Fixed, "pinned" => SupportType.Pinned, "roller" => SupportType.Roller,
                    _ => throw new ProjectFormatException("Unknown support type.") }, Name(x.Name));
            }),
            Required(d.PointLoads, "pointLoads").Select(entry =>
            {
                var x = Required(entry, "pointLoad");
                return EditorPointLoad.Create(EntityId(x.Id), L(x.Position, "position"), x.Type switch
                { "force" => PointLoadKind.Force, "moment" => PointLoadKind.Moment,
                    _ => throw new ProjectFormatException("Unknown point load type.") }, Number(x.Value, "value"), Name(x.Name));
            }),
            new NamingState(n.NextSupportOrdinal, n.NextForceNumber, n.NextMomentNumber, n.NextDistributedLoadNumber),
            Required(d.DistributedLoads, "distributedLoads").Select(entry =>
            {
                var x = Required(entry, "distributedLoad");
                return new EditorUniformDistributedLoad(EntityId(x.Id), L(x.StartPosition, "startPosition"),
                    L(x.EndPosition, "endPosition"), ForcePerLength.FromNewtonsPerMeter(Number(x.Intensity, "intensity")), Name(x.Name));
            }));
        var errors = BeamModelValidator.Validate(document.ToBeamModel()).Where(e => e.Code != ValidationErrorCode.MissingSupports).ToArray();
        if (errors.Length > 0) throw new ProjectFormatException(errors[0].Message);
        if (document.Supports.Select(x => x.Position).Distinct().Count() != document.Supports.Count)
            throw new ProjectFormatException("Support positions must be unique.");
        var offsets = new Dictionary<Guid, AnnotationOffset>();
        foreach (var entry in Required(p.AnnotationOffsets, "annotationOffsets"))
        {
            var x = Required(entry, "annotationOffset");
            if (!ids.Contains(x.EntityId) || !offsets.TryAdd(x.EntityId, new(Number(x.Dx, "dx"), Number(x.Dy, "dy"))))
                throw new ProjectFormatException("Annotation offsets must reference existing, unique entities.");
        }
        return new(document, new(offsets));
    }

    private static T Required<T>(T? value, string field) where T : class => value
        ?? throw new ProjectFormatException("Missing or null field: " + field);
    private static double Number(double? value, string field) => value is { } v && double.IsFinite(v) ? v
        : throw new ProjectFormatException("Missing or non-finite number: " + field);
    private static Length L(double? value, string field) => Length.FromMeters(Number(value, field));
    private static string Name(string? value)
    {
        var name = Required(value, "name");
        if (EntityNaming.Normalize(name) != name) throw new ProjectFormatException("Entity names must be trimmed.");
        return name;
    }
}
